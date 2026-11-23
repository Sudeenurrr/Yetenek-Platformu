using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using YetenekPlatformu.Models;

namespace YetenekPlatformu.Services;

// Şifremi unuttum için kurtarma kodu: üretir, karıştırılmış (hash) halini kullanıcıya yazar, girilen kodu kontrol eder.
// Kodun kendisi veritabanına yazılmaz; kişiye yalnızca bir kez gösterilir.
public class RecoveryCodeService(UserManager<ApplicationUser> userManager, IPasswordHasher<ApplicationUser> hasher)
{
    // Birbiriyle karışabilen 0/O, 1/I/L yok: 31 karakter, 12 karakterlik kodda yaklaşık 10^18 olasılık
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 12;
    private const int GroupSize = 4;

    // Kodu bir sonraki sayfaya (Account/RecoveryCode) taşıyan TempData anahtarları
    public const string TempDataCodeKey = "RecoveryCode";
    public const string TempDataContextKey = "RecoveryCodeContext";
    public const string ContextRegister = "register";
    public const string ContextReset = "reset";

    // Hesap bulunamadığında da aynı süreli karşılaştırma yapılsın diye kullanılan sahte hash
    private static readonly string DummyHash = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), "dummy-recovery-code");

    // Kriptografik olarak güvenli rastgele kod, gösterim biçimi: "K7MP-X3QR-9HTW"
    public static string Generate()
    {
        var chars = RandomNumberGenerator.GetItems<char>(Alphabet, CodeLength);
        return string.Join('-', Enumerable.Range(0, CodeLength / GroupSize)
            .Select(i => new string(chars, i * GroupSize, GroupSize)));
    }

    // Girilen kod: tire ve boşluklar atılır, büyük harfe çevrilir ("k7mp x3qr-9htw" -> "K7MPX3QR9HTW")
    public static string Normalize(string? input) =>
        new string((input ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    // Yeni kod üretir ve hash'ini kullanıcı nesnesine yazar (kaydetmez). Eski kod geçersiz olur.
    public string AssignNewCode(ApplicationUser user)
    {
        var code = Generate();
        user.RecoveryCodeHash = hasher.HashPassword(user, Normalize(code));
        return code;
    }

    // Yeni kod üretir ve kaydeder; kaydedilemezse null döner
    public async Task<string?> CreateAndSaveAsync(ApplicationUser user)
    {
        var code = AssignNewCode(user);
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded ? code : null;
    }

    // Girilen kod kullanıcının kayıtlı koduyla aynı mı? Kullanıcı ya da kodu yoksa da aynı işlem yapılır,
    // böylece cevap süresinden hesabın ya da kodun var olup olmadığı anlaşılmaz.
    public bool Verify(ApplicationUser? user, string? input)
    {
        var normalized = Normalize(input);
        if (user?.RecoveryCodeHash is null)
        {
            hasher.VerifyHashedPassword(new ApplicationUser(), DummyHash, normalized);
            return false;
        }

        return normalized.Length == CodeLength
            && hasher.VerifyHashedPassword(user, user.RecoveryCodeHash, normalized) != PasswordVerificationResult.Failed;
    }
}
