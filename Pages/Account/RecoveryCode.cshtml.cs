using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Account;

// Kurtarma kodunu bir kez gösterir (kayıttan ve "Şifremi unuttum"dan sonra).
// Kod buraya TempData (şifrelenmiş, tek kullanımlık çerez) ile gelir; sayfa yenilenince bir daha görünmez.
public class RecoveryCodeModel : PageModel
{
    public string? Code { get; private set; }

    // "register": kayıttan sonra (kişi giriş yapmış), "reset": şifre sıfırlandıktan sonra (girişe gider)
    public string? Context { get; private set; }

    public void OnGet()
    {
        // Tarayıcı bu sayfayı saklamasın: geri tuşuyla kod yeniden görünmesin
        Response.Headers.CacheControl = "no-store";

        Code = TempData[RecoveryCodeService.TempDataCodeKey] as string;
        Context = TempData[RecoveryCodeService.TempDataContextKey] as string;
    }
}
