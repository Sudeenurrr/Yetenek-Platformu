using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;

namespace YetenekPlatformu.Pages.Account.Profile;

// Favorilerim: favoriye eklenen kişiler (ekleme arama sonuçlarından yapılacak)
[Authorize(Roles = AppRoles.Member)]
public class FavoritesModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager) : PageModel
{
    public record FavoriteCard(string UserId, string FullName, string City, List<string> Skills);

    public List<FavoriteCard> Favorites { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var userId = userManager.GetUserId(User)!;

        // Telefon ve IBAN burada gösterilmez (görünürlük kuralları)
        Favorites = await db.Favorites
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FavoriteCard(
                f.InstructorId,
                f.Instructor.FirstName + " " + f.Instructor.LastName,
                f.Instructor.City,
                db.UserSkills
                    .Where(us => us.UserId == f.InstructorId)
                    .OrderBy(us => us.Skill.Name)
                    .Select(us => us.Skill.Name)
                    .ToList()))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostRemoveAsync(string instructorId)
    {
        var userId = userManager.GetUserId(User)!;

        var removed = await db.Favorites
            .Where(f => f.UserId == userId && f.InstructorId == instructorId)
            .ExecuteDeleteAsync();

        if (removed > 0)
            TempData["StatusMessage"] = "Favorilerinizden çıkarıldı.";
        return RedirectToPage();
    }
}
