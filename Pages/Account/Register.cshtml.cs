using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Account;

public class RegisterModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    RecoveryCodeService recoveryCodes) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<string> Cities => TurkishCities.All;

    public class InputModel
    {
        [Required(ErrorMessage = "Ad zorunludur.")]
        public string? FirstName { get; set; }

        [Required(ErrorMessage = "Soyad zorunludur.")]
        public string? LastName { get; set; }

        [Required(ErrorMessage = "Şifre zorunludur.")]
        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [Required(ErrorMessage = "IBAN zorunludur.")]
        public string? Iban { get; set; }

        [Required(ErrorMessage = "Telefon zorunludur.")]
        public string? PhoneNumber { get; set; }

        [Required(ErrorMessage = "Şehir seçmelisiniz.")]
        public string? City { get; set; }
    }

    public IActionResult OnGet()
    {
        if (signInManager.IsSignedIn(User))
            return RedirectToPage("/Index");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (signInManager.IsSignedIn(User))
            return RedirectToPage("/Index");

        // Değerleri temizle (boşluklar, telefon ayraçları)
        var firstName = UserNameHelper.CleanName(Input.FirstName);
        var lastName = UserNameHelper.CleanName(Input.LastName);
        // Telefon: "+90 5"ten sonraki 9 rakam; IBAN: "TR"den sonraki 7 rakam
        var phoneDigits = MemberValidation.PhoneLocalDigits(Input.PhoneNumber);
        var ibanDigits = MemberValidation.IbanDigits(Input.Iban);

        // Sunucu tarafı doğrulamalar (zorunlu alan kontrolü [Required] ile yapıldı)
        if (!string.IsNullOrEmpty(Input.FirstName) && !MemberValidation.IsValidNamePart(firstName))
            ModelState.AddModelError("Input.FirstName", "Ad " + MemberValidation.NameError);

        if (!string.IsNullOrEmpty(Input.LastName) && !MemberValidation.IsValidNamePart(lastName))
            ModelState.AddModelError("Input.LastName", "Soyad " + MemberValidation.NameError);

        if (!string.IsNullOrEmpty(Input.Password) && Input.Password.Length < 6)
            ModelState.AddModelError("Input.Password", MemberValidation.PasswordLengthError);

        if (!string.IsNullOrEmpty(Input.Iban) && !MemberValidation.IsValidIbanDigits(ibanDigits))
            ModelState.AddModelError("Input.Iban", MemberValidation.IbanError);

        if (!string.IsNullOrEmpty(Input.PhoneNumber) && !MemberValidation.IsValidPhoneLocal(phoneDigits))
            ModelState.AddModelError("Input.PhoneNumber", MemberValidation.PhoneError);

        if (!string.IsNullOrEmpty(Input.City) && !TurkishCities.IsValid(Input.City))
            ModelState.AddModelError("Input.City", MemberValidation.CityError);

        if (!ModelState.IsValid)
            return Page();

        // Ad + soyad benzersiz olmalı
        var userName = UserNameHelper.BuildUserName(firstName, lastName);
        if (await userManager.FindByNameAsync(userName) is not null)
        {
            ModelState.AddModelError(string.Empty, "Bu ad ve soyadla kayıtlı bir üye zaten var. Giriş sayfasını deneyebilirsiniz.");
            return Page();
        }

        var user = new ApplicationUser
        {
            UserName = userName,
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = MemberValidation.ToStoredPhone(phoneDigits),
            Iban = MemberValidation.ToStoredIban(ibanDigits),
            City = Input.City!
        };

        // Telefon ve IBAN başka bir üyede olmamalı
        if (AddTakenErrors(await MemberUniqueness.CheckAsync(userManager.Users, user.PhoneNumber, user.Iban)))
            return Page();

        // Kurtarma kodu kullanıcıyla birlikte kaydedilir (yalnızca hash'i); kod kayıttan sonra bir kez gösterilir
        var recoveryCode = recoveryCodes.AssignNewCode(user);

        IdentityResult result;
        try
        {
            result = await userManager.CreateAsync(user, Input.Password!);
        }
        catch (DbUpdateException ex) when (MemberUniqueness.FromDuplicateKey(ex) is { } taken)
        {
            // Kontrolden sonra başka biri aynı değerle kayıt olduysa veritabanı reddeder
            AddTakenErrors(taken);
            return Page();
        }

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        // Yeni üye Uye rolünü alır; olmazsa yarım kayıt bırakmamak için kullanıcı geri silinir
        var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Member);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            ModelState.AddModelError(string.Empty, "Kayıt tamamlanamadı. Lütfen tekrar deneyin.");
            return Page();
        }

        await signInManager.SignInAsync(user, isPersistent: false);

        TempData[RecoveryCodeService.TempDataCodeKey] = recoveryCode;
        TempData[RecoveryCodeService.TempDataContextKey] = RecoveryCodeService.ContextRegister;
        return RedirectToPage("/Account/RecoveryCode");
    }

    private bool AddTakenErrors(MemberUniqueness.Result taken)
    {
        if (taken.PhoneTaken)
            ModelState.AddModelError("Input.PhoneNumber", MemberUniqueness.PhoneTakenError);
        if (taken.IbanTaken)
            ModelState.AddModelError("Input.Iban", MemberUniqueness.IbanTakenError);
        return taken.PhoneTaken || taken.IbanTaken;
    }
}
