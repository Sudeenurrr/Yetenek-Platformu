using YetenekPlatformu.Models;
using YetenekPlatformu.Services;
using static YetenekPlatformu.Services.AppointmentResponseService;

namespace YetenekPlatformu.Pages.Appointments;

// Randevu İstekleri ve Gönderdiğim Randevular listesinin ortak verisi (_AppointmentList.cshtml, _AppointmentFilter.cshtml)
public class AppointmentListViewModel
{
    // Filtreden geçip ekranda gösterilen satırlar (yalnızca bunlar "görüldü" yapılır)
    public required IReadOnlyList<AppointmentRow> Rows { get; init; }

    // true: Randevu İstekleri (onay/red formları görünür), false: Gönderdiğim Randevular
    public required bool AsInstructor { get; init; }

    public required string EmptyText { get; init; }

    // Seçili filtre (adresteki ?status=...&time=..., sunucuda doğrulanmış)
    public ListFilter Filter { get; init; } = ListFilter.All;

    // Filtresiz toplam randevu sayısı: sıfırsa sayfanın kendi boş mesajı, değilse "Bu filtreye uyan randevu yok" gösterilir
    public int TotalCount { get; init; }

    // Durum düğmelerindeki sayılar: seçili zaman filtresine (Tümü / Yaklaşan / Geçmiş) uyan satırlardan
    public int TimeCount { get; init; }
    public IReadOnlyDictionary<AppointmentStatus, int> StatusCounts { get; init; } = new Dictionary<AppointmentStatus, int>();

    // Son işlemde hata olan randevu: mesaj o randevunun kartında gösterilir
    public int? ErrorAppointmentId { get; init; }
    public string? ErrorMessage { get; init; }

    // Link hatalıysa girilen link kutuda kalır
    public string? EnteredLink { get; init; }

    // İptal hata verirse yazılan neden kutuda kalır
    public string? EnteredReason { get; init; }

    // Hata iptal işleminden geldiyse iptal bölümü açık gelir
    public bool CancelFailed { get; init; }

    // Kişinin listesi filtrelenir: önce zaman (sıralama da buradan), sonra durum. "now" AppClock.Now (Türkiye saati).
    public static (List<AppointmentRow> Shown, List<AppointmentRow> TimeRows) ApplyFilter(
        IReadOnlyList<AppointmentRow> all, ListFilter filter, DateTime now)
    {
        var timeRows = ApplyTime(all, filter.Time, now);
        return (ApplyStatus(timeRows, filter.Status), timeRows);
    }

    // Düğme sayıları filtreyle aynı kuralla: Değişiklik Önerildi, Onaylandı sayısına dahil
    public static IReadOnlyDictionary<AppointmentStatus, int> CountByStatus(IEnumerable<AppointmentRow> rows) =>
        rows.GroupBy(r => FilterStatusOf(r.Status)).ToDictionary(g => g.Key, g => g.Count());
}
