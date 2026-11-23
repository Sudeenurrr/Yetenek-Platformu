using System.Globalization;
using System.Text.RegularExpressions;

namespace YetenekPlatformu.Services;

// Ad + soyaddan kullanıcı adı üretir. Kayıt ve giriş aynı metodu kullanır.
public static partial class UserNameHelper
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // Baştaki/sondaki boşlukları siler, aradaki çoklu boşlukları teke indirir
    public static string CleanName(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // "  AYŞE  ", "Yılmaz" -> "ayşe yılmaz" (Türkçe kurallarla küçük harf: I -> ı, İ -> i)
    public static string BuildUserName(string? firstName, string? lastName) =>
        $"{CleanName(firstName)} {CleanName(lastName)}".ToLower(Turkish);

    // Yalnızca harf ve kelimeler arasında tek boşluk
    public static bool IsValidName(string cleanedName) => NamePattern().IsMatch(cleanedName);

    [GeneratedRegex(@"^\p{L}+( \p{L}+)*$")]
    private static partial Regex NamePattern();
}
