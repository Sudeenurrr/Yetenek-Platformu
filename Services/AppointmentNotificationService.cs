using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;

namespace YetenekPlatformu.Services;

// Kırmızı nokta: "Randevu İstekleri" ve "Gönderdiğim Randevular" sayfalarında görülmemiş değişiklik var mı?
// Yalnızca StudentSeen / InstructorSeen alanlarına bakılır. Bir olay (yeni talep, onay, red; ileride iptal,
// bekleme listesinden aktifleşme, değişiklik önerisi) karşı tarafın alanını false yapınca nokta kendiliğinden çıkar.
public class AppointmentNotificationService(ApplicationDbContext db)
{
    public record UnseenState(bool Incoming, bool Sent)
    {
        public bool Any => Incoming || Sent;
    }

    // Aynı istekte menü ve açılır menü düğmesi için tek sorgu yeter
    private UnseenState? cached;
    private string? cachedFor;

    // Yalnızca kişinin kendi randevuları: eğitmeni olduğu (Randevu İstekleri) ve öğrencisi olduğu (Gönderdiğim Randevular).
    // "Var mı" sorusu (EXISTS) tek sorguda sorulur; satırlar getirilmez.
    public async Task<UnseenState> GetAsync(string userId)
    {
        if (cached is not null && cachedFor == userId)
            return cached;

        var row = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                Incoming = db.Appointments.Any(a => a.InstructorId == userId && !a.InstructorSeen),
                Sent = db.Appointments.Any(a => a.StudentId == userId && !a.StudentSeen)
            })
            .FirstOrDefaultAsync();

        cachedFor = userId;
        cached = row is null ? new UnseenState(false, false) : new UnseenState(row.Incoming, row.Sent);
        return cached;
    }
}
