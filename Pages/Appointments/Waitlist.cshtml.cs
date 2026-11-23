using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Appointments;

// Bekleme Listem: kişinin dolu saatler için girdiği bekleme kayıtları. "Bekliyor" kayıttan kendi isteğiyle çıkılabilir.
// Saat boşalınca talep otomatik oluşur ve Gönderdiğim Randevular'da görünür. Telefon ve IBAN bu sayfaya gönderilmez.
[Authorize(Roles = AppRoles.Member)]
public class WaitlistModel(WaitlistService waitlist, UserManager<ApplicationUser> userManager) : PageModel
{
    public List<WaitlistService.WaitlistRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() =>
        Rows = await waitlist.ListAsync(userManager.GetUserId(User)!);

    // Listeden çık: yalnızca kaydın sahibi, yalnızca bekleyen kayıt (sunucuda kontrol edilir)
    public async Task<IActionResult> OnPostWithdrawAsync(int id)
    {
        var (succeeded, message) = await waitlist.WithdrawAsync(id, userManager.GetUserId(User)!);
        TempData["StatusMessage"] = message;
        TempData["StatusIsError"] = !succeeded;
        return RedirectToPage();
    }
}
