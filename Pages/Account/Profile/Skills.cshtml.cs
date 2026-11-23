using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Account.Profile;

// Yeteneklerim: arama kutusuyla (otomatik tamamlama) ekle, × ile kaldır.
// Skills tablosu harici veri setinden doldurulunca ek değişiklik gerekmeden çalışır.
[Authorize(Roles = AppRoles.Member)]
public class SkillsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager) : PageModel
{
    private const int MinTermLength = 2;
    private const int MaxResults = 10;

    public List<Skill> MySkills { get; private set; } = [];

    // Veri seti henüz yüklenmediyse arama kutusu kapalı gösterilir
    public bool SkillCatalogEmpty { get; private set; }

    public async Task OnGetAsync()
    {
        var userId = userManager.GetUserId(User)!;

        MySkills = await db.UserSkills
            .Where(us => us.UserId == userId)
            .Select(us => us.Skill)
            .OrderBy(s => s.Name)
            .ToListAsync();

        SkillCatalogEmpty = !await db.Skills.AnyAsync();
    }

    // Otomatik tamamlama: /Account/Profile/Skills?handler=Search&term=...
    public async Task<IActionResult> OnGetSearchAsync(string? term)
    {
        term = term?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < MinTermLength || term.Length > 100)
            return new JsonResult(Array.Empty<object>());

        // Kişinin zaten eklediği yetenekler gösterilmez
        var results = await MemberQueries.SearchSkillsAsync(db, term, MaxResults, excludeUserId: userManager.GetUserId(User)!);
        return new JsonResult(results);
    }

    public async Task<IActionResult> OnPostAddAsync(int skillId)
    {
        var userId = userManager.GetUserId(User)!;

        var skill = await db.Skills.FindAsync(skillId);
        if (skill is null)
        {
            TempData["StatusMessage"] = "Lütfen listeden bir yetenek seçin.";
            return RedirectToPage();
        }

        var alreadyAdded = await db.UserSkills.AnyAsync(us => us.UserId == userId && us.SkillId == skillId);
        if (alreadyAdded)
        {
            TempData["StatusMessage"] = $"\"{skill.Name}\" zaten yeteneklerinizde.";
            return RedirectToPage();
        }

        db.UserSkills.Add(new UserSkill { UserId = userId, SkillId = skillId, CreatedAt = DateTime.Now });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Aynı anda iki kez eklenirse birincil anahtar ikinciyi engeller
            TempData["StatusMessage"] = $"\"{skill.Name}\" zaten yeteneklerinizde.";
            return RedirectToPage();
        }

        TempData["StatusMessage"] = $"\"{skill.Name}\" yeteneklerinize eklendi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int skillId)
    {
        var userId = userManager.GetUserId(User)!;

        var removed = await db.UserSkills
            .Where(us => us.UserId == userId && us.SkillId == skillId)
            .ExecuteDeleteAsync();

        if (removed > 0)
            TempData["StatusMessage"] = "Yetenek kaldırıldı.";
        return RedirectToPage();
    }
}
