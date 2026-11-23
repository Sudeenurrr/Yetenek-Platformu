using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Appointments;

// Kırmızı nokta için küçük kontrol adresi: /Appointments/Unseen -> { incoming: true/false, sent: true/false }
// Yalnızca var/yok bilgisi döner (hangi randevu olduğu gönderilmez) ve hiçbir şeyi "görüldü" yapmaz.
[Authorize(Roles = AppRoles.Member)]
public class UnseenModel(AppointmentNotificationService notifications, UserManager<ApplicationUser> userManager) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        // Tarayıcı ya da ara sunucu eski cevabı saklamasın
        Response.Headers.CacheControl = "no-store";

        var state = await notifications.GetAsync(userManager.GetUserId(User)!);
        return new JsonResult(new { incoming = state.Incoming, sent = state.Sent });
    }
}
