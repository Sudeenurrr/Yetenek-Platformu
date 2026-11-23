namespace YetenekPlatformu.Settings;

// Ödeme tutarı seçenekleri: TEK tanım yeri.
// Form, sunucu kontrolü ve veritabanı kontrol kısıtı bu listeyi kullanır.
// Listeyi değiştirdikten sonra yeni bir migration oluşturup uygulayın (kontrol kısıtı güncellensin).
public static class PaymentOptions
{
    public static readonly IReadOnlyList<decimal> AllowedAmounts = [200m, 250m, 300m, 400m, 500m];

    public static bool IsAllowed(decimal amount) => AllowedAmounts.Contains(amount);
}
