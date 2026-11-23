using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Account.Profile;

// Kişisel Bilgilerim: telefon, IBAN, şehir güncellenir; ad ve soyad yalnızca görünür.
// Şifre değiştirme ve kurtarma kodunu yenileme de burada.
[Authorize]
public class IndexModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    RecoveryCodeService recoveryCodes) : PageModel
{
    // Bölümlerin sayfadaki işaretleri (Index.cshtml'deki id'lerle aynı)
    private const string PasswordSectionId = "sifre-degistir";
    private const string RecoverySectionId = "kurtarma-kodu";

    private const string LockedError = "Çok sayıda hatalı deneme yapıldı. Hesabınız 2 dakika kilitlendi, lütfen sonra tekrar deneyin.";

    public ApplicationUser CurrentUser { get; private set; } = null!;

    public InfoInputModel Info { get; set; } = new();

    public PasswordInputModel PasswordInput { get; set; } = new();

    public RecoveryInputModel RecoveryInput { get; set; } = new();

    public bool HasRecoveryCode => CurrentUser.RecoveryCodeHash is not null;

    public IReadOnlyList<string> Cities => TurkishCities.All;

    // Admin üyeliğini iptal edemez
    public bool CanCancelMembership => !User.IsInRole(AppRoles.Admin);

    public class InfoInputModel
    {
        [Required(ErrorMessage = "Telefon zorunludur.")]
        public string? PhoneNumber { get; set; }

        [Required(ErrorMessage = "IBAN zorunludur.")]
        public string? Iban { get; set; }

        [Required(ErrorMessage = "Şehir seçmelisiniz.")]
        public string? City { get; set; }
    }

    public class PasswordInputModel
    {
        [Required(ErrorMessage = "Mevcut şifre zorunludur.")]
        [DataType(DataType.Password)]
        public string? CurrentPassword { get; set; }

        [Required(ErrorMessage = "Yeni şifre zorunludur.")]
        [DataType(DataType.Password)]
        public string? NewPassword { get; set; }
    }

    public class RecoveryInputModel
    {
        [Required(ErrorMessage = "Mevcut şifre zorunludur.")]
        [DataType(DataType.Password)]
        public string? CurrentPassword { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadUserAsync())
            return RedirectToPage("/Account/Login");

        // Yeni kurtarma kodu gösterilecekse tarayıcı bu sayfayı saklamasın (geri tuşuyla kod yeniden görünmesin)
        if (TempData.Peek("NewRecoveryCode") is not null)
            Response.Headers.CacheControl = "no-store";

        Info = InfoFromCurrentUser();
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateInfoAsync(InfoInputModel info)
    {
        if (!await LoadUserAsync())
            return RedirectToPage("/Account/Login");

        Info = info;
        // Telefon: "+90 5"ten sonraki 9 rakam; IBAN: "TR"den sonraki 7 rakam
        var phoneDigits = MemberValidation.PhoneLocalDigits(info.PhoneNumber);
        var ibanDigits = MemberValidation.IbanDigits(info.Iban);

        // Kayıttaki kontrollerin aynısı
        if (!string.IsNullOrEmpty(info.PhoneNumber) && !MemberValidation.IsValidPhoneLocal(phoneDigits))
            ModelState.AddModelError("Info.PhoneNumber", MemberValidation.PhoneError);

        if (!string.IsNullOrEmpty(info.Iban) && !MemberValidation.IsValidIbanDigits(ibanDigits))
            ModelState.AddModelError("Info.Iban", MemberValidation.IbanError);

        if (!string.IsNullOrEmpty(info.City) && !TurkishCities.IsValid(info.City))
            ModelState.AddModelError("Info.City", MemberValidation.CityError);

        if (!ModelState.IsValid)
            return Page();

        var storedPhone = MemberValidation.ToStoredPhone(phoneDigits);
        var storedIban = MemberValidation.ToStoredIban(ibanDigits);

        // Telefon ve IBAN başka bir üyede olmamalı (kişinin kendi kaydı sayılmaz)
        if (AddTakenErrors(await MemberUniqueness.CheckAsync(userManager.Users, storedPhone, storedIban, CurrentUser.Id)))
            return Page();

        // Aynı kullanıcı satırı güncellenir (yeni satır oluşmaz)
        CurrentUser.PhoneNumber = storedPhone;
        CurrentUser.Iban = storedIban;
        CurrentUser.City = info.City!;

        IdentityResult result;
        try
        {
            result = await userManager.UpdateAsync(CurrentUser);
        }
        catch (DbUpdateException ex) when (MemberUniqueness.FromDuplicateKey(ex) is { } taken)
        {
            // Kontrolden sonra başka biri aynı değeri aldıysa veritabanı reddeder
            AddTakenErrors(taken);
            return Page();
        }

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        TempData["StatusMessage"] = "Bilgileriniz güncellendi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(PasswordInputModel passwordInput)
    {
        if (!await LoadUserAsync())
            return RedirectToPage("/Account/Login");

        // Üstteki bilgi formu kayıtlı değerlerle dolu kalsın
        Info = InfoFromCurrentUser();

        if (!ModelState.IsValid)
            return Page();

        // Mevcut şifre önce hesap kilidiyle kontrol edilir: yanlış denemeler girişteki gibi sayılır,
        // 3 yanlış denemede hesap 2 dakika kilitlenir (Program.cs). Kilitliyken doğru şifre de kabul edilmez.
        var check = await signInManager.CheckPasswordSignInAsync(CurrentUser, passwordInput.CurrentPassword!, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            ModelState.AddModelError("PasswordInput.CurrentPassword", LockedError);
            return Page();
        }
        if (!check.Succeeded)
        {
            ModelState.AddModelError("PasswordInput.CurrentPassword", "Mevcut şifre hatalı.");
            return Page();
        }

        // Mevcut şifre doğrulandıktan sonra: yeni şifre onunla aynı olamaz (büyük/küçük harfe duyarlı)
        if (string.Equals(passwordInput.CurrentPassword, passwordInput.NewPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError("PasswordInput.NewPassword", "Yeni şifre mevcut şifreyle aynı olamaz.");
            return Page();
        }

        var result = await userManager.ChangePasswordAsync(CurrentUser, passwordInput.CurrentPassword!, passwordInput.NewPassword!);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                if (error.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                    ModelState.AddModelError("PasswordInput.CurrentPassword", "Mevcut şifre hatalı.");
                else
                    ModelState.AddModelError("PasswordInput.NewPassword", error.Description);
            }
            return Page();
        }

        // Şifre değişince oturum bilgisi yenilenir, kullanıcı çıkış yapmış olmaz
        await signInManager.RefreshSignInAsync(CurrentUser);
        // Mesaj şifre bölümünde gösterilir, sayfa doğrudan o bölümden açılır
        TempData["PasswordStatusMessage"] = "Şifreniz değiştirildi.";
        return RedirectToPage(pageName: null, pageHandler: null, routeValues: null, fragment: PasswordSectionId);
    }

    // Kurtarma kodunu yenile: mevcut şifre (hesap kilidiyle) doğrulanır, yeni kod üretilip bir kez gösterilir.
    // Kodu olmayan hesaplar da kodu buradan üretir. Eski kod geçersiz olur.
    public async Task<IActionResult> OnPostRegenerateRecoveryCodeAsync(RecoveryInputModel recoveryInput)
    {
        if (!await LoadUserAsync())
            return RedirectToPage("/Account/Login");

        Info = InfoFromCurrentUser();

        if (!ModelState.IsValid)
            return Page();

        var check = await signInManager.CheckPasswordSignInAsync(CurrentUser, recoveryInput.CurrentPassword!, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            ModelState.AddModelError("RecoveryInput.CurrentPassword", LockedError);
            return Page();
        }
        if (!check.Succeeded)
        {
            ModelState.AddModelError("RecoveryInput.CurrentPassword", "Mevcut şifre hatalı.");
            return Page();
        }

        var code = await recoveryCodes.CreateAndSaveAsync(CurrentUser);
        if (code is null)
        {
            ModelState.AddModelError("RecoveryInput.CurrentPassword", "Kurtarma kodu oluşturulamadı. Lütfen tekrar deneyin.");
            return Page();
        }

        // Kod yalnızca bir kez, kurtarma kodu bölümünde gösterilir
        TempData["NewRecoveryCode"] = code;
        return RedirectToPage(pageName: null, pageHandler: null, routeValues: null, fragment: RecoverySectionId);
    }

    // Formda "+90 5" ve "TR" sabit kutularda durur; kutulara yalnızca kalan kısım gelir
    private InfoInputModel InfoFromCurrentUser() => new()
    {
        PhoneNumber = MemberValidation.PhoneLocalFormatted(CurrentUser.PhoneNumber),
        Iban = MemberValidation.IbanDigits(CurrentUser.Iban),
        City = CurrentUser.City
    };

    private bool AddTakenErrors(MemberUniqueness.Result taken)
    {
        if (taken.PhoneTaken)
            ModelState.AddModelError("Info.PhoneNumber", MemberUniqueness.PhoneTakenError);
        if (taken.IbanTaken)
            ModelState.AddModelError("Info.Iban", MemberUniqueness.IbanTakenError);
        return taken.PhoneTaken || taken.IbanTaken;
    }

    private async Task<bool> LoadUserAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return false;
        CurrentUser = user;
        return true;
    }
}
