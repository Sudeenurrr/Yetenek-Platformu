using System.Text.RegularExpressions;

namespace YetenekPlatformu.Services;

// Kayıt ve profil formlarının ortak kontrolleri (tek yerde)
//
// Telefon: formda "+90 5" sabittir, kullanıcı kalan 9 rakamı yazar ("55 555 55 55").
//          Veritabanında "05XXXXXXXXX" (11 rakam) olarak saklanır, ekranda "+90 555 555 55 55" gösterilir.
// IBAN:    formda "TR" sabittir, kullanıcı 7 rakam yazar. Veritabanında "TR1234567" olarak saklanır.
public static partial class MemberValidation
{
    public const string IbanCountryCode = "TR";
    private const string PhonePrefix = "05";

    public const string NameError = "2-50 karakter olmalı, yalnızca harf ve boşluk içermelidir.";
    public const string PhoneError = "Telefon numarasının +90 5'ten sonraki 9 rakamını girin.";
    public const string IbanError = "IBAN için TR'den sonraki 7 rakamı girin.";
    public const string CityError = "Lütfen listeden geçerli bir şehir seçin.";
    public const string PasswordLengthError = "Şifre en az 6 karakter olmalıdır.";

    // Formdan gelen telefonu "+90 5"ten sonraki 9 rakama indirger.
    // Numaranın tamamı yazılmış/yapıştırılmışsa ("05551234567", "+905551234567") baştaki fazlalık atılır.
    public static string PhoneLocalDigits(string? value)
    {
        var digits = NonDigits().Replace(value ?? string.Empty, string.Empty);
        if (digits.Length == 12 && digits.StartsWith("905"))
            return digits[3..];
        if (digits.Length == 11 && digits.StartsWith("05"))
            return digits[2..];
        if (digits.Length == 10 && digits.StartsWith('5'))
            return digits[1..];
        return digits;
    }

    public static bool IsValidPhoneLocal(string localDigits) => NineDigits().IsMatch(localDigits);

    // 9 rakam -> veritabanı biçimi "05XXXXXXXXX"
    public static string ToStoredPhone(string localDigits) => PhonePrefix + localDigits;

    // Kayıtlı telefon -> formdaki hali "55 555 55 55"
    public static string PhoneLocalFormatted(string? storedPhone)
    {
        var d = PhoneLocalDigits(storedPhone);
        return d.Length == 9 ? $"{d[..2]} {d[2..5]} {d[5..7]} {d[7..]}" : d;
    }

    // Kayıtlı telefon -> ekranda gösterim "+90 555 555 55 55"
    public static string FormatPhone(string? storedPhone) => "+90 5" + PhoneLocalFormatted(storedPhone);

    // Formdan gelen IBAN'ı 7 rakama indirger; "TR1234567" yapıştırılmışsa baştaki TR atılır
    public static string IbanDigits(string? value)
    {
        var v = (value ?? string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
        return v.StartsWith(IbanCountryCode) ? v[IbanCountryCode.Length..] : v;
    }

    public static bool IsValidIbanDigits(string digits) => SevenDigits().IsMatch(digits);

    // 7 rakam -> veritabanı ve ödeme ekranlarındaki hali "TR1234567"
    public static string ToStoredIban(string digits) => IbanCountryCode + digits;

    public static bool IsValidNamePart(string cleanedName) =>
        cleanedName.Length is >= 2 and <= 50 && UserNameHelper.IsValidName(cleanedName);

    // [0-9]: yalnızca 0-9 rakamları (\d başka alfabelerdeki rakamları da kabul ederdi)
    [GeneratedRegex(@"[^0-9]")]
    private static partial Regex NonDigits();

    [GeneratedRegex(@"^[0-9]{9}$")]
    private static partial Regex NineDigits();

    [GeneratedRegex(@"^[0-9]{7}$")]
    private static partial Regex SevenDigits();
}
