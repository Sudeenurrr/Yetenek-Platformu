using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Account;

// Üyeliğimi İptal Et (Profilim sayfasındaki butondan açılır). Admin rolündeki kullanıcıya kapalıdır.
[Authorize]
public class DeleteModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AccountDeletionService deletionService) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Şifre zorunludur.")]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    public bool IsAdmin { get; private set; }

    public void OnGet()
    {
        IsAdmin = User.IsInRole(AppRoles.Admin);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Admin silme yapamaz (sunucu tarafında da engellenir)
        IsAdmin = User.IsInRole(AppRoles.Admin);
        if (IsAdmin)
            return Page();

        if (!ModelState.IsValid)
            return Page();

        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return RedirectToPage("/Index");

        // Şifre kontrolü; yanlış denemeler hesap kilidine sayılır
        var check = await signInManager.CheckPasswordSignInAsync(user, Password!, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Çok sayıda hatalı deneme yapıldı. Hesabınız 2 dakika kilitlendi, lütfen sonra tekrar deneyin.");
            return Page();
        }
        if (!check.Succeeded)
        {
            ModelState.AddModelError(nameof(Password), "Şifre hatalı.");
            return Page();
        }

        var result = await deletionService.DeleteAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Üyelik silinemedi. Lütfen tekrar deneyin.");
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        await signInManager.SignOutAsync();
        TempData["StatusMessage"] = "Üyeliğiniz iptal edildi.";
        return RedirectToPage("/Index");
    }
}
