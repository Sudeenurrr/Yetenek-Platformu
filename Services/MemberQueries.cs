using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;

namespace YetenekPlatformu.Services;

// Otomatik tamamlama listesinde bir yetenek (JSON: { id, name })
public record SkillOption(int Id, string Name);

// Arama ve profil sayfalarının ortak sorguları
public static class MemberQueries
{
    // Yalnızca Uye rolündeki kullanıcılar (admin aramalarda ve profillerde görünmez)
    public static IQueryable<ApplicationUser> Members(ApplicationDbContext db) =>
        db.Users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Member)));

    // Adı aranan kelimeyi içeren yetenekler; adı kelimeyle başlayanlar önce.
    // excludeUserId verilirse o kişinin zaten eklediği yetenekler çıkarılır (Yeteneklerim).
    public static Task<List<SkillOption>> SearchSkillsAsync(ApplicationDbContext db, string term, int maxResults, string? excludeUserId = null)
    {
        var query = db.Skills.Where(s => s.Name.Contains(term));

        if (excludeUserId is not null)
            query = query.Where(s => !db.UserSkills.Any(us => us.UserId == excludeUserId && us.SkillId == s.Id));

        return query
            .OrderBy(s => s.Name.StartsWith(term) ? 0 : 1)
            .ThenBy(s => s.Name)
            .Take(maxResults)
            .Select(s => new SkillOption(s.Id, s.Name))
            .ToListAsync();
    }
}
