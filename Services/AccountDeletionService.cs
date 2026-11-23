using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;

namespace YetenekPlatformu.Services;

// Üyeliği siler (docs/sema.md -> "Üyelik silme"). Bütün adımlar tek transaction içindedir.
public class AccountDeletionService(ApplicationDbContext db, UserManager<ApplicationUser> userManager, WaitlistService waitlist)
{
    public const string CancelReason = "Üyelik silindi";

    private static readonly AppointmentStatus[] ActiveStatuses =
        [AppointmentStatus.Pending, AppointmentStatus.Approved, AppointmentStatus.ChangeProposed];

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user)
    {
        var userId = user.Id;
        await using var transaction = await db.Database.BeginTransactionAsync();

        // 0) Karşı tarafı zaten silinmiş randevular: bu kişi de silinince iki tarafı da boş kalacak (bkz. 4. adım)
        var bothDeletedIds = await db.Appointments
            .Where(a => (a.StudentId == userId && a.InstructorId == null) || (a.InstructorId == userId && a.StudentId == null))
            .Select(a => a.Id)
            .ToListAsync();

        // 1) Aktif randevuları iptal et, saatleri boşa çıkar
        var activeAppointments = await db.Appointments
            .Include(a => a.TimeSlots)
            .Where(a => (a.StudentId == userId || a.InstructorId == userId) && ActiveStatuses.Contains(a.Status))
            .ToListAsync();

        // Boşalan eğitmen saatleri (bekleme listesi için). Eğitmen silinen kişinin kendisiyse
        // onu bekleyen kayıtlar zaten silineceği için sıradaki aranmaz.
        var freedInstructorHours = activeAppointments
            .Where(a => a.InstructorId != null && a.InstructorId != userId)
            .SelectMany(a => a.TimeSlots.Where(t => t.UserId == a.InstructorId).Select(t => (InstructorId: a.InstructorId!, t.SlotStart)))
            .ToList();

        foreach (var appointment in activeAppointments)
        {
            appointment.Status = AppointmentStatus.Cancelled;
            appointment.CancelReason = CancelReason;
            appointment.ProposedStartTime = null;
            appointment.ProposedDurationHours = null;
            appointment.ProposedByUserId = null;
            appointment.ProposedAt = null;
            appointment.ProposedPlaceName = null;
            appointment.ProposedLatitude = null;
            appointment.ProposedLongitude = null;

            // Karşı tarafta kırmızı nokta çıksın
            if (appointment.StudentId == userId)
                appointment.InstructorSeen = false;
            else
                appointment.StudentSeen = false;

            db.TimeSlots.RemoveRange(appointment.TimeSlots);
        }
        await db.SaveChangesAsync();

        // Aşağıdaki toplu güncellemeler EF'in hafızasını atlar; eski kopyalar kalırsa
        // kullanıcı silinirken EF onları tekrar yazmaya çalışır ve "eşzamanlılık" hatası verir.
        db.ChangeTracker.Clear();

        // Üyenin bekleme listesi kayıtları (bekleyen ya da beklenen olarak) önce silinir: sıradan kendisi seçilmesin
        await db.WaitlistEntries.Where(w => w.UserId == userId || w.InstructorId == userId).ExecuteDeleteAsync();

        // Boşa çıkan saatler için bekleme listesindeki sıradakiler aktifleşir (aynı transaction içinde)
        foreach (var group in freedInstructorHours.GroupBy(f => f.InstructorId))
            await waitlist.ActivateForFreedSlotsAsync(group.Key, group.Select(f => f.SlotStart));

        // 2) Üyeye ait yan kayıtları sil
        await db.TimeSlots.Where(t => t.UserId == userId).ExecuteDeleteAsync();
        await db.UserSkills.Where(us => us.UserId == userId).ExecuteDeleteAsync();
        await db.Favorites.Where(f => f.UserId == userId || f.InstructorId == userId).ExecuteDeleteAsync();

        // 3) Geçmiş kayıtlardaki kimliği boşalt ("Silinmiş Üye")
        await db.Appointments.Where(a => a.StudentId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.StudentId, (string?)null));
        await db.Appointments.Where(a => a.InstructorId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.InstructorId, (string?)null));
        await db.Appointments.Where(a => a.CancelledByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CancelledByUserId, (string?)null));
        await db.Appointments.Where(a => a.ProposedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ProposedByUserId, (string?)null));
        await db.Payments.Where(p => p.SenderId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SenderId, (string?)null));
        await db.Payments.Where(p => p.ReceiverId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ReceiverId, (string?)null));

        // 4) İki tarafı da silinmiş randevular ve bağlı kayıtları tamamen silinir.
        //    Ödemesi olan randevu kalır: ödeme admin toplamlarında görünmeye devam etsin ve yetim kalmasın.
        //    Bir taraf hâlâ varsa randevu yerinde kalır, o kişi "Silinmiş Üye" görür.
        if (bothDeletedIds.Count > 0)
        {
            var removable = db.Appointments.Where(a => bothDeletedIds.Contains(a.Id)
                && a.StudentId == null && a.InstructorId == null
                && !db.Payments.Any(p => p.AppointmentId == a.Id));

            // Bekleme listesinden bu randevuya bağlanmış kayıt silmeyi engellemesin
            await db.WaitlistEntries
                .Where(w => w.ActivatedAppointmentId != null && removable.Any(a => a.Id == w.ActivatedAppointmentId))
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.ActivatedAppointmentId, (int?)null));
            // Saat dilimleri veritabanında cascade ile de silinir; burada açıkça siliniyor
            await db.TimeSlots.Where(t => removable.Any(a => a.Id == t.AppointmentId)).ExecuteDeleteAsync();
            await removable.ExecuteDeleteAsync();
        }

        // 5) Kullanıcı satırını sil (rol kayıtları Identity tarafından otomatik silinir)
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            return result;
        }

        await transaction.CommitAsync();
        return result;
    }
}
