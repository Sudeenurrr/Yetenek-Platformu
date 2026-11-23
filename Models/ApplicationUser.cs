using Microsoft.AspNetCore.Identity;

namespace YetenekPlatformu.Models;

// Identity kullanıcısı; kayıt formundaki ek alanlarla genişletildi.
// UserName = ad + soyad (küçük harf), giriş bu alanla yapılır. E-posta kullanılmaz.
public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    // Gerçek olmayan IBAN: tam olarak 7 rakam
    public string Iban { get; set; } = string.Empty;

    // 81 il listesinden seçilen şehir
    public string City { get; set; } = string.Empty;

    // Şifremi unuttum: kurtarma kodunun PasswordHasher ile karıştırılmış hali (kodun kendisi saklanmaz).
    // Boşsa kişinin kodu yok (profilden üretebilir).
    public string? RecoveryCodeHash { get; set; }
}
