using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Members;

// Başka bir üyenin profili: ad soyad, şehir, yetenekleri ve ders saatleri.
// Telefon ve IBAN burada gösterilmez (görünürlük kuralları).
[Authorize(Roles = AppRoles.Member)]
public class ProfileModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager) : PageModel
{
    public record SkillHours(string Name, int Hours);

    [BindProperty(SupportsGet = true)]
    public string? Id { get; set; }

    public bool Found { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public List<SkillHours> Skills { get; private set; } = [];
    public int TotalHours { get; private set; }
    public bool IsFavorite { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var me = userManager.GetUserId(User)!;

        // Kendi profiline bakan kişi kendi profil sayfasına gider
        if (Id == me)
            return RedirectToPage("/Account/Profile/Index");

        var member = await MemberQueries.Members(db)
            .Where(u => u.Id == Id)
            .Select(u => new { u.FirstName, u.LastName, u.City })
            .FirstOrDefaultAsync();

        if (member is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        Found = true;
        FullName = $"{member.FirstName} {member.LastName}";
        City = member.City;

        // Her yetenekte tamamlanmış ders saati; çoktan aza
        var skills = await db.UserSkills
            .Where(us => us.UserId == Id)
            .Select(us => new
            {
                us.Skill.Name,
                Hours = db.Appointments
                    .Where(a => a.InstructorId == Id && a.SkillId == us.SkillId && a.Status == AppointmentStatus.Completed)
                    .Sum(a => (int)a.DurationHours)
            })
            .OrderByDescending(s => s.Hours)
            .ThenBy(s => s.Name)
            .ToListAsync();
        Skills = skills.Select(s => new SkillHours(s.Name, s.Hours)).ToList();

        TotalHours = await db.Appointments
            .Where(a => a.InstructorId == Id && a.Status == AppointmentStatus.Completed)
            .SumAsync(a => (int)a.DurationHours);

        IsFavorite = await db.Favorites.AnyAsync(f => f.UserId == me && f.InstructorId == Id);
        return Page();
    }
}
