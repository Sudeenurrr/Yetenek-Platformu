using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using ActionResult = YetenekPlatformu.Services.AppointmentResponseService.ActionResult;

namespace YetenekPlatformu.Services;

// Değişiklik önerisi (docs/sema.md -> "Randevu durumları"). Kurallar sunucuda:
//   - Yalnızca Onaylandı randevu, ders başlamadan, iki taraftan biri önerir. Aynı anda en fazla bir öneri olur.
//   - Önerilen yeni saatler iki kişi için TimeSlots'a IsProposal = true olarak yazılır (eski saatler de dolu kalır).
//     Eski saatle çakışan saatler yeniden yazılmaz; benzersiz indeks (UserId + SlotStart) çakışmayı veritabanında korur.
//   - Yüz Yüze randevuda yeni buluşma yeri de (saatle birlikte ya da tek başına) önerilebilir: iki kişinin şehri hâlâ aynı
//     olmalı, Randevu Al'daki yer kuralları geçerli. Yalnızca yer değişirse önerilen saat = mevcut saat, hiçbir saat ayrılmaz.
//   - Karşı taraf kabul ederse randevu yeni saate (ve yere) taşınır; reddederse, öneren geri çekerse ya da süre dolarsa
//     öneri düşer ve randevu önceki haliyle devam eder. Boşalan eğitmen saatleri için bekleme listesi aynı transaction'da işlenir.
//   - Görüşme türü (Online/Yüz Yüze) ve online link değişmez.
//   - Öneri 24 saat içinde yanıtlanmazsa, eski ders saati ya da önerilen saat gelince düşer (AppointmentRules).
//   - Aynı randevuya aynı anda iki işlem gelirse RowVersion ile yalnızca biri geçer.
public class AppointmentChangeService(ApplicationDbContext db, AppointmentBookingService booking, WaitlistService waitlist)
{
    private const string SlotIndexName = "IX_TimeSlots_UserId_SlotStart";

    // Öneri sayfasında gösterilen randevu (telefon ve IBAN yok)
    // IsStudent: kişi bu randevuda talebi gönderen taraf mı (işlemden sonra hangi listeye dönüleceği)
    // PlaceName: Yüz Yüze mevcut buluşma yeri. CanChangePlace: Yüz Yüze ve iki kişinin şehri hâlâ aynı (yeni yer önerilebilir)
    public record ProposalTarget(int Id, bool IsStudent, string? OtherPartyName, string SkillName, DateTime StartTime, byte DurationHours,
        MeetingType MeetingType, string? PlaceName, bool CanChangePlace, string? City);

    // Öneri yapılabilecek randevu: kişinin taraf olduğu, Onaylandı ve saati gelmemiş randevu
    public Task<ProposalTarget?> FindTargetAsync(int id, string userId)
    {
        var now = AppClock.Now;
        return db.Appointments
            .Where(a => a.Id == id && (a.StudentId == userId || a.InstructorId == userId)
                && a.Status == AppointmentStatus.Approved && a.StartTime > now)
            .Select(a => new ProposalTarget(
                a.Id,
                a.StudentId == userId,
                a.StudentId == userId
                    ? (a.Instructor == null ? null : a.Instructor.FirstName + " " + a.Instructor.LastName)
                    : (a.Student == null ? null : a.Student.FirstName + " " + a.Student.LastName),
                a.Skill.Name, a.StartTime, a.DurationHours, a.MeetingType,
                a.MeetingType == MeetingType.FaceToFace ? a.MeetingPlaceName : null,
                a.MeetingType == MeetingType.FaceToFace && a.Student != null && a.Instructor != null && a.Student.City == a.Instructor.City,
                a.MeetingType == MeetingType.FaceToFace && a.Student != null ? a.Student.City : null))
            .FirstOrDefaultAsync();
    }

    // Öneri takvimi: seçilen günün saatleri. Bu randevunun kendi saatleri boş sayılır.
    // "mine": öneren kişinin başka randevusu var; "busy": karşı tarafın saati dolu (nedeni gösterilmez).
    public async Task<List<AppointmentBookingService.SlotState>?> GetDaySlotsAsync(int id, string userId, DateOnly date)
    {
        var parties = await db.Appointments
            .Where(a => a.Id == id && (a.StudentId == userId || a.InstructorId == userId) && a.Status == AppointmentStatus.Approved)
            .Select(a => new { a.StudentId, a.InstructorId })
            .FirstOrDefaultAsync();
        if (parties?.StudentId is null || parties.InstructorId is null)
            return null;

        var otherId = parties.StudentId == userId ? parties.InstructorId : parties.StudentId;
        var dayStart = date.ToDateTime(new TimeOnly(booking.DayStartHour, 0));
        var dayEnd = date.ToDateTime(TimeOnly.MinValue).AddHours(booking.DayEndHour);
        var now = AppClock.Now;

        // Süresi dolmuş bekleyen talepler saati tutmaz (kural AppointmentRules.ExpiredPending; saat dilimi üzerinden aynen yazılı)
        var taken = await db.TimeSlots
            .Where(ts => (ts.UserId == userId || ts.UserId == otherId) && ts.AppointmentId != id
                && ts.SlotStart >= dayStart && ts.SlotStart < dayEnd
                && !(ts.Appointment.Status == AppointmentStatus.Pending
                    && (ts.Appointment.ExpiresAt <= now || ts.Appointment.StartTime <= now)))
            .Select(ts => new { ts.UserId, ts.SlotStart })
            .ToListAsync();

        var mine = taken.Where(t => t.UserId == userId).Select(t => t.SlotStart.Hour).ToHashSet();
        var others = taken.Where(t => t.UserId == otherId).Select(t => t.SlotStart.Hour).ToHashSet();

        return Enumerable.Range(booking.DayStartHour, booking.DayEndHour - booking.DayStartHour)
            .Select(hour =>
            {
                var start = date.ToDateTime(new TimeOnly(hour, 0));
                var state = start <= now ? AppointmentBookingService.StatePast
                    : mine.Contains(hour) ? AppointmentBookingService.StateMine
                    : others.Contains(hour) ? AppointmentBookingService.StateBusy
                    : AppointmentBookingService.StateFree;
                return new AppointmentBookingService.SlotState(hour, state);
            })
            .ToList();
    }

    // Öneri: ChangeTime ise yeni gün/saat/süre, ChangePlace ise (yalnızca Yüz Yüze) yeni buluşma yeri; en az biri seçilir.
    // Yer elle yazılmışsa koordinatlar boştur.
    public record ProposalInput(bool ChangeTime, DateOnly? Date, int? StartHour, int DurationHours,
        bool ChangePlace, string? PlaceName, decimal? PlaceLatitude, decimal? PlaceLongitude);

    // Yer araması için ortak şehir: kişinin taraf olduğu, Onaylandı, saati gelmemiş Yüz Yüze randevu ve iki kişinin
    // kayıtlı şehri hâlâ aynıysa o şehir; değilse null (öneri sayfasındaki konum araması yalnızca bu durumda çalışır)
    public async Task<string?> FindPlaceSearchCityAsync(int id, string userId)
    {
        var now = AppClock.Now;
        var cities = await db.Appointments
            .Where(a => a.Id == id && (a.StudentId == userId || a.InstructorId == userId)
                && a.Status == AppointmentStatus.Approved && a.StartTime > now && a.MeetingType == MeetingType.FaceToFace
                && a.Student != null && a.Instructor != null)
            .Select(a => new { StudentCity = a.Student!.City, InstructorCity = a.Instructor!.City })
            .FirstOrDefaultAsync();
        return cities is not null && cities.StudentCity == cities.InstructorCity ? cities.StudentCity : null;
    }

    // Değişiklik önerisi: önerilen yeni saatler iki kişi için geçici ayrılır (saat değişmiyorsa hiçbir saat ayrılmaz),
    // önerilen yer öneri alanlarına yazılır; durum Değişiklik Önerildi olur
    public async Task<ActionResult> ProposeAsync(int id, string userId, ProposalInput input)
    {
        if (!input.ChangeTime && !input.ChangePlace)
            return ActionResult.Fail("Neyi değiştirmek istediğinizi seçin: gün/saat ya da buluşma yeri.");

        var now = AppClock.Now;
        DateTime? requestedStart = null;
        if (input.ChangeTime)
        {
            if (input.Date is not DateOnly date || input.StartHour is not int startHour)
                return ActionResult.Fail("Lütfen yeni gün ve saati seçin.");
            if (input.DurationHours is not (1 or 2))
                return ActionResult.Fail("Süre 1 ya da 2 saat olabilir.");
            if (date < booking.Today || date > booking.LastBookableDay)
                return ActionResult.Fail($"Bugünden itibaren en fazla {booking.MaxDaysAhead} gün sonrası önerilebilir.");
            if (startHour < booking.DayStartHour || startHour + input.DurationHours > booking.DayEndHour)
                return ActionResult.Fail($"Önerilen saat {booking.DayStartHour:00}:00 ile {booking.DayEndHour:00}:00 arasında olmalıdır.");
            requestedStart = date.ToDateTime(new TimeOnly(startHour, 0));
            if (requestedStart <= now)
                return ActionResult.Fail("Geçmiş bir saat önerilemez.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Yalnızca randevunun tarafları: başkasının randevusu "bulunamadı" sayılır
        var appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == id && (a.StudentId == userId || a.InstructorId == userId));
        if (appointment is null)
            return new ActionResult(false, "Randevu bulunamadı.", NotFound: true);
        if (appointment.Status == AppointmentStatus.ChangeProposed)
            return ActionResult.Fail("Bu randevu için zaten yanıt bekleyen bir öneri var.");
        if (appointment.Status != AppointmentStatus.Approved)
            return ActionResult.Fail("Yalnızca onaylanmış randevu için değişiklik önerilebilir.");
        if (appointment.StartTime <= now)
            return ActionResult.Fail("Saati gelmiş randevu için değişiklik önerilemez.");
        if (appointment.StudentId is null || appointment.InstructorId is null)
            return ActionResult.Fail("Bu randevu için değişiklik önerilemez.");

        // Saat değişmiyorsa önerilen saat alanlarına mevcut saat ve süre yazılır (hiçbir saat ayrılmaz ya da taşınmaz)
        var start = requestedStart ?? appointment.StartTime;
        var durationHours = input.ChangeTime ? input.DurationHours : appointment.DurationHours;
        var timeChanged = start != appointment.StartTime || durationHours != appointment.DurationHours;

        // Yeni buluşma yeri: yalnızca Yüz Yüze, iki kişinin şehri hâlâ aynıysa; Randevu Al ile aynı yer kuralları
        string? placeName = null;
        decimal? placeLat = null, placeLon = null;
        if (input.ChangePlace)
        {
            if (appointment.MeetingType != MeetingType.FaceToFace)
                return ActionResult.Fail("Online randevuda buluşma yeri önerilemez.");
            if (await FindPlaceSearchCityAsync(id, userId) is null)
                return ActionResult.Fail("Şehirleriniz artık farklı olduğu için yeni buluşma yeri önerilemez. Yalnızca gün/saat önerebilirsiniz.");
            if (!PlaceSearchService.TryValidatePlace(input.PlaceName, input.PlaceLatitude, input.PlaceLongitude,
                    out var cleanedName, out placeLat, out placeLon, out var placeError))
                return ActionResult.Fail(placeError);

            // Mevcut yerle aynı (ad ve koordinat) öneri yok sayılır; saat de değişmiyorsa öneri reddedilir (aşağıda)
            var samePlace = cleanedName == appointment.MeetingPlaceName
                && placeLat == appointment.MeetingLatitude && placeLon == appointment.MeetingLongitude;
            if (!samePlace)
                placeName = cleanedName;
            else
                placeLat = placeLon = null;
        }

        if (!timeChanged && placeName is null)
            return ActionResult.Fail(input.ChangePlace
                ? "Önerdiğiniz saat ve yer mevcut haliyle aynı."
                : "Önerdiğiniz saat mevcut saatle aynı.");

        var isStudent = appointment.StudentId == userId;
        string[] userIds = [appointment.StudentId, appointment.InstructorId];
        var newStarts = Enumerable.Range(0, durationHours).Select(i => start.AddHours(i)).ToList();
        var oldStarts = Enumerable.Range(0, appointment.DurationHours).Select(i => appointment.StartTime.AddHours(i)).ToList();

        try
        {
            if (timeChanged)
            {
                // Bu saatleri hâlâ tutan süresi dolmuş talepler kapatılır (sıradaki bekleme kaydı önce gelir)
                await waitlist.ExpireStaleRequestsAsync(userIds, newStarts);

                var taken = await db.TimeSlots
                    .Where(ts => userIds.Contains(ts.UserId) && newStarts.Contains(ts.SlotStart) && ts.AppointmentId != id)
                    .Select(ts => ts.UserId)
                    .ToListAsync();
                if (taken.Count > 0)
                {
                    await transaction.CommitAsync();   // varsa süresi dolan talep ve sıradan aktifleşme kaydedilir
                    return ActionResult.Fail(taken.Contains(userId)
                        ? "Bu saatte başka bir randevunuz var. Lütfen başka bir saat seçin."
                        : "Seçtiğiniz saat dolu. Lütfen başka bir saat seçin.");
                }

                // Eski saatle çakışan saatler zaten bu randevuya ait; yalnızca yeni saatler geçici ayrılır
                foreach (var slotStart in newStarts.Except(oldStarts))
                    foreach (var uid in userIds)
                        db.TimeSlots.Add(new TimeSlot { UserId = uid, SlotStart = slotStart, AppointmentId = id, IsProposal = true });
            }

            appointment.Status = AppointmentStatus.ChangeProposed;
            appointment.ProposedStartTime = start;
            appointment.ProposedDurationHours = (byte)durationHours;
            appointment.ProposedByUserId = userId;
            appointment.ProposedAt = now;
            appointment.ProposedPlaceName = placeName;
            appointment.ProposedLatitude = placeLat;
            appointment.ProposedLongitude = placeLon;
            // Öneren kendisi yaptı, karşı tarafta kırmızı nokta çıkar
            appointment.StudentSeen = isStudent;
            appointment.InstructorSeen = !isStudent;

            // RowVersion: randevu bu arada değiştiyse burada hata verir; aynı saati başkası aldıysa benzersiz indeks hata verir
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return new ActionResult(true, "Değişiklik öneriniz gönderildi.");
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            return ActionResult.Fail("Bu randevu az önce güncellendi. Lütfen sayfayı yenileyin.");
        }
        catch (DbUpdateException ex) when (IsSlotConflict(ex))
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            return ActionResult.Fail("Bu saat az önce doldu. Lütfen başka bir saat seçin.");
        }
    }

    // Kabul: yalnızca öneriyi yapmayan taraf. Randevu yeni saate taşınır, eski saatler boşa çıkar.
    public Task<ActionResult> AcceptAsync(int id, string userId) => RespondAsync(id, userId, ProposalAction.Accept);

    // Red: yalnızca öneriyi yapmayan taraf. Randevu eski saatiyle devam eder.
    public Task<ActionResult> RejectAsync(int id, string userId) => RespondAsync(id, userId, ProposalAction.Reject);

    // Geri çekme: yalnızca öneriyi yapan taraf. Randevu eski saatiyle devam eder.
    public Task<ActionResult> WithdrawAsync(int id, string userId) => RespondAsync(id, userId, ProposalAction.Withdraw);

    private enum ProposalAction { Accept, Reject, Withdraw }

    private async Task<ActionResult> RespondAsync(int id, string userId, ProposalAction action)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        var appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == id && (a.StudentId == userId || a.InstructorId == userId));
        if (appointment is null)
            return new ActionResult(false, "Randevu bulunamadı.", NotFound: true);
        if (appointment.Status != AppointmentStatus.ChangeProposed)
            return ActionResult.Fail("Bu randevuda yanıt bekleyen bir öneri yok.");

        var proposedByMe = appointment.ProposedByUserId == userId;
        if (action == ProposalAction.Withdraw && !proposedByMe)
            return ActionResult.Fail("Öneriyi yalnızca yapan kişi geri çekebilir.");
        if (action != ProposalAction.Withdraw && proposedByMe)
            return ActionResult.Fail("Kendi önerinizi kabul edemez ya da reddedemezsiniz; isterseniz geri çekebilirsiniz.");

        var isStudent = appointment.StudentId == userId;
        var now = AppClock.Now;
        try
        {
            // Süresi dolmuş öneri: işlem yapılmaz, öneri düşer (süre dolması kırmızı nokta çıkarmaz)
            if (AppointmentRules.IsExpiredProposal(appointment.Status, appointment.ProposedAt, appointment.StartTime, appointment.ProposedStartTime, now))
            {
                await DropProposalAsync(appointment);
                await transaction.CommitAsync();
                return ActionResult.Fail("Bu önerinin süresi dolmuş. Randevu önceki haliyle devam ediyor.");
            }

            // İşlemi yapan gördü, karşı tarafta kırmızı nokta çıkar
            appointment.StudentSeen = isStudent;
            appointment.InstructorSeen = !isStudent;

            if (action == ProposalAction.Accept)
            {
                await MoveToProposalAsync(appointment);
                await transaction.CommitAsync();
                return new ActionResult(true, "Öneri kabul edildi. Randevu güncellendi.");
            }

            await DropProposalAsync(appointment);
            await transaction.CommitAsync();
            return new ActionResult(true, action == ProposalAction.Reject
                ? "Öneri reddedildi. Randevu önceki haliyle devam ediyor."
                : "Öneriniz geri çekildi. Randevu önceki haliyle devam ediyor.");
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            return ActionResult.Fail("Bu randevu az önce güncellendi. Lütfen sayfayı yenileyin.");
        }
    }

    // Kabul: randevu yeni saate taşınır. Yeni saatte olmayan eski saatler silinir (sıradaki aktifleşir),
    // önerilen saatler kalıcı olur. RowVersion kontrolü SaveChanges'te yapılır.
    private async Task MoveToProposalAsync(Appointment appointment)
    {
        var newStart = appointment.ProposedStartTime!.Value;
        var newDuration = appointment.ProposedDurationHours!.Value;
        var newStarts = Enumerable.Range(0, newDuration).Select(i => newStart.AddHours(i)).ToList();

        appointment.StartTime = newStart;
        appointment.DurationHours = newDuration;
        // Yer önerildiyse buluşma yeri de yeni yere geçer (elle yazılmış yerde koordinatlar boşalır)
        if (appointment.ProposedPlaceName is not null)
        {
            appointment.MeetingPlaceName = appointment.ProposedPlaceName;
            appointment.MeetingLatitude = appointment.ProposedLatitude;
            appointment.MeetingLongitude = appointment.ProposedLongitude;
        }
        ClearProposal(appointment);
        await db.SaveChangesAsync();

        var id = appointment.Id;
        await waitlist.ReleaseAsync(db.TimeSlots.Where(t => t.AppointmentId == id && !t.IsProposal && !newStarts.Contains(t.SlotStart)));
        await db.TimeSlots.Where(t => t.AppointmentId == id && t.IsProposal)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsProposal, false));
    }

    // Red / geri çekme / süre dolması: öneri düşer, geçici saatler silinir (sıradaki aktifleşir), eski saat devam eder
    private async Task DropProposalAsync(Appointment appointment)
    {
        ClearProposal(appointment);
        await db.SaveChangesAsync();

        var id = appointment.Id;
        await waitlist.ReleaseAsync(db.TimeSlots.Where(t => t.AppointmentId == id && t.IsProposal));
    }

    private static void ClearProposal(Appointment appointment)
    {
        appointment.Status = AppointmentStatus.Approved;
        appointment.ProposedStartTime = null;
        appointment.ProposedDurationHours = null;
        appointment.ProposedByUserId = null;
        appointment.ProposedAt = null;
        appointment.ProposedPlaceName = null;
        appointment.ProposedLatitude = null;
        appointment.ProposedLongitude = null;
    }

    // Görev (BackgroundService): süresi dolan öneriler düşer, geçici saatler silinir, sıradakiler aktifleşir.
    // Güncelleme "durum hâlâ Değişiklik Önerildi ve süresi dolmuş" koşuluyla yapılır; görülme alanları değişmez.
    public async Task<(int Expired, WaitlistService.ActivationCounts Waitlist)> ExpireProposalsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var ids = await db.Appointments.Where(AppointmentRules.ExpiredProposal(now)).Select(a => a.Id).ToListAsync(cancellationToken);
        if (ids.Count == 0)
            return (0, WaitlistService.ActivationCounts.None);

        var expired = await db.Appointments
            .Where(a => ids.Contains(a.Id))
            .Where(AppointmentRules.ExpiredProposal(now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AppointmentStatus.Approved)
                .SetProperty(a => a.ProposedStartTime, (DateTime?)null)
                .SetProperty(a => a.ProposedDurationHours, (byte?)null)
                .SetProperty(a => a.ProposedByUserId, (string?)null)
                .SetProperty(a => a.ProposedAt, (DateTime?)null)
                .SetProperty(a => a.ProposedPlaceName, (string?)null)
                .SetProperty(a => a.ProposedLatitude, (decimal?)null)
                .SetProperty(a => a.ProposedLongitude, (decimal?)null), cancellationToken);

        // Yalnızca öneri alanı boşalmış (gerçekten düşen) randevuların geçici saatleri silinir;
        // bu arada kabul edilen öneride IsProposal zaten false olur
        var counts = await waitlist.ReleaseAsync(db.TimeSlots.Where(t => ids.Contains(t.AppointmentId) && t.IsProposal
            && t.Appointment.Status == AppointmentStatus.Approved && t.Appointment.ProposedStartTime == null));

        await transaction.CommitAsync(cancellationToken);
        return (expired, counts);
    }

    // 2601: benzersiz indeks ihlali, 2627: benzersiz kısıt ihlali (TimeSlots: UserId + SlotStart)
    private static bool IsSlotConflict(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql && sql.Message.Contains(SlotIndexName);
}
