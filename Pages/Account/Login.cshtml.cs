using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Account;

public class LoginModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Ad zorunludur.")]
        public string? FirstName { get; set; }

        [Required(ErrorMessage = "Soyad zorunludur.")]
        public string? LastName { get; set; }

        [Required(ErrorMessage = "Şifre zorunludur.")]
        [DataType(DataType.Password)]
        public string? Password { get; set; }
    }

    public IActionResult OnGet()
    {
        if (signInManager.IsSignedIn(User))
            return RedirectToPage("/Index");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var userName = UserNameHelper.BuildUserName(Input.FirstName, Input.LastName);

        // lockoutOnFailure: true -> 3 yanlış denemede hesap 2 dakika kilitlenir (Program.cs)
        var result = await signInManager.PasswordSignInAsync(userName, Input.Password!, isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            // Yalnızca site içi adreslere yönlendir
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                return LocalRedirect(ReturnUrl);
            return RedirectToPage("/Index");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Çok sayıda hatalı deneme yapıldı. Hesabınız 2 dakika kilitlendi, lütfen sonra tekrar deneyin.");
            return Page();
        }

        // Hangi bilginin yanlış olduğunu bilerek söylemiyoruz
        ModelState.AddModelError(string.Empty, "Ad, soyad veya şifre hatalı.");
        return Page();
    }
}
