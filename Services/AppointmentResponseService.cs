using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;

namespace YetenekPlatformu.Services;

// Randevu İstekleri / Gönderdiğim Randevular: listeler, onay ve red.
// Kurallar sunucuda: yalnızca talebin eğitmeni işlem yapar, yalnızca Beklemede -> Onaylandı / Reddedildi,
// süresi dolmuş talep işlenemez, aynı talebe aynı anda iki işlem gelirse RowVersion ile yalnızca biri geçer.
public class AppointmentResponseService(ApplicationDbContext db, WaitlistService waitlist)
{
    public const int MeetingLinkMaxLength = 500;
    public const int CancelReasonMaxLength = 250;

    // Listede bir randevu. Telefon ve link yalnızca Onaylandı / Değişiklik Önerildi durumunda ve yalnızca taraflara doldurulur.
    public record AppointmentRow(
        int Id,
        string? OtherPartyId,
        string? OtherPartyName,          // boşsa karşı taraf üyeliğini silmiş ("Silinmiş Üye")
        string SkillName,
        DateTime StartTime,
        byte DurationHours,
        MeetingType MeetingType,
        AppointmentStatus Status,        // süresi dolmuş bekleyen talep için Expired
        DateTime Deadline,               // bekleyen talebin kapanacağı an: 24 saat ya da ders saati, hangisi önceyse
        string? OtherPartyPhone,
        string? MeetingLink,
        bool IsNew,                      // bu kişi son değişikliği henüz görmedi ("Yeni" etiketi)
        bool CanCancel,                  // İptal Et / Talebi Geri Çek düğmesi (sunucu işlemde yeniden kontrol eder)
        bool CancelledByMe,              // İptal Edildi: iptal eden bu kişi mi
        string? CancelReason,            // İptal Edildi: isteğe bağlı neden (iki tarafa gösterilir)
        bool FromWaitlist,               // talep bekleme listesinden otomatik oluştu
        bool CanPropose,                 // Değişiklik Öner düğmesi (sunucu işlemde yeniden kontrol eder)
        DateTime? ProposedStartTime,     // Değişiklik Önerildi: önerilen yeni başlangıç
        byte? ProposedDurationHours,     // Değişiklik Önerildi: önerilen süre
        bool ProposedByMe,               // öneriyi bu kişi mi yaptı (değilse yanıt bekleniyor: Kabul Et / Reddet)
        DateTime? ProposalDeadline,      // öneri yanıtlanmazsa düşeceği an
        string? PlaceName,               // Yüz Yüze buluşma yeri (yalnızca iki tarafın listelerinde)
        decimal? PlaceLatitude,          // listeden seçilen yerin koordinatları (elle yazılan yerde boş)
        decimal? PlaceLongitude,
        string? ProposedPlaceName,       // Değişiklik Önerildi: önerilen yeni buluşma yeri (boşsa yer değişmiyor)
        decimal? ProposedPlaceLatitude,  // önerilen yer listeden seçildiyse koordinatları
        decimal? ProposedPlaceLongitude);

    public record ActionResult(bool Succeeded, string Message, bool NotFound = false, bool LinkError = false)
    {
        public static ActionResult Fail(string message) => new(false, message);
    }

    public static string StatusName(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Pending => "Beklemede",
        AppointmentStatus.Approved => "Onaylandı",
        AppointmentStatus.Rejected => "Reddedildi",
        AppointmentStatus.Cancelled => "İptal Edildi",
        AppointmentStatus.ChangeProposed => "Değişiklik Önerildi",
        AppointmentStatus.Completed => "Tamamlandı",
        AppointmentStatus.Expired => "Süresi Doldu",
        _ => status.ToString()
    };

    // Bekleyen talep 24 saat geçince ya da ders saati gelince kapanır. Kural AppointmentRules'ta;
    // AppointmentStatusWorker bunu düzenli yapar, burada da işlem anında ayrıca kontrol edilir.
    public static bool IsExpiredPending(AppointmentStatus status, DateTime expiresAt, DateTime startTime, DateTime now) =>
        AppointmentRules.IsExpiredPending(status, expiresAt, startTime, now);

    // Görüşme linki: her güvenli https adresi (Meet, Zoom, Teams, Jitsi...). Geçerliyse temizlenmiş hali döner.
    public static bool TryValidateMeetingLink(string? input, out string link, out string error)
    {
        link = (input ?? string.Empty).Trim();
        error = string.Empty;

        if (link.Length == 0)
            error = "Online randevuyu onaylamak için görüşme linkini girin.";
        else if (link.Length > MeetingLinkMaxLength)
            error = $"Link en fazla {MeetingLinkMaxLength} karakter olabilir.";
        else if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            error = "Link https:// ile başlayan geçerli bir adres olmalıdır (ör. https://meet.google.com/abc-defg-hij).";
        else if (!uri.Host.Contains('.') || uri.Host.StartsWith('.') || uri.Host.EndsWith('.'))
            error = "Link geçerli bir alan adı içermelidir (ör. meet.google.com).";
        else if (!string.IsNullOrEmpty(uri.UserInfo))
            error = "Link kullanıcı adı veya şifre içeremez.";
        else if (link.Any(char.IsWhiteSpace))
            error = "Link boşluk içeremez.";

        return error.Length == 0;
    }

    // asInstructor: Randevu İstekleri (eğitmeni olduğum), değilse Gönderdiğim Randevular (öğrencisi olduğum)
    public async Task<List<AppointmentRow>> ListAsync(string userId, bool asInstructor)
    {
        var now = AppClock.Now;

        // Karşı taraf: Randevu İstekleri'nde öğrenci, Gönderdiğim Randevular'da eğitmen.
        // Telefon ve link yalnızca Onaylandı ya da Değişiklik Önerildi durumunda (randevu geçerli) sayfaya gelir.
        var rows = await db.Appointments
            .Where(a => asInstructor ? a.InstructorId == userId : a.StudentId == userId)
            .Select(a => new
            {
                a.Id,
                OtherId = asInstructor ? a.StudentId : a.InstructorId,
                OtherName = asInstructor
                    ? (a.Student == null ? null : a.Student.FirstName + " " + a.Student.LastName)
                    : (a.Instructor == null ? null : a.Instructor.FirstName + " " + a.Instructor.LastName),
                OtherPhone = a.Status == AppointmentStatus.Approved || a.Status == AppointmentStatus.ChangeProposed
                    ? (asInstructor
                        ? (a.Student == null ? null : a.Student.PhoneNumber)
                        : (a.Instructor == null ? null : a.Instructor.PhoneNumber))
                    : null,
                Link = a.Status == AppointmentStatus.Approved || a.Status == AppointmentStatus.ChangeProposed ? a.MeetingLink : null,
                SkillName = a.Skill.Name, a.StartTime, a.DurationHours, a.MeetingType, a.Status, a.ExpiresAt,
                IsNew = asInstructor ? !a.InstructorSeen : !a.StudentSeen,
                a.CancelledByUserId, a.CancelReason,
                a.ProposedStartTime, a.ProposedDurationHours, a.ProposedByUserId, a.ProposedAt,
                a.MeetingPlaceName, a.MeetingLatitude, a.MeetingLongitude,
                a.ProposedPlaceName, a.ProposedLatitude, a.ProposedLongitude,
                FromWaitlist = db.WaitlistEntries.Any(w => w.ActivatedAppointmentId == a.Id)
            })
            .ToListAsync();

        return rows
            .Select(r =>
            {
                // Süresi dolmuş ama görevin henüz kapatmadığı kayıtlar sonuçlarıyla gösterilir:
                // bekleyen talep "Süresi Doldu", öneri düşmüş (randevu eski saatiyle Onaylandı),
                // bitiş saati geçmiş onaylı ders "Tamamlandı" (görev de bu kuralla Tamamlandı yapar: AppointmentRules.FinishedApproved)
                var proposalExpired = AppointmentRules.IsExpiredProposal(r.Status, r.ProposedAt, r.StartTime, r.ProposedStartTime, now);
                var status = IsExpiredPending(r.Status, r.ExpiresAt, r.StartTime, now) ? AppointmentStatus.Expired
                    : proposalExpired ? AppointmentStatus.Approved
                    : r.Status;
                if (status == AppointmentStatus.Approved && r.StartTime.AddHours(r.DurationHours) <= now)
                    status = AppointmentStatus.Completed;
                var hasProposal = status == AppointmentStatus.ChangeProposed;
                // Telefon ve link yalnızca geçerli (Onaylandı / Değişiklik Önerildi) randevuda; bitmiş ders "Tamamlandı" gösterildiğinde gönderilmez
                var showContact = status is AppointmentStatus.Approved or AppointmentStatus.ChangeProposed;
                var active = (status == AppointmentStatus.Approved || hasProposal) && r.StartTime > now;

                return new AppointmentRow(
                    r.Id, r.OtherId, r.OtherName, r.SkillName, r.StartTime, r.DurationHours, r.MeetingType,
                    status,
                    r.ExpiresAt < r.StartTime ? r.ExpiresAt : r.StartTime,
                    showContact && r.OtherPhone is not null ? MemberValidation.FormatPhone(r.OtherPhone) : null,
                    showContact ? r.Link : null,
                    r.IsNew,
                    // Onaylı (ya da öneri bekleyen) randevuyu iki taraf ders başlayana kadar iptal eder;
                    // bekleyen talebi yalnızca gönderen geri çeker
                    active || (!asInstructor && status == AppointmentStatus.Pending),
                    r.CancelledByUserId == userId,
                    status == AppointmentStatus.Cancelled ? r.CancelReason : null,
                    r.FromWaitlist,
                    // Değişiklik önerisi: yalnızca onaylı randevuda, ders başlamadan, açık öneri yokken
                    status == AppointmentStatus.Approved && r.StartTime > now,
                    hasProposal ? r.ProposedStartTime : null,
                    hasProposal ? r.ProposedDurationHours : null,
                    hasProposal && r.ProposedByUserId == userId,
                    hasProposal ? AppointmentRules.ProposalDeadline(r.ProposedAt, r.StartTime, r.ProposedStartTime) : null,
                    r.MeetingType == MeetingType.FaceToFace ? r.MeetingPlaceName : null,
                    r.MeetingType == MeetingType.FaceToFace ? r.MeetingLatitude : null,
                    r.MeetingType == MeetingType.FaceToFace ? r.MeetingLongitude : null,
                    hasProposal && r.MeetingType == MeetingType.FaceToFace ? r.ProposedPlaceName : null,
                    hasProposal && r.MeetingType == MeetingType.FaceToFace ? r.ProposedLatitude : null,
                    hasProposal && r.MeetingType == MeetingType.FaceToFace ? r.ProposedLongitude : null);
            })
            // Yanıt bekleyenler (bekleyen talep, değişiklik önerisi) üstte (yakın saat önce), diğerleri altta (yeni olan önce)
            .OrderBy(r => NeedsAttention(r) ? 0 : 1)
            .ThenBy(r => NeedsAttention(r) ? r.StartTime.Ticks : -r.StartTime.Ticks)
            .ToList();
    }

    private static bool NeedsAttention(AppointmentRow r) =>
        r.Status is AppointmentStatus.Pending or AppointmentStatus.ChangeProposed;

    // ---- Liste filtresi (Randevu İstekleri / Gönderdiğim Randevular): önce zaman (Tümü / Yaklaşan / Geçmiş),
    // altında o zamana uyan durumlar. Adresteki değerler (?status=...&time=...) yalnızca bu listeden kabul edilir;
    // başka her değer ve seçili zamana uymayan durum "Tümü" sayılır.

    public enum TimeFilter { All, Upcoming, Past }

    // Değişiklik Önerildi için ayrı düğme yok: bu randevular Onaylandı filtresinde görünür ve Onaylandı sayısına dahildir
    // (adreste "changeproposed" gelirse geçersiz sayılır, yani Tümü)
    public static readonly IReadOnlyList<(string Value, AppointmentStatus Status)> StatusFilterValues =
    [
        ("pending", AppointmentStatus.Pending),
        ("approved", AppointmentStatus.Approved),
        ("completed", AppointmentStatus.Completed),
        ("cancelled", AppointmentStatus.Cancelled),
        ("rejected", AppointmentStatus.Rejected),
        ("expired", AppointmentStatus.Expired)
    ];

    // Seçili zamana göre sunulan durumlar:
    //   Yaklaşan: Beklemede, Onaylandı (henüz yapılmamış ve geçerli)
    //   Geçmiş: Tamamlandı, İptal Edildi, Reddedildi, Süresi Doldu (bitmiş ya da kapanmış)
    //   Tümü: hepsi
    public static IReadOnlyList<AppointmentStatus> StatusesFor(TimeFilter time) => time switch
    {
        TimeFilter.Upcoming => [AppointmentStatus.Pending, AppointmentStatus.Approved],
        TimeFilter.Past => [AppointmentStatus.Completed, AppointmentStatus.Cancelled, AppointmentStatus.Rejected, AppointmentStatus.Expired],
        _ => StatusFilterValues.Select(v => v.Status).ToList()
    };

    // Status null: Tümü
    public record ListFilter(AppointmentStatus? Status, TimeFilter Time)
    {
        public static readonly ListFilter All = new(null, TimeFilter.All);

        // Adres değerlerini güvenli okur: bilinmeyen, boş ya da farklı yazılmış değer Tümü sayılır;
        // durum seçili zamana uymuyorsa (ör. time=past&status=approved) durum Tümü sayılır
        public static ListFilter Parse(string? status, string? time)
        {
            AppointmentStatus? s = null;
            foreach (var (value, st) in StatusFilterValues)
                if (string.Equals(status, value, StringComparison.Ordinal))
                    s = st;
            var t = time switch
            {
                "upcoming" => TimeFilter.Upcoming,
                "past" => TimeFilter.Past,
                _ => TimeFilter.All
            };
            if (s is AppointmentStatus chosen && !StatusesFor(t).Contains(chosen))
                s = null;
            return new ListFilter(s, t);
        }

        // Zaman değişince: seçili durum yeni zamana uyuyorsa korunur, uymuyorsa Tümü (adres değeri olarak)
        public string? StatusValueFor(TimeFilter time) =>
            Status is AppointmentStatus st && StatusesFor(time).Contains(st) ? StatusValue : null;

        // Adreste taşınan değerler (Tümü için null: adrese hiç yazılmaz)
        public string? StatusValue => Status is AppointmentStatus st ? StatusFilterValues.First(v => v.Status == st).Value : null;
        public string? TimeValue => Time switch { TimeFilter.Upcoming => "upcoming", TimeFilter.Past => "past", _ => null };
        public bool IsAll => Status is null && Time == TimeFilter.All;
    }

    // Yaklaşan: henüz yapılmamış ve geçerli randevu, yani kartta gösterilen durum Beklemede ya da Onaylandı
    // (Değişiklik Önerildi Onaylandı sayılır) ve dersin bitişi (başlangıç + süre) henüz gelmemiş; devam eden ders de yaklaşan.
    // Değişiklik Önerildi randevuda geçerli (mevcut) saat kullanılır, önerilen saat değil.
    // Geçmiş: diğer her şey (Tamamlandı, İptal Edildi, Reddedildi, Süresi Doldu; gelecek tarihli iptal/red de geçmiştir).
    // Bitişi geçmiş onaylı ders ListAsync'te zaten "Tamamlandı" gösterilir, bu yüzden Geçmiş'te Tamamlandı olarak görünür.
    public static bool IsUpcoming(AppointmentRow row, DateTime now) =>
        FilterStatusOf(row.Status) is AppointmentStatus.Pending or AppointmentStatus.Approved
        && row.StartTime.AddHours(row.DurationHours) > now;

    // Zaman seçimine uyan satırlar ve sıralaması:
    //   Tümü: ListAsync'teki sıra aynen (yanıt bekleyenler üstte yakın tarih önce, diğerleri yeni tarih önce)
    //   Yaklaşan: en yakın tarih en üstte, ileriye doğru; Geçmiş: en yeni geçmiş en üstte, eskiye doğru
    public static List<AppointmentRow> ApplyTime(IEnumerable<AppointmentRow> rows, TimeFilter time, DateTime now) => time switch
    {
        TimeFilter.Upcoming => rows.Where(r => IsUpcoming(r, now)).OrderBy(r => r.StartTime).ThenBy(r => r.Id).ToList(),
        TimeFilter.Past => rows.Where(r => !IsUpcoming(r, now)).OrderByDescending(r => r.StartTime).ThenByDescending(r => r.Id).ToList(),
        _ => rows.ToList()
    };

    // Filtre açısından durum: Değişiklik Önerildi randevu hâlâ geçerli (onaylı) olduğu için Onaylandı sayılır.
    // Kartta yine "Değişiklik Önerildi" ve ⚠ görünür; bu yalnızca filtre ve sayılar içindir.
    public static AppointmentStatus FilterStatusOf(AppointmentStatus status) =>
        status == AppointmentStatus.ChangeProposed ? AppointmentStatus.Approved : status;

    // Durum filtresi: kartta gösterilen duruma göre (süresi dolmuş ama görevin henüz kapatmadığı talep "Süresi Doldu",
    // süresi dolmuş öneri "Onaylandı" sayılır; Değişiklik Önerildi Onaylandı filtresindedir). Sıralamayı değiştirmez.
    public static List<AppointmentRow> ApplyStatus(IEnumerable<AppointmentRow> rows, AppointmentStatus? status) =>
        status is AppointmentStatus s ? rows.Where(r => FilterStatusOf(r.Status) == s).ToList() : rows.ToList();

    // Sayfa açılınca "görüldü": yalnızca az önce listede gösterilen görülmemiş satırlar işaretlenir.
    // Okuma ile işaretleme arasında gelen yeni talep işaretlenmez, nokta onun için kalır.
    // Not: bu güncelleme satırın RowVersion değerini değiştirir (aynı anda yapılan onay "az önce güncellendi" der).
    public Task<int> MarkSeenAsync(string userId, bool asInstructor, IEnumerable<AppointmentRow> shownRows)
    {
        var ids = shownRows.Where(r => r.IsNew).Select(r => r.Id).ToList();
        if (ids.Count == 0)
            return Task.FromResult(0);

        return asInstructor
            ? db.Appointments
                .Where(a => ids.Contains(a.Id) && a.InstructorId == userId && !a.InstructorSeen)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.InstructorSeen, true))
            : db.Appointments
                .Where(a => ids.Contains(a.Id) && a.StudentId == userId && !a.StudentSeen)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.StudentSeen, true));
    }

    public Task<ActionResult> ApproveAsync(int appointmentId, string instructorId, string? meetingLink) =>
        RespondAsync(appointmentId, instructorId, approve: true, meetingLink);

    public Task<ActionResult> RejectAsync(int appointmentId, string instructorId) =>
        RespondAsync(appointmentId, instructorId, approve: false, null);

    private async Task<ActionResult> RespondAsync(int appointmentId, string instructorId, bool approve, string? meetingLink)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        // Yalnızca talebin eğitmeni: başkasının talebi "bulunamadı" sayılır (var olup olmadığı da belli edilmez)
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId && a.InstructorId == instructorId);
        if (appointment is null)
            return new ActionResult(false, "Talep bulunamadı.", NotFound: true);

        if (appointment.Status != AppointmentStatus.Pending)
            return ActionResult.Fail("Bu talep artık işlenemez.");

        var now = AppClock.Now;
        try
        {
            // Süresi dolmuş talep: işlem yapılmaz, talep Süresi Doldu olur ve saatleri boşa çıkar
            if (IsExpiredPending(appointment.Status, appointment.ExpiresAt, appointment.StartTime, now))
                return await ExpireAsync(appointment, transaction);

            if (approve)
            {
                string? link = null;
                if (appointment.MeetingType == MeetingType.Online)
                {
                    if (!TryValidateMeetingLink(meetingLink, out var cleaned, out var error))
                        return new ActionResult(false, error, LinkError: true);
                    link = cleaned;
                }

                // Yüz yüze randevuda link kaydedilmez; saatler dolu kalır
                appointment.Status = AppointmentStatus.Approved;
                appointment.MeetingLink = link;
            }
            else
            {
                appointment.Status = AppointmentStatus.Rejected;
            }

            appointment.RespondedAt = now;
            // Değişikliği öğrenci henüz görmedi, eğitmen kendisi yaptı (kırmızı nokta sonraki aşamada)
            appointment.StudentSeen = false;
            appointment.InstructorSeen = true;

            // RowVersion: talep bu arada başka bir işlemle değiştiyse burada hata verir
            await db.SaveChangesAsync();
            if (!approve)
                await waitlist.FreeSlotsAndActivateAsync(appointment.Id);   // reddedilen saat boşa çıkar, sıradaki aktifleşir

            await transaction.CommitAsync();
            return new ActionResult(true, approve ? "Talep onaylandı." : "Talep reddedildi.");
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            return ActionResult.Fail("Bu talep az önce güncellendi. Lütfen sayfayı yenileyin.");
        }
    }

    // İptal: onaylanmış randevuyu taraflardan biri ders başlayana kadar iptal eder; bekleyen talebi yalnızca
    // gönderen (öğrenci) geri çeker. İkisi de İptal Edildi olur, neden isteğe bağlıdır ve karşı tarafa gösterilir.
    // Saatler boşa çıkar, bekleme listesinde sıradaki varsa aynı transaction'da onun talebi oluşur.
    public async Task<ActionResult> CancelAsync(int appointmentId, string userId, string? reason)
    {
        var cleanedReason = string.IsNullOrWhiteSpace(reason) ? null : string.Join(' ', reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (cleanedReason is { Length: > CancelReasonMaxLength })
            return ActionResult.Fail($"İptal nedeni en fazla {CancelReasonMaxLength} karakter olabilir.");

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Yalnızca randevunun tarafları: başkasının randevusu "bulunamadı" sayılır
        var appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId && (a.StudentId == userId || a.InstructorId == userId));
        if (appointment is null)
            return new ActionResult(false, "Randevu bulunamadı.", NotFound: true);

        var isStudent = appointment.StudentId == userId;
        var now = AppClock.Now;
        try
        {
            if (appointment.Status == AppointmentStatus.Pending)
            {
                // Eğitmen bekleyen talebi iptal etmez, reddeder
                if (!isStudent)
                    return ActionResult.Fail("Bekleyen talebi onaylayabilir ya da reddedebilirsiniz.");
                if (IsExpiredPending(appointment.Status, appointment.ExpiresAt, appointment.StartTime, now))
                    return await ExpireAsync(appointment, transaction);
            }
            else if (appointment.Status is AppointmentStatus.Approved or AppointmentStatus.ChangeProposed)
            {
                // Öneri bekleyen randevu da iptal edilebilir: eski ve önerilen saatlerin hepsi boşa çıkar
                if (appointment.StartTime <= now)
                    return ActionResult.Fail("Saati gelmiş ya da geçmiş randevu iptal edilemez.");
            }
            else
            {
                return ActionResult.Fail("Bu randevu artık iptal edilemez.");
            }

            var wasPending = appointment.Status == AppointmentStatus.Pending;
            appointment.Status = AppointmentStatus.Cancelled;
            appointment.CancelledByUserId = userId;
            appointment.CancelReason = cleanedReason;
            appointment.ProposedStartTime = null;
            appointment.ProposedDurationHours = null;
            appointment.ProposedByUserId = null;
            appointment.ProposedAt = null;
            appointment.ProposedPlaceName = null;
            appointment.ProposedLatitude = null;
            appointment.ProposedLongitude = null;
            // İptal eden kendisi yaptı, karşı tarafta kırmızı nokta çıkar
            appointment.StudentSeen = isStudent;
            appointment.InstructorSeen = !isStudent;

            // RowVersion: randevu bu arada başka bir işlemle değiştiyse burada hata verir
            await db.SaveChangesAsync();
            await waitlist.FreeSlotsAndActivateAsync(appointment.Id);
            await transaction.CommitAsync();
            return new ActionResult(true, wasPending ? "Talebiniz geri çekildi." : "Randevu iptal edildi.");
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            return ActionResult.Fail("Bu randevu az önce güncellendi. Lütfen sayfayı yenileyin.");
        }
    }

    // Süresi dolmuş talep: işlem yapılmaz, talep Süresi Doldu olur, saatleri boşa çıkar ve sıradaki aktifleşir
    private async Task<ActionResult> ExpireAsync(Appointment appointment, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        appointment.Status = AppointmentStatus.Expired;
        await db.SaveChangesAsync();
        await waitlist.FreeSlotsAndActivateAsync(appointment.Id);
        await transaction.CommitAsync();
        return ActionResult.Fail("Bu talebin süresi dolmuş. Talep kapatıldı.");
    }
}
