using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Members;

// Favori ekle/çıkar (arama sonuçları ve üye profilindeki ♥ butonu). Sonra geldiği sayfaya döner.
[Authorize(Roles = AppRoles.Member)]
public class FavoriteModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Search");

    public async Task<IActionResult> OnPostAsync(string? memberId, string? returnUrl)
    {
        var me = userManager.GetUserId(User)!;

        if (string.IsNullOrEmpty(memberId) || memberId == me)
        {
            TempData["StatusMessage"] = "Kendinizi favorilere ekleyemezsiniz.";
        }
        else if (!await MemberQueries.Members(db).AnyAsync(u => u.Id == memberId))
        {
            TempData["StatusMessage"] = "Üye bulunamadı.";
        }
        else
        {
            var existing = await db.Favorites.FindAsync(me, memberId);
            if (existing is not null)
            {
                db.Favorites.Remove(existing);
                TempData["StatusMessage"] = "Favorilerinizden çıkarıldı.";
            }
            else
            {
                db.Favorites.Add(new Favorite { UserId = me, InstructorId = memberId, CreatedAt = DateTime.Now });
                TempData["StatusMessage"] = "Favorilerinize eklendi.";
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Aynı anda iki kez basılırsa birincil anahtar ikinciyi engeller; sonuç zaten istenen durumda
            }
        }

        // Yalnızca site içi adrese dön
        return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/Search");
    }
}
