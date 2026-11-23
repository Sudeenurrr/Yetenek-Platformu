using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;

namespace YetenekPlatformu.Services;

// Bekleme listesi (docs/sema.md -> "Bekleme Listesi"): eğitmenin saati boşalınca sıradakinin talebi otomatik oluşur.
// Aktifleştirme, saati boşaltan işlemle (iptal, geri çekme, red, süre dolması, üyelik silme) AYNI transaction içinde çağrılır.
// Çakışma: yeni randevunun saat dilimleri TimeSlots (UserId + SlotStart) benzersiz indeksiyle korunur;
// aynı anda başka biri saati aldıysa yalnızca bu kaydın denemesi (savepoint) geri alınır, asıl işlem bozulmaz.
public class WaitlistService(ApplicationDbContext db)
{
    private const string SlotIndexName = "IX_TimeSlots_UserId_SlotStart";
    private const string SavepointName = "WaitlistActivation";

    // Bir aktifleştirme turunun sonucu (günlük ve testler için)
    public record ActivationCounts(int Activated, int Invalidated)
    {
        public static readonly ActivationCounts None = new(0, 0);
        public static ActivationCounts operator +(ActivationCounts a, ActivationCounts b) =>
            new(a.Activated + b.Activated, a.Invalidated + b.Invalidated);
    }

    // Bekleme Listem sayfasında bir satır (telefon ve IBAN yok)
    public record WaitlistRow(
        int Id,
        string InstructorId,
        string InstructorName,
        string SkillName,
        DateTime StartTime,
        byte DurationHours,
        MeetingType MeetingType,
        WaitlistStatus Status,
        string? PlaceName);              // Yüz Yüze buluşma yeri (yalnızca ad; koordinat sayfaya gönderilmez)

    public static string StatusName(WaitlistStatus status) => status switch
    {
        WaitlistStatus.Waiting => "Bekliyor",
        WaitlistStatus.Activated => "Talebe Dönüştü",
        WaitlistStatus.Withdrawn => "Listeden Çıktınız",
        WaitlistStatus.Invalid => "Geçersiz",
        _ => status.ToString()
    };

    // Kişinin bütün kayıtları: bekleyenler üstte (yakın saat önce), diğerleri altta (yeni olan önce)
    public async Task<List<WaitlistRow>> ListAsync(string userId)
    {
        var rows = await db.WaitlistEntries
            .Where(w => w.UserId == userId)
            .Select(w => new WaitlistRow(
                w.Id, w.InstructorId, w.Instructor.FirstName + " " + w.Instructor.LastName, w.Skill.Name,
                w.StartTime, w.DurationHours, w.MeetingType, w.Status,
                w.MeetingType == MeetingType.FaceToFace ? w.MeetingPlaceName : null))
            .ToListAsync();

        return rows
            .OrderBy(r => r.Status == WaitlistStatus.Waiting ? 0 : 1)
            .ThenBy(r => r.Status == WaitlistStatus.Waiting ? r.StartTime.Ticks : -r.StartTime.Ticks)
            .ToList();
    }

    // Listeden çık: yalnızca kaydın sahibi, yalnızca "Bekliyor" durumundaki kayıt
    public async Task<(bool Succeeded, string Message)> WithdrawAsync(int entryId, string userId)
    {
        var updated = await db.WaitlistEntries
            .Where(w => w.Id == entryId && w.UserId == userId && w.Status == WaitlistStatus.Waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WaitlistStatus.Withdrawn));
        if (updated == 1)
            return (true, "Bekleme listesinden çıktınız.");

        return await db.WaitlistEntries.AnyAsync(w => w.Id == entryId && w.UserId == userId)
            ? (false, "Bu kayıt artık beklemede değil.")
            : (false, "Kayıt bulunamadı.");
    }

    // Eğitmenin boşalan saatleri için sıradakileri aktifleştirir. Kayıtlara en eski olandan başlanır:
    //   - saat geçmiş / bekleyenin o saatte başka randevusu var / yetenek kaldırılmış / yüz yüze ama şehirler farklı -> Geçersiz
    //   - kaydın diğer saati (2 saatlik kayıt) hâlâ eğitmende dolu -> beklemeye devam eder, arkasındakine bakılır
    //   - uygunsa Beklemede bir talep oluşur (24 saatlik süre şimdi başlar)
    public async Task<ActivationCounts> ActivateForFreedSlotsAsync(string? instructorId, IEnumerable<DateTime> freedHours)
    {
        var hours = freedHours.Distinct().ToList();
        if (string.IsNullOrEmpty(instructorId) || hours.Count == 0)
            return ActivationCounts.None;

        var candidates = await db.WaitlistEntries
            .Where(w => w.InstructorId == instructorId && w.Status == WaitlistStatus.Waiting
                && (hours.Contains(w.StartTime) || (w.DurationHours == 2 && hours.Contains(w.StartTime.AddHours(1)))))
            .OrderBy(w => w.CreatedAt).ThenBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync();

        var counts = ActivationCounts.None;
        foreach (var entryId in candidates)
            counts += await TryActivateAsync(entryId);
        return counts;
    }

    // BackgroundService turu: saati geçmiş bekleyen kayıtlar Geçersiz olur; eğitmen saati boş olduğu halde
    // bekleyen kayıtlar (ör. kişi sıraya girerken saat de tam o anda boşaldıysa) aktifleştirilir.
    public async Task<ActivationCounts> SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = AppClock.Now;
        var invalidated = await db.WaitlistEntries
            .Where(w => w.Status == WaitlistStatus.Waiting && w.StartTime <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WaitlistStatus.Invalid), cancellationToken);

        // Eğitmenin ilk saati boş görünen kayıtlar (süresi dolmuş talepleri aynı turda önce görev kapatır)
        var groups = await db.WaitlistEntries
            .Where(w => w.Status == WaitlistStatus.Waiting
                && !db.TimeSlots.Any(ts => ts.UserId == w.InstructorId && ts.SlotStart == w.StartTime))
            .Select(w => new { w.InstructorId, w.StartTime, w.DurationHours })
            .ToListAsync(cancellationToken);

        var counts = new ActivationCounts(0, invalidated);
        foreach (var group in groups.GroupBy(g => g.InstructorId))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var hours = group.SelectMany(g => Enumerable.Range(0, g.DurationHours).Select(i => g.StartTime.AddHours(i)));
            counts += await ActivateForFreedSlotsAsync(group.Key, hours);
            await transaction.CommitAsync(cancellationToken);
        }
        return counts;
    }

    // Süresi dolmuş (24 saat geçmiş ya da ders saati gelmiş) ama hâlâ saat tutan bekleyen talepler:
    // Süresi Doldu olur, saatleri silinir ve eğitmenlerin boşalan saatleri için sıradakiler aktifleşir.
    // Yalnızca verilen kişilerin verilen saatlerini tutan talepler işlenir. Çağıran transaction içinde olmalıdır.
    public async Task<ActivationCounts> ExpireStaleRequestsAsync(IEnumerable<string> userIds, IEnumerable<DateTime> slotStarts)
    {
        var now = AppClock.Now;
        var users = userIds.ToList();
        var starts = slotStarts.ToList();

        var expiredIds = await db.TimeSlots
            .Where(ts => users.Contains(ts.UserId) && starts.Contains(ts.SlotStart))
            .Select(ts => ts.Appointment)
            .Where(AppointmentRules.ExpiredPending(now))
            .Select(a => a.Id)
            .Distinct()
            .ToListAsync();

        return (await ExpireAndActivateAsync(expiredIds, now)).Waitlist;
    }

    // Verilen bekleyen talepleri (hâlâ süresi dolmuşsa) Süresi Doldu yapar, saatlerini siler ve sıradakileri aktifleştirir.
    // Durum koşulu yeniden uygulanır: bu arada kullanıcı onayladıysa o kayda dokunulmaz. Çağıran transaction içinde olmalıdır.
    public async Task<(int Expired, ActivationCounts Waitlist)> ExpireAndActivateAsync(IReadOnlyCollection<int> appointmentIds, DateTime now)
    {
        if (appointmentIds.Count == 0)
            return (0, ActivationCounts.None);

        var updated = await db.Appointments
            .Where(a => appointmentIds.Contains(a.Id))
            .Where(AppointmentRules.ExpiredPending(now))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatus.Expired));
        if (updated == 0)
            return (0, ActivationCounts.None);

        var freed = await TakeInstructorSlotsAsync(db.TimeSlots
            .Where(t => appointmentIds.Contains(t.AppointmentId) && t.Appointment.Status == AppointmentStatus.Expired));
        return (updated, await ActivateAsync(freed));
    }

    // Randevunun bütün saat dilimlerini siler ve eğitmenin boşalan saatleri için sıradakileri aktifleştirir
    // (iptal, geri çekme, red). Çağıran transaction içinde olmalıdır.
    public Task<ActivationCounts> FreeSlotsAndActivateAsync(int appointmentId) =>
        ReleaseAsync(db.TimeSlots.Where(t => t.AppointmentId == appointmentId));

    // Verilen saat dilimlerini siler ve eğitmenin boşalan saatleri için sıradakileri aktifleştirir
    // (ör. değişiklik önerisinin geçici saatleri ya da taşınan randevunun eski saatleri). Çağıran transaction içinde olmalıdır.
    public async Task<ActivationCounts> ReleaseAsync(IQueryable<TimeSlot> slots)
    {
        var freed = await TakeInstructorSlotsAsync(slots);
        return await ActivateAsync(freed);
    }

    // Saat dilimlerinden eğitmene ait olanları (boşalan eğitmen saatleri) okur, sonra hepsini siler
    private async Task<List<(string InstructorId, DateTime Hour)>> TakeInstructorSlotsAsync(IQueryable<TimeSlot> slots)
    {
        var freed = await slots
            .Where(t => t.UserId == t.Appointment.InstructorId)
            .Select(t => new { t.UserId, t.SlotStart })
            .ToListAsync();
        await slots.ExecuteDeleteAsync();
        return freed.Select(f => (f.UserId, f.SlotStart)).ToList();
    }

    private async Task<ActivationCounts> ActivateAsync(List<(string InstructorId, DateTime Hour)> freed)
    {
        var counts = ActivationCounts.None;
        foreach (var group in freed.GroupBy(f => f.InstructorId))
            counts += await ActivateForFreedSlotsAsync(group.Key, group.Select(f => f.Hour));
        return counts;
    }

    private async Task<ActivationCounts> TryActivateAsync(int entryId)
    {
        var now = AppClock.Now;
        var entry = await db.WaitlistEntries
            .Include(w => w.User)
            .Include(w => w.Instructor)
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == entryId && w.Status == WaitlistStatus.Waiting);
        if (entry is null)
            return ActivationCounts.None;   // bu arada başka bir işlem ele aldı

        var slotStarts = Enumerable.Range(0, entry.DurationHours).Select(i => entry.StartTime.AddHours(i)).ToList();
        var userIds = new[] { entry.UserId, entry.InstructorId };

        // Kayıt artık geçerli değilse Geçersiz olur, sıradakine geçilir
        if (entry.StartTime <= now
            || !await db.UserSkills.AnyAsync(us => us.UserId == entry.InstructorId && us.SkillId == entry.SkillId)
            || (entry.MeetingType == MeetingType.FaceToFace && !AppointmentBookingService.CanMeetFaceToFace(entry.User, entry.Instructor)))
            return await InvalidateAsync(entryId);

        // Bu saatleri hâlâ tutan süresi dolmuş talepler önce kapatılır (aktifleştirme yeniden tetiklenmez:
        // aynı saatleri zaten bu kayıt alacak; diğer saatlerini görevin sonraki turu ele alır)
        var staleIds = await db.TimeSlots
            .Where(ts => userIds.Contains(ts.UserId) && slotStarts.Contains(ts.SlotStart))
            .Select(ts => ts.Appointment)
            .Where(AppointmentRules.ExpiredPending(now))
            .Select(a => a.Id)
            .Distinct()
            .ToListAsync();
        if (staleIds.Count > 0)
        {
            await db.Appointments.Where(a => staleIds.Contains(a.Id)).Where(AppointmentRules.ExpiredPending(now))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatus.Expired));
            await db.TimeSlots.Where(t => staleIds.Contains(t.AppointmentId) && t.Appointment.Status == AppointmentStatus.Expired)
                .ExecuteDeleteAsync();
        }

        var taken = await db.TimeSlots
            .Where(ts => userIds.Contains(ts.UserId) && slotStarts.Contains(ts.SlotStart))
            .Select(ts => ts.UserId)
            .ToListAsync();
        if (taken.Contains(entry.UserId))
            return await InvalidateAsync(entryId);   // bekleyenin o saatte başka randevusu var
        if (taken.Contains(entry.InstructorId))
            return ActivationCounts.None;            // eğitmenin diğer saati hâlâ dolu: beklemeye devam

        var appointment = new Appointment
        {
            StudentId = entry.UserId,
            InstructorId = entry.InstructorId,
            SkillId = entry.SkillId,
            StartTime = entry.StartTime,
            DurationHours = entry.DurationHours,
            Status = AppointmentStatus.Pending,
            MeetingType = entry.MeetingType,
            MeetingPlaceName = entry.MeetingPlaceName,
            MeetingLatitude = entry.MeetingLatitude,
            MeetingLongitude = entry.MeetingLongitude,
            // 24 saatlik süre talebin aktifleştiği an başlar
            CreatedAt = now,
            ExpiresAt = now + AppointmentBookingService.RequestLifetime,
            // Eğitmene yeni talep, öğrenciye "talebiniz oluştu": iki tarafta da kırmızı nokta
            StudentSeen = false,
            InstructorSeen = false
        };
        foreach (var slotStart in slotStarts)
            foreach (var userId in userIds)
                appointment.TimeSlots.Add(new TimeSlot { UserId = userId, SlotStart = slotStart });

        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Bekleme listesi aktifleştirmesi bir transaction içinde çağrılmalıdır.");
        await transaction.CreateSavepointAsync(SavepointName);
        db.Appointments.Add(appointment);
        try
        {
            await db.SaveChangesAsync();

            // Kayıt hâlâ "Bekliyor" ise aktifleşir; aynı kaydı başka bir işlem az önce aldıysa bu deneme geri alınır
            var updated = await db.WaitlistEntries
                .Where(w => w.Id == entryId && w.Status == WaitlistStatus.Waiting)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.Status, WaitlistStatus.Activated)
                    .SetProperty(w => w.ActivatedAppointmentId, appointment.Id));
            if (updated == 0)
            {
                await UndoAsync(transaction, appointment);
                return ActivationCounts.None;
            }
        }
        catch (DbUpdateException ex) when (IsSlotConflict(ex))
        {
            // Saati aynı anda başka biri aldı: kayıt beklemeye devam eder
            await UndoAsync(transaction, appointment);
            return ActivationCounts.None;
        }

        Detach(appointment);
        return new ActivationCounts(1, 0);
    }

    private async Task<ActivationCounts> InvalidateAsync(int entryId)
    {
        var updated = await db.WaitlistEntries
            .Where(w => w.Id == entryId && w.Status == WaitlistStatus.Waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WaitlistStatus.Invalid));
        return new ActivationCounts(0, updated);
    }

    private async Task UndoAsync(IDbContextTransaction transaction, Appointment appointment)
    {
        await transaction.RollbackToSavepointAsync(SavepointName);
        Detach(appointment);
    }

    // Yeni randevu EF'in hafızasından çıkarılır: çağıranın sonraki SaveChanges'i onu tekrar yazmaya çalışmasın
    private void Detach(Appointment appointment)
    {
        foreach (var slot in appointment.TimeSlots)
            db.Entry(slot).State = EntityState.Detached;
        db.Entry(appointment).State = EntityState.Detached;
    }

    // 2601: benzersiz indeks ihlali, 2627: benzersiz kısıt ihlali (TimeSlots: UserId + SlotStart)
    private static bool IsSlotConflict(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql && sql.Message.Contains(SlotIndexName);
}
