using System.Linq.Expressions;
using YetenekPlatformu.Models;

namespace YetenekPlatformu.Services;

// Randevuların zamana bağlı ortak kuralları (tek yerde). "now" her zaman AppClock.Now (Türkiye saati).
// Sorgu içinde kullanılan biçimler (Expression) veritabanında, diğerleri bellekte çalışır.
public static class AppointmentRules
{
    // Beklemede talep 24 saat dolunca ya da ders saati gelince kapanır (Süresi Doldu)
    public static bool IsExpiredPending(AppointmentStatus status, DateTime expiresAt, DateTime startTime, DateTime now) =>
        status == AppointmentStatus.Pending && (expiresAt <= now || startTime <= now);

    public static Expression<Func<Appointment, bool>> ExpiredPending(DateTime now) =>
        a => a.Status == AppointmentStatus.Pending && (a.ExpiresAt <= now || a.StartTime <= now);

    // Değişiklik önerisi bu süre içinde yanıtlanmazsa düşer
    public static readonly TimeSpan ProposalLifetime = TimeSpan.FromHours(24);

    // Öneri düşer: 24 saat dolduysa, eski ders saati geldiyse ya da önerilen saat geldiyse (hangisi önceyse)
    public static DateTime ProposalDeadline(DateTime? proposedAt, DateTime startTime, DateTime? proposedStartTime)
    {
        var deadline = startTime;
        if (proposedAt is DateTime at && at + ProposalLifetime < deadline)
            deadline = at + ProposalLifetime;
        if (proposedStartTime is DateTime proposed && proposed < deadline)
            deadline = proposed;
        return deadline;
    }

    public static bool IsExpiredProposal(AppointmentStatus status, DateTime? proposedAt, DateTime startTime, DateTime? proposedStartTime, DateTime now) =>
        status == AppointmentStatus.ChangeProposed && ProposalDeadline(proposedAt, startTime, proposedStartTime) <= now;

    public static Expression<Func<Appointment, bool>> ExpiredProposal(DateTime now)
    {
        var proposedBefore = now - ProposalLifetime;
        return a => a.Status == AppointmentStatus.ChangeProposed
            && (a.ProposedAt <= proposedBefore || a.StartTime <= now || a.ProposedStartTime <= now);
    }

    // Onaylanmış dersin bitiş saati (başlangıç + süre) geldiyse ders bitmiştir (Tamamlandı)
    public static Expression<Func<Appointment, bool>> FinishedApproved(DateTime now) =>
        a => a.Status == AppointmentStatus.Approved && a.StartTime.AddHours(a.DurationHours) <= now;
}
