using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Settings;

namespace YetenekPlatformu.Services;

// Gönüllü ödeme (benzetim: gerçek para aktarılmaz, ödeme yalnızca kayıt olarak tutulur).
// Kurallar burada, sunucuda kontrol edilir:
// - yalnızca dersin öğrencisi, yalnızca Tamamlandı ders için ve eğitmen üyeliğini silmemişse ödeyebilir;
// - tutar yalnızca PaymentOptions.AllowedAmounts listesinden (veritabanında CK_Payments_Amount da korur);
// - her derse en fazla bir ödeme (veritabanında IX_Payments_AppointmentId benzersiz indeksi de korur);
// - ödeme geri alınamaz ve değiştirilemez; süre sınırı yoktur.
// Eğitmenin IBAN'ı yalnızca Ödeme sayfasına, yalnızca o dersin öğrencisine gönderilir.
public class PaymentService(ApplicationDbContext db)
{
    public const string AppointmentIndexName = "IX_Payments_AppointmentId";

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    // Ödeme sayfasında gösterilen ders. PaidAmount doluysa bu ders için ödeme zaten yapılmış.
    public record PayTarget(
        int AppointmentId,
        string InstructorName,
        string InstructorIban,
        string SkillName,
        DateTime StartTime,
        byte DurationHours,
        MeetingType MeetingType,
        decimal? PaidAmount,
        DateTime? PaidAt);

    public record PayResult(bool Succeeded, string Message, bool NotFound = false)
    {
        public static PayResult Fail(string message, bool notFound = false) => new(false, message, notFound);
    }

    // Tutar ekranda Türkçe biçimde: "1.250 TL"
    public static string FormatAmount(decimal amount) => amount.ToString("#,0", Tr) + " TL";

    // Formdan gelen tutar yalnızca düz tam sayı olarak okunur ("250"); "250,5", "250.00", "-200" gibi değerler geçersizdir
    public static bool TryParseAmount(string? input, out decimal amount)
    {
        amount = 0;
        if (!int.TryParse(input?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return false;
        amount = value;
        return PaymentOptions.IsAllowed(amount);
    }

    // Ödenebilir ders: numarası bu, öğrencisi ben, durumu Tamamlandı, eğitmeni hâlâ üye. Değilse null.
    public Task<PayTarget?> FindTargetAsync(int appointmentId, string studentId) =>
        db.Appointments
            .Where(a => a.Id == appointmentId && a.StudentId == studentId
                && a.Status == AppointmentStatus.Completed && a.Instructor != null)
            .Select(a => new PayTarget(
                a.Id,
                a.Instructor!.FirstName + " " + a.Instructor.LastName,
                a.Instructor.Iban,
                a.Skill.Name,
                a.StartTime,
                a.DurationHours,
                a.MeetingType,
                db.Payments.Where(p => p.AppointmentId == a.Id).Select(p => (decimal?)p.Amount).FirstOrDefault(),
                db.Payments.Where(p => p.AppointmentId == a.Id).Select(p => (DateTime?)p.CreatedAt).FirstOrDefault()))
            .FirstOrDefaultAsync();

    public async Task<PayResult> PayAsync(int appointmentId, string studentId, string? amountInput)
    {
        if (!TryParseAmount(amountInput, out var amount))
            return PayResult.Fail("Lütfen listedeki tutarlardan birini seçin.");

        var lesson = await db.Appointments
            .Where(a => a.Id == appointmentId && a.StudentId == studentId
                && a.Status == AppointmentStatus.Completed && a.InstructorId != null)
            .Select(a => new { a.Id, a.InstructorId, Paid = db.Payments.Any(p => p.AppointmentId == a.Id) })
            .FirstOrDefaultAsync();
        if (lesson is null)
            return PayResult.Fail("Ders bulunamadı. Yalnızca tamamlanmış derslerinizin ödemesini yapabilirsiniz.", notFound: true);
        if (lesson.Paid)
            return PayResult.Fail("Bu ders için zaten ödeme yapılmış.");

        db.Payments.Add(new Payment
        {
            SenderId = studentId,
            ReceiverId = lesson.InstructorId,
            AppointmentId = lesson.Id,
            Amount = amount,
            CreatedAt = AppClock.Now
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains(AppointmentIndexName))
        {
            // Aynı ders için aynı anda iki ödeme gönderildi: veritabanı ikincisini reddetti
            db.ChangeTracker.Clear();
            return PayResult.Fail("Bu ders için zaten ödeme yapılmış.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            // 547: yabancı anahtar ya da kontrol kısıtı. Ör. eğitmen tam o anda üyeliğini sildi.
            db.ChangeTracker.Clear();
            return PayResult.Fail("Ödeme kaydedilemedi. Lütfen sayfayı yenileyip tekrar deneyin.");
        }

        return new PayResult(true, $"{FormatAmount(amount)} ödemeniz kaydedildi. Teşekkürler!");
    }

    // Toplam kazancım (alınan ödemeler) ve toplam ödemem (gönderilen ödemeler): Payments tablosundan
    public Task<decimal> TotalReceivedAsync(string userId) =>
        db.Payments.Where(p => p.ReceiverId == userId).SumAsync(p => p.Amount);

    public Task<decimal> TotalSentAsync(string userId) =>
        db.Payments.Where(p => p.SenderId == userId).SumAsync(p => p.Amount);
}
