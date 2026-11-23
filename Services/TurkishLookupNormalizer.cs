using Microsoft.AspNetCore.Identity;

namespace YetenekPlatformu.Services;

// Identity varsayılan olarak İngilizce kurallarla büyük harfe çevirir ("ali" ve "alı" ikisi de "ALI" olur).
// Bu sınıf Türkçe kuralları kullanır: i -> İ, ı -> I. Böylece farklı adlar karışmaz.
public class TurkishLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name) => name?.Normalize().ToUpper(UserNameHelper.Turkish);

    public string? NormalizeEmail(string? email) => email?.Normalize().ToUpper(UserNameHelper.Turkish);
}
