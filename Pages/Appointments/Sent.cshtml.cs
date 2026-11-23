using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Appointments;

// Gönderdiğim Randevular: öğrencisi olduğum talepler. Bekleyen talep geri çekilebilir,
// onaylanmış randevu ders başlayana kadar iptal edilebilir; değişiklik önerisi kabul / red / geri çekme burada da yapılır.
// Kurallar AppointmentResponseService ve AppointmentChangeService'te, sunucuda.
[Authorize(Roles = AppRoles.Member)]
public class SentModel(
    AppointmentResponseService responses,
    AppointmentChangeService changes,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db) : PageModel
{
    public AppointmentListViewModel List { get; private set; } = null!;

    // Bekleme Listem bağlantısında gösterilen, hâlâ bekleyen kayıt sayısı
    public int WaitingCount { get; private set; }

    private string Me => userManager.GetUserId(User)!;

    // Liste filtresi adresten okunur (?status=...&time=...); formların adresinde de taşındığı için işlemlerde de gelir.
    // Geçersiz değer Tümü sayılır (AppointmentResponseService.ListFilter.Parse).
    [BindProperty(SupportsGet = true, Name = "status")]
    public string? StatusParam { get; set; }

    [BindProperty(SupportsGet = true, Name = "time")]
    public string? TimeParam { get; set; }

    public AppointmentResponseService.ListFilter Filter => AppointmentResponseService.ListFilter.Parse(StatusParam, TimeParam);

    public async Task OnGetAsync() => await LoadAsync();

    // Bekleyen talebi geri çek ya da onaylı randevuyu iptal et (neden isteğe bağlı)
    public async Task<IActionResult> OnPostCancelAsync(int id, string? reason) =>
        await HandleAsync(id, await responses.CancelAsync(id, Me, reason), reason, cancelFailed: true);

    // Değişiklik önerisi: kabul, red (yanıt bekleyen taraf), geri çekme (öneren)
    public async Task<IActionResult> OnPostAcceptChangeAsync(int id) => await HandleAsync(id, await changes.AcceptAsync(id, Me));

    public async Task<IActionResult> OnPostRejectChangeAsync(int id) => await HandleAsync(id, await changes.RejectAsync(id, Me));

    public async Task<IActionResult> OnPostWithdrawChangeAsync(int id) => await HandleAsync(id, await changes.WithdrawAsync(id, Me));

    private async Task<IActionResult> HandleAsync(int id, AppointmentResponseService.ActionResult result, string? reason = null, bool cancelFailed = false)
    {
        if (result.Succeeded)
        {
            TempData["StatusMessage"] = result.Message;
            // İşlemden sonra aynı filtreyle aynı karta dönülür (kart artık filtreye uymuyorsa listede görünmez)
            return RedirectToPage(pageName: null, pageHandler: null,
                routeValues: new { status = Filter.StatusValue, time = Filter.TimeValue }, fragment: $"randevu-{id}");
        }

        if (result.NotFound)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            TempData["StatusMessage"] = result.Message;
            await LoadAsync();
            return Page();
        }

        // Hata mesajı o randevunun kartında gösterilir, iptal hatasında yazılan neden kutuda kalır
        await LoadAsync(id, result.Message, reason, cancelFailed);
        return Page();
    }

    private async Task LoadAsync(int? errorId = null, string? error = null, string? enteredReason = null, bool cancelFailed = false)
    {
        var all = await responses.ListAsync(Me, asInstructor: false);
        var (shown, timeRows) = AppointmentListViewModel.ApplyFilter(all, Filter, AppClock.Now);

        // Sayfa açıldı: yalnızca filtreden geçip gösterilen değişiklikler "görüldü" olur (kartlarda bu seferlik "Yeni" etiketi kalır).
        // Filtre yüzünden görünmeyen değişiklik görülmemiş kalır, menüdeki kırmızı nokta onun için durur.
        await responses.MarkSeenAsync(Me, asInstructor: false, shown);

        var me = Me;
        WaitingCount = await db.WaitlistEntries.CountAsync(w => w.UserId == me && w.Status == WaitlistStatus.Waiting);

        List = new AppointmentListViewModel
        {
            Rows = shown,
            AsInstructor = false,
            EmptyText = "Henüz bir randevu talebi göndermediniz. Ara sayfasından bir üye bulup randevu alabilirsiniz.",
            Filter = Filter,
            TotalCount = all.Count,
            TimeCount = timeRows.Count,
            StatusCounts = AppointmentListViewModel.CountByStatus(timeRows),
            ErrorAppointmentId = errorId,
            ErrorMessage = error,
            EnteredReason = enteredReason,
            CancelFailed = cancelFailed
        };
    }
}
