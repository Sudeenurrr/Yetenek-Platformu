using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;

namespace YetenekPlatformu.Pages.Account.Profile;

// Tamamlanmış bir dersin listede gösterilen hali. OtherPartyName boşsa karşı taraf üyeliğini iptal etmiştir.
// PaymentAmount: bu ders için yapılan gönüllü ödeme (yoksa boş). Yalnızca ödemenin iki tarafının listesinde görünür.
// CanPay: Aldığım Dersler'de "Ödeme Yap" düğmesi (ödeme yok ve eğitmen hâlâ üye; sunucu işlemde yeniden kontrol eder).
// IBAN bu listeye gönderilmez; yalnızca Ödeme sayfasında gösterilir.
public record LessonRow(
    int AppointmentId,
    DateTime StartTime,
    byte DurationHours,
    string SkillName,
    string? OtherPartyName,
    MeetingType MeetingType,
    decimal? PaymentAmount,
    bool CanPay);

// Verdiğim / Aldığım Dersler listesinin verisi (_LessonList.cshtml)
public class LessonListViewModel
{
    public required string EmptyText { get; init; }
    public required IReadOnlyList<LessonRow> Rows { get; init; }

    // true: Aldığım Dersler (öğrenci; "Ödeme Yap", "Toplam ödemem"), false: Verdiğim Dersler ("Toplam kazancım")
    public required bool AsStudent { get; init; }

    // Payments tablosundan: öğrencide gönderilen, eğitmende alınan ödemelerin toplamı
    public required decimal PaymentTotal { get; init; }

    public int TotalHours => Rows.Sum(r => r.DurationHours);
}

public static class LessonQueries
{
    // Kişinin dersi verdiği (InstructorId) tamamlanmış dersler; karşı taraf dersi alan
    public static Task<List<LessonRow>> CompletedAsInstructorAsync(ApplicationDbContext db, string userId) =>
        db.Appointments
            .Where(a => a.InstructorId == userId && a.Status == AppointmentStatus.Completed)
            .OrderByDescending(a => a.StartTime)
            .Select(a => new LessonRow(
                a.Id,
                a.StartTime,
                a.DurationHours,
                a.Skill.Name,
                a.Student == null ? null : a.Student.FirstName + " " + a.Student.LastName,
                a.MeetingType,
                db.Payments.Where(p => p.AppointmentId == a.Id).Select(p => (decimal?)p.Amount).FirstOrDefault(),
                false))
            .ToListAsync();

    // Kişinin dersi aldığı (StudentId) tamamlanmış dersler; karşı taraf dersi veren
    public static Task<List<LessonRow>> CompletedAsStudentAsync(ApplicationDbContext db, string userId) =>
        db.Appointments
            .Where(a => a.StudentId == userId && a.Status == AppointmentStatus.Completed)
            .OrderByDescending(a => a.StartTime)
            .Select(a => new LessonRow(
                a.Id,
                a.StartTime,
                a.DurationHours,
                a.Skill.Name,
                a.Instructor == null ? null : a.Instructor.FirstName + " " + a.Instructor.LastName,
                a.MeetingType,
                db.Payments.Where(p => p.AppointmentId == a.Id).Select(p => (decimal?)p.Amount).FirstOrDefault(),
                a.InstructorId != null && !db.Payments.Any(p => p.AppointmentId == a.Id)))
            .ToListAsync();
}
