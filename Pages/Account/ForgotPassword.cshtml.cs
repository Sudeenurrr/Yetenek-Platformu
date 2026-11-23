using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Account;

// Şifremi unuttum: ad, soyad ve kurtarma koduyla yeni şifre belirlenir (sistemde e-posta yok).
// Yanlış denemeler girişle aynı sayaca sayılır (3 yanlışta 2 dakika kilit); hata mesajı hangi bilginin yanlış olduğunu söylemez.
public class ForgotPasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    RecoveryCodeService recoveryCodes) : PageModel
{
    private const string WrongInfoError = "Ad, soyad veya kurtarma kodu hatalı.";
    private const string LockedError = "Çok sayıda hatalı deneme yapıldı. Hesabınız 2 dakika kilitlendi, lütfen sonra tekrar deneyin.";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "Ad zorunludur.")]
        public string? FirstName { get; set; }

        [Required(ErrorMessage = "Soyad zorunludur.")]
        public string? LastName { get; set; }

        [Required(ErrorMessage = "Kurtarma kodu zorunludur.")]
        public string? RecoveryCode { get; set; }

        [Required(ErrorMessage = "Yeni şifre zorunludur.")]
        [DataType(DataType.Password)]
        public string? NewPassword { get; set; }
    }

    public IActionResult OnGet()
    {
        // Giriş yapmış kişi şifresini profilden değiştirir
        if (signInManager.IsSignedIn(User))
            return RedirectToPage("/Account/Profile/Index");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (signInManager.IsSignedIn(User))
            return RedirectToPage("/Account/Profile/Index");

        // Hesaba bağlı olmayan kural önce: yeni şifre en az 6 karakter (bir şey açığa çıkarmaz)
        if (!string.IsNullOrEmpty(Input.NewPassword) && Input.NewPassword.Length < 6)
            ModelState.AddModelError("Input.NewPassword", MemberValidation.PasswordLengthError);

        if (!ModelState.IsValid)
            return Failed();

        var user = await userManager.FindByNameAsync(UserNameHelper.BuildUserName(Input.FirstName, Input.LastName));

        if (user is not null && await userManager.IsLockedOutAsync(user))
            return Failed(LockedError);

        // Hesap ya da kodu yoksa da aynı süreli kontrol yapılır ve aynı mesaj verilir
        if (!recoveryCodes.Verify(user, Input.RecoveryCode))
        {
            if (user is null)
                return Failed(WrongInfoError);

            // Yanlış deneme sayılır; 3. denemede hesap kilitlenir
            await userManager.AccessFailedAsync(user);
            return Failed(await userManager.IsLockedOutAsync(user) ? LockedError : WrongInfoError);
        }

        // Kod doğru: şifre Identity'nin sıfırlama akışıyla değişir. Oturum anahtarı yenilenir,
        // diğer cihazlardaki oturumlar en geç 1 dakikada kapanır (Program.cs).
        var token = await userManager.GeneratePasswordResetTokenAsync(user!);
        var result = await userManager.ResetPasswordAsync(user!, token, Input.NewPassword!);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError("Input.NewPassword", error.Description);
            return Failed();
        }

        await userManager.ResetAccessFailedCountAsync(user!);

        // Kullanılan kod geçersiz olur, yenisi bir kez gösterilir
        var newCode = await recoveryCodes.CreateAndSaveAsync(user!);
        TempData[RecoveryCodeService.TempDataCodeKey] = newCode;
        TempData[RecoveryCodeService.TempDataContextKey] = RecoveryCodeService.ContextReset;
        return RedirectToPage("/Account/RecoveryCode");
    }

    // Hata durumunda kod ve yeni şifre forma geri yazılmaz
    private PageResult Failed(string? message = null)
    {
        if (message is not null)
            ModelState.AddModelError(string.Empty, message);

        ModelState.Remove("Input.RecoveryCode");
        Input.RecoveryCode = null;
        return Page();
    }
}
