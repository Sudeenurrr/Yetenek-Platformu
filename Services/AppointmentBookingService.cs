using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Settings;

namespace YetenekPlatformu.Services;

// Randevu Al: takvimdeki saatlerin durumu ve randevu talebinin oluşturulması.
// Bütün kurallar burada, sunucuda kontrol edilir; sayfadaki JavaScript yalnızca görünümü hazırlar.
// Çakışma: TimeSlots tablosundaki UserId + SlotStart benzersiz indeksi + transaction (docs/sema.md).
// Süresi dolmuş bekleyen talebin tuttuğu saate yeni talep gelirse önce bekleme listesindeki sıradaki aktifleşir.
public class AppointmentBookingService(ApplicationDbContext db, IOptions<SchedulingOptions> scheduling, WaitlistService waitlist)
{
    // Bekleyen talep bu süre sonra kendiliğinden kapanır
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromHours(24);

    private const string SlotIndexName = "IX_TimeSlots_UserId_SlotStart";
    private const string WaitlistIndexName = "IX_WaitlistEntries_UserId_InstructorId_StartTime";

    public int DayStartHour => scheduling.Value.DayStartHour;
    public int DayEndHour => scheduling.Value.DayEndHour;

    // Bugünden itibaren en fazla kaç gün sonrasına randevu alınabilir (appsettings.json -> Scheduling)
    public int MaxDaysAhead => scheduling.Value.MaxDaysAhead;

    // Saat durumu (JSON'da metin olarak gönderilir)
    public const string StateFree = "free";        // Boş
    public const string StateBusy = "busy";        // Eğitmenin saati dolu (nedeni gösterilmez)
    public const string StateMine = "mine";        // Talep edenin bu saatte başka aktif randevusu var
    public const string StatePast = "past";        // Geçti

    public record SlotState(int Hour, string State);

    // Yüz Yüze randevuda buluşma yeri: ad zorunlu; koordinatlar listeden seçilince dolu, elle yazılınca boş
    public record BookingRequest(string StudentId, string? InstructorId, int SkillId, DateOnly Date, int StartHour, int DurationHours, MeetingType MeetingType,
        string? PlaceName = null, decimal? PlaceLatitude = null, decimal? PlaceLongitude = null);

    public record BookingResult(bool Succeeded, string? Error, int? AppointmentId = null)
    {
        public static BookingResult Fail(string error) => new(false, error);
    }

    // "Bugün" Türkiye saatine göre (tarayıcının ya da sunucunun saat dilimine bakılmaz)
    public DateOnly Today => AppClock.Today;
    public DateOnly LastBookableDay => Today.AddDays(MaxDaysAhead);

    // Eğitmen olarak randevu alınabilecek kişi: Uye rolünde ve kişinin kendisi değil
    public Task<ApplicationUser?> FindInstructorAsync(string? instructorId, string studentId) =>
        string.IsNullOrEmpty(instructorId) || instructorId == studentId
            ? Task.FromResult<ApplicationUser?>(null)
            : MemberQueries.Members(db).FirstOrDefaultAsync(u => u.Id == instructorId);

    // Yüz yüze yalnızca iki kişinin kayıtlı şehri aynıysa
    public static bool CanMeetFaceToFace(ApplicationUser student, ApplicationUser instructor) =>
        student.City == instructor.City;

    // Seçilen günün saatleri: her saat için boş / dolu / başka randevunuz var / geçti
    public async Task<List<SlotState>> GetDaySlotsAsync(string studentId, string instructorId, DateOnly date)
    {
        var dayStart = date.ToDateTime(new TimeOnly(DayStartHour, 0));
        var dayEnd = date.ToDateTime(TimeOnly.MinValue).AddHours(DayEndHour);
        var now = AppClock.Now;

        // Süresi dolmuş bekleyen talepler saati tutmaz (bkz. CheckTakenAsync)
        var taken = await ActiveSlots(now)
            .Where(ts => (ts.UserId == instructorId || ts.UserId == studentId) && ts.SlotStart >= dayStart && ts.SlotStart < dayEnd)
            .Select(ts => new { ts.UserId, ts.SlotStart })
            .ToListAsync();

        var instructorHours = taken.Where(t => t.UserId == instructorId).Select(t => t.SlotStart.Hour).ToHashSet();
        var studentHours = taken.Where(t => t.UserId == studentId).Select(t => t.SlotStart.Hour).ToHashSet();

        return Enumerable.Range(DayStartHour, DayEndHour - DayStartHour)
            .Select(hour =>
            {
                var start = date.ToDateTime(new TimeOnly(hour, 0));
                var state = start <= now ? StatePast
                    : instructorHours.Contains(hour) ? StateBusy
                    : studentHours.Contains(hour) ? StateMine
                    : StateFree;
                return new SlotState(hour, state);
            })
            .ToList();
    }

    // Talep ve bekleme listesi için ortak kontroller geçtiyse hazırlanan bilgiler
    private record CheckedRequest(ApplicationUser Student, ApplicationUser Instructor, DateTime Start, List<DateTime> SlotStarts, string[] UserIds,
        string? PlaceName, decimal? PlaceLatitude, decimal? PlaceLongitude);

    // Yetenek, görüşme türü, şehir ve zaman kuralları (talep ve bekleme listesine giriş için aynı)
    private async Task<(CheckedRequest? Checked, string? Error)> CheckAsync(BookingRequest request)
    {
        var student = await db.Users.FirstOrDefaultAsync(u => u.Id == request.StudentId);
        var instructor = await FindInstructorAsync(request.InstructorId, request.StudentId);
        if (student is null || instructor is null)
            return (null, "Bu üyeden randevu alınamaz.");

        // Yetenek: yalnızca eğitmenin profiline eklediği yetenekler
        if (!await db.UserSkills.AnyAsync(us => us.UserId == instructor.Id && us.SkillId == request.SkillId))
            return (null, "Lütfen bu üyenin yeteneklerinden birini seçin.");

        if (!Enum.IsDefined(request.MeetingType))
            return (null, "Lütfen görüşme türünü seçin.");
        if (request.MeetingType == MeetingType.FaceToFace && !CanMeetFaceToFace(student, instructor))
            return (null, "Şehirleriniz farklı olduğu için yalnızca Online seçilebilir.");

        // Yüz Yüze: buluşma yeri zorunlu (ad en fazla 300 karakter; koordinatlar ya ikisi boş ya da Türkiye içinde).
        // Online: yer bilgisi gönderilse de kaydedilmez.
        string? placeName = null;
        decimal? placeLat = null, placeLon = null;
        if (request.MeetingType == MeetingType.FaceToFace)
        {
            if (!PlaceSearchService.TryValidatePlace(request.PlaceName, request.PlaceLatitude, request.PlaceLongitude,
                    out var cleanedName, out placeLat, out placeLon, out var placeError))
                return (null, placeError);
            placeName = cleanedName;
        }

        // Zaman kuralları
        if (request.DurationHours is not (1 or 2))
            return (null, "Süre 1 ya da 2 saat olabilir.");
        if (request.Date < Today || request.Date > LastBookableDay)
            return (null, $"Bugünden itibaren en fazla {MaxDaysAhead} gün sonrası için randevu alınabilir.");
        if (request.StartHour < DayStartHour || request.StartHour + request.DurationHours > DayEndHour)
            return (null, $"Randevu {DayStartHour:00}:00 ile {DayEndHour:00}:00 arasında olmalıdır.");

        var start = request.Date.ToDateTime(new TimeOnly(request.StartHour, 0));
        if (start <= AppClock.Now)
            return (null, "Geçmiş bir saate randevu alınamaz.");

        var slotStarts = Enumerable.Range(0, request.DurationHours).Select(i => start.AddHours(i)).ToList();
        return (new CheckedRequest(student, instructor, start, slotStarts, [student.Id, instructor.Id], placeName, placeLat, placeLon), null);
    }

    // Seçilen saatlerde kimin dolu olduğu. Önce bu saatleri hâlâ tutan süresi dolmuş talepler kapatılır ve
    // boşalan eğitmen saatleri için bekleme listesindeki sıradaki aktifleşir (sıra yeni gelen talepten önce gelir).
    private async Task<(bool InstructorBusy, bool StudentBusy, bool OwnRequest)> CheckTakenAsync(CheckedRequest c)
    {
        await waitlist.ExpireStaleRequestsAsync(c.UserIds, c.SlotStarts);

        // Kullanıcıya anlaşılır mesaj için önce kontrol edilir; asıl güvence benzersiz indekstir
        var taken = await db.TimeSlots
            .Where(ts => c.UserIds.Contains(ts.UserId) && c.SlotStarts.Contains(ts.SlotStart))
            .Select(ts => new { ts.UserId, ts.Appointment.StudentId, ts.Appointment.InstructorId })
            .ToListAsync();

        return (taken.Any(t => t.UserId == c.Instructor.Id),
                taken.Any(t => t.UserId == c.Student.Id),
                // Eğitmenin saatini bu kişinin kendi talebi tutuyor (ör. sıradan az önce aktifleşti)
                taken.Any(t => t.StudentId == c.Student.Id && t.InstructorId == c.Instructor.Id));
    }

    // Randevu talebi: kurallar kontrol edilir, randevu ve saat dilimleri tek transaction'da yazılır
    public async Task<BookingResult> CreateRequestAsync(BookingRequest request)
    {
        var (c, error) = await CheckAsync(request);
        if (c is null)
            return BookingResult.Fail(error!);

        var (student, instructor, start, slotStarts, userIds) = (c.Student, c.Instructor, c.Start, c.SlotStarts, c.UserIds);
        var now = AppClock.Now;

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var (instructorBusy, studentBusy, ownRequest) = await CheckTakenAsync(c);
            if (ownRequest)
            {
                // Sıradan aktifleşen talep bu transaction'da yazıldı: kaydedilir
                await transaction.CommitAsync();
                return BookingResult.Fail("Bu saat için zaten bir talebiniz var. Gönderdiğim Randevular sayfasına bakın.");
            }
            if (instructorBusy)
            {
                await transaction.CommitAsync();
                return BookingResult.Fail("Seçtiğiniz saat dolu. Başka bir saat seçebilir ya da bekleme listesine girebilirsiniz.");
            }
            if (studentBusy)
            {
                await transaction.CommitAsync();
                return BookingResult.Fail("Bu saatte başka bir randevunuz var.");
            }

            var appointment = new Appointment
            {
                StudentId = student.Id,
                InstructorId = instructor.Id,
                SkillId = request.SkillId,
                StartTime = start,
                DurationHours = (byte)request.DurationHours,
                Status = AppointmentStatus.Pending,
                MeetingType = request.MeetingType,
                MeetingPlaceName = c.PlaceName,
                MeetingLatitude = c.PlaceLatitude,
                MeetingLongitude = c.PlaceLongitude,
                CreatedAt = now,
                ExpiresAt = now + RequestLifetime,
                // Yeni talep: eğitmen henüz görmedi (kırmızı nokta)
                StudentSeen = true,
                InstructorSeen = false
            };

            // Her saat için iki satır: öğrenci ve eğitmen (2 saatlik randevu 4 satır)
            foreach (var slotStart in slotStarts)
                foreach (var userId in userIds)
                    appointment.TimeSlots.Add(new TimeSlot { UserId = userId, SlotStart = slotStart });

            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return new BookingResult(true, null, appointment.Id);
        }
        catch (DbUpdateException ex) when (IsSlotConflict(ex))
        {
            // Aynı saati başka biri aynı anda aldı: veritabanı ikinci kaydı reddetti
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            return BookingResult.Fail("Bu saat az önce doldu. Başka bir saat seçebilir ya da bekleme listesine girebilirsiniz.");
        }
    }

    // Bekleme listesine giriş: yalnızca eğitmenin saati gerçekten doluysa ve kişinin o saatte kendi randevusu yoksa.
    // Aynı kişi aynı eğitmenin aynı saati için yalnızca bir kez "Bekliyor" olabilir (filtreli benzersiz indeks).
    public async Task<BookingResult> JoinWaitlistAsync(BookingRequest request)
    {
        var (c, error) = await CheckAsync(request);
        if (c is null)
            return BookingResult.Fail(error!);

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var (instructorBusy, studentBusy, ownRequest) = await CheckTakenAsync(c);
            string? refusal = ownRequest ? "Bu saat için zaten bir talebiniz var. Gönderdiğim Randevular sayfasına bakın."
                : studentBusy ? "Bu saatte başka bir randevunuz var."
                : !instructorBusy ? "Bu saat artık boş. Bekleme listesine girmeden doğrudan talep gönderebilirsiniz."
                : await db.WaitlistEntries.AnyAsync(w => w.UserId == c.Student.Id && w.InstructorId == c.Instructor.Id
                    && w.StartTime == c.Start && w.Status == WaitlistStatus.Waiting)
                    ? "Bu saat için zaten bekleme listesindesiniz."
                : null;
            if (refusal is not null)
            {
                await transaction.CommitAsync();   // varsa süresi dolan talep ve sıradan aktifleşme kaydedilir
                return BookingResult.Fail(refusal);
            }

            db.WaitlistEntries.Add(new WaitlistEntry
            {
                UserId = c.Student.Id,
                InstructorId = c.Instructor.Id,
                SkillId = request.SkillId,
                StartTime = c.Start,
                DurationHours = (byte)request.DurationHours,
                MeetingType = request.MeetingType,
                // Sıra gelince oluşan talebe aynen kopyalanır (WaitlistService)
                MeetingPlaceName = c.PlaceName,
                MeetingLatitude = c.PlaceLatitude,
                MeetingLongitude = c.PlaceLongitude,
                CreatedAt = AppClock.Now,
                Status = WaitlistStatus.Waiting
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return new BookingResult(true, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains(WaitlistIndexName))
        {
            // Aynı kayıt aynı anda iki kez gönderildi: veritabanı ikincisini reddetti
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            return BookingResult.Fail("Bu saat için zaten bekleme listesindesiniz.");
        }
    }

    // Saati gerçekten tutan satırlar: süresi dolmuş (24 saat geçmiş ya da ders saati gelmiş) bekleyen talepler hariç
    // (Kural: AppointmentRules.ExpiredPending; burada saat dilimi tablosu üzerinden sorgulandığı için koşul aynen yazılı)
    private IQueryable<TimeSlot> ActiveSlots(DateTime now) =>
        db.TimeSlots.Where(ts => !(ts.Appointment.Status == AppointmentStatus.Pending
            && (ts.Appointment.ExpiresAt <= now || ts.Appointment.StartTime <= now)));

    // 2601: benzersiz indeks ihlali, 2627: benzersiz kısıt ihlali (TimeSlots: UserId + SlotStart)
    private static bool IsSlotConflict(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql && sql.Message.Contains(SlotIndexName);
}
