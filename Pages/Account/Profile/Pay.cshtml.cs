using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Account.Profile;

// Ödeme Yap (/Account/Profile/Pay?id=...): tamamlanmış ders için eğitmene gönüllü ödeme (benzetim, gerçek para aktarılmaz).
// Yalnızca dersin öğrencisi açabilir; eğitmenin IBAN'ı yalnızca burada gösterilir.
// Bütün kurallar (öğrenci, Tamamlandı, eğitmen üye, izin verilen tutar, tek ödeme) PaymentService'te, sunucuda kontrol edilir.
// Profil sekmelerine yeni sekme eklenmez (ana sayfa kartları değişmesin); sayfa "Aldığım Dersler" sekmesinin altında açılır.
[Authorize(Roles = AppRoles.Member)]
public class PayModel(PaymentService payments, UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    // Seçilen tutar metin olarak alınır, sunucuda yalnızca izin verilen tam sayılardan biri kabul edilir
    [BindProperty]
    public string? Amount { get; set; }

    public PaymentService.PayTarget? Target { get; private set; }

    private string Me => userManager.GetUserId(User)!;

    public async Task<IActionResult> OnGetAsync()
    {
        Target = await payments.FindTargetAsync(Id, Me);
        return Target is null ? NotFoundPage() : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await payments.PayAsync(Id, Me, Amount);
        if (result.Succeeded)
        {
            TempData["StatusMessage"] = result.Message;
            return RedirectToPage("/Account/Profile/LessonsTaken", pageHandler: null, routeValues: null, fragment: $"ders-{Id}");
        }

        // Ders ödenmiş görünüyorsa sayfa "ödeme yapılmış" haliyle yeniden gösterilir
        Target = await payments.FindTargetAsync(Id, Me);
        if (Target is null)
            return NotFoundPage();

        ModelState.AddModelError(string.Empty, result.Message);
        return Page();
    }

    private PageResult NotFoundPage()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }
}
