using System.Globalization;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;
using YetenekPlatformu.Settings;

namespace YetenekPlatformu.Data;

// Şemanın tamamı docs/sema.md ile uyumludur.
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<UserSkill> UserSkills => Set<UserSkill>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Kullanıcı
        builder.Entity<ApplicationUser>(b =>
        {
            // E-posta sistemde yok: Identity'nin e-posta alanları tabloya yazılmaz
            b.Ignore(u => u.Email);
            b.Ignore(u => u.NormalizedEmail);
            b.Ignore(u => u.EmailConfirmed);

            b.Property(u => u.FirstName).HasMaxLength(50).IsRequired();
            b.Property(u => u.LastName).HasMaxLength(50).IsRequired();
            b.Property(u => u.PhoneNumber).HasMaxLength(15).IsRequired();
            b.Property(u => u.Iban).HasMaxLength(9).IsRequired();
            b.Property(u => u.City).HasMaxLength(30).IsRequired();
            b.Property(u => u.RecoveryCodeHash).HasMaxLength(200);

            // Telefon ve IBAN kişiye özel: aynı değer iki üyede olamaz
            b.HasIndex(u => u.PhoneNumber).IsUnique().HasDatabaseName(MemberUniqueness.PhoneIndexName);
            b.HasIndex(u => u.Iban).IsUnique().HasDatabaseName(MemberUniqueness.IbanIndexName);

            // IBAN: "TR" + tam 7 rakam (ör. TR1234567)
            b.ToTable(t => t.HasCheckConstraint("CK_Users_Iban",
                "LEN([Iban]) = 9 AND [Iban] LIKE 'TR[0-9][0-9][0-9][0-9][0-9][0-9][0-9]'"));
        });

        // Yetenek
        builder.Entity<Skill>(b =>
        {
            // Türkçe karşılaştırma: "ingi" araması "İngilizce"yi bulur, sıralama Türkçe alfabeye göre
            b.Property(s => s.Name).HasMaxLength(200).IsRequired().UseCollation("Turkish_CI_AS");
            b.Property(s => s.ExternalId).HasMaxLength(200);
            b.HasIndex(s => s.Name).IsUnique();
        });

        // Kullanıcı-Yetenek
        builder.Entity<UserSkill>(b =>
        {
            b.HasKey(us => new { us.UserId, us.SkillId });

            b.HasOne(us => us.User).WithMany()
                .HasForeignKey(us => us.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(us => us.Skill).WithMany(s => s.UserSkills)
                .HasForeignKey(us => us.SkillId).OnDelete(DeleteBehavior.Restrict);
        });

        // Randevu
        builder.Entity<Appointment>(b =>
        {
            b.Property(a => a.MeetingLink).HasMaxLength(500);
            b.Property(a => a.MeetingPlaceName).HasMaxLength(300);
            b.Property(a => a.MeetingLatitude).HasPrecision(9, 6);
            b.Property(a => a.MeetingLongitude).HasPrecision(9, 6);
            b.Property(a => a.CancelledByUserId).HasMaxLength(450);
            b.Property(a => a.CancelReason).HasMaxLength(250);
            b.Property(a => a.ProposedByUserId).HasMaxLength(450);
            // Değişiklik önerisinde önerilen yeni buluşma yeri (boşsa yer değişmiyor)
            b.Property(a => a.ProposedPlaceName).HasMaxLength(300);
            b.Property(a => a.ProposedLatitude).HasPrecision(9, 6);
            b.Property(a => a.ProposedLongitude).HasPrecision(9, 6);
            b.Property(a => a.RowVersion).IsRowVersion();

            b.HasOne(a => a.Student).WithMany()
                .HasForeignKey(a => a.StudentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(a => a.Instructor).WithMany()
                .HasForeignKey(a => a.InstructorId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(a => a.Skill).WithMany()
                .HasForeignKey(a => a.SkillId).OnDelete(DeleteBehavior.Restrict);

            b.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Appointments_DurationHours", "[DurationHours] IN (1, 2)");
                t.HasCheckConstraint("CK_Appointments_ProposedDurationHours",
                    "[ProposedDurationHours] IS NULL OR [ProposedDurationHours] IN (1, 2)");
                // Kişi kendinden randevu alamaz
                t.HasCheckConstraint("CK_Appointments_NotSelf", "[StudentId] <> [InstructorId]");
            });
        });

        // Saat dilimi
        builder.Entity<TimeSlot>(b =>
        {
            // Çakışma koruması: aynı kişinin aynı saati için ikinci satır yazılamaz
            b.HasIndex(ts => new { ts.UserId, ts.SlotStart }).IsUnique();

            b.HasOne(ts => ts.User).WithMany()
                .HasForeignKey(ts => ts.UserId).OnDelete(DeleteBehavior.Restrict);
            // Tek istisna: randevu silinirse saat dilimleri de silinir
            b.HasOne(ts => ts.Appointment).WithMany(a => a.TimeSlots)
                .HasForeignKey(ts => ts.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        });

        // Bekleme listesi
        builder.Entity<WaitlistEntry>(b =>
        {
            b.Property(w => w.MeetingPlaceName).HasMaxLength(300);
            b.Property(w => w.MeetingLatitude).HasPrecision(9, 6);
            b.Property(w => w.MeetingLongitude).HasPrecision(9, 6);

            // Aynı kişi aynı saat için yalnızca bir kez "Bekliyor" durumunda olabilir
            b.HasIndex(w => new { w.UserId, w.InstructorId, w.StartTime })
                .IsUnique()
                .HasFilter($"[Status] = {(int)WaitlistStatus.Waiting}");

            b.HasOne(w => w.User).WithMany()
                .HasForeignKey(w => w.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(w => w.Instructor).WithMany()
                .HasForeignKey(w => w.InstructorId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(w => w.Skill).WithMany()
                .HasForeignKey(w => w.SkillId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(w => w.ActivatedAppointment).WithMany()
                .HasForeignKey(w => w.ActivatedAppointmentId).OnDelete(DeleteBehavior.Restrict);

            b.ToTable(t => t.HasCheckConstraint("CK_WaitlistEntries_DurationHours", "[DurationHours] IN (1, 2)"));
        });

        // Favori
        builder.Entity<Favorite>(b =>
        {
            b.HasKey(f => new { f.UserId, f.InstructorId });

            b.HasOne(f => f.User).WithMany()
                .HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(f => f.Instructor).WithMany()
                .HasForeignKey(f => f.InstructorId).OnDelete(DeleteBehavior.Restrict);

            // Kişi kendini favoriye ekleyemez
            b.ToTable(t => t.HasCheckConstraint("CK_Favorites_NotSelf", "[UserId] <> [InstructorId]"));
        });

        // Ödeme
        builder.Entity<Payment>(b =>
        {
            b.Property(p => p.Amount).HasPrecision(10, 2);

            // Ders başına en fazla bir ödeme
            b.HasIndex(p => p.AppointmentId).IsUnique();

            b.HasOne(p => p.Sender).WithMany()
                .HasForeignKey(p => p.SenderId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.Receiver).WithMany()
                .HasForeignKey(p => p.ReceiverId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.Appointment).WithMany()
                .HasForeignKey(p => p.AppointmentId).OnDelete(DeleteBehavior.Restrict);

            // Tutar yalnızca PaymentOptions listesindeki değerlerden biri olabilir
            var amounts = string.Join(", ",
                PaymentOptions.AllowedAmounts.Select(a => a.ToString(CultureInfo.InvariantCulture)));
            b.ToTable(t => t.HasCheckConstraint("CK_Payments_Amount", $"[Amount] IN ({amounts})"));
        });
    }
}
