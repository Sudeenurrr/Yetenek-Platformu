using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages;

// Arama: "Yetenek Ara" (o yeteneği ekleyen üyeler, o yetenekteki ders saatine göre çoktan aza)
// ve "Kişi Ara" (ad/soyad, toplam verdiği ders saatine göre çoktan aza). Yalnızca üyeler kullanır.
// İki modda da isteğe bağlı şehir filtresi var; filtre sunucuda uygulanır.
[Authorize(Roles = AppRoles.Member)]
public class SearchModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager) : PageModel
{
    public const string SkillMode = "skill";
    public const string PersonMode = "person";
    private const int MinTermLength = 2;
    private const int MaxResults = 50;
    private const int MaxSuggestions = 10;

    // Türkçe kurala göre büyük/küçük harf farkını yok sayan karşılaştırma ("İngilizce" = "ingilizce")
    private static readonly StringComparer TurkishIgnoreCase = StringComparer.Create(UserNameHelper.Turkish, ignoreCase: true);

    public record MemberResult(string Id, string FullName, string City, int Hours, List<string> Skills, bool IsFavorite);

    // Kişi önerisi: yalnızca ad soyad ve şehir (telefon ve IBAN gönderilmez)
    public record PersonOption(string Id, string Name, string City);

    [BindProperty(SupportsGet = true)]
    public string? Mode { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SkillId { get; set; }

    // Şehir filtresi; boş ya da listede olmayan değer "Tüm şehirler" sayılır
    [BindProperty(SupportsGet = true)]
    public string? City { get; set; }

    public IReadOnlyList<string> Cities => TurkishCities.All;

    public bool IsPersonMode => Mode == PersonMode;

    // Veri seti henüz yüklenmediyse yetenek araması kapalı gösterilir
    public bool SkillCatalogEmpty { get; private set; }

    public Skill? SelectedSkill { get; private set; }

    // Listeden seçmeden aranınca açılan yetenek dışında, yazılan kelimeye uyan diğer yetenekler
    public List<SkillOption> OtherMatchingSkills { get; private set; } = [];

    // Yazılan kelimeye uyan hiçbir yetenek yok
    public bool NoSkillMatch { get; private set; }

    public List<MemberResult>? Results { get; private set; }

    public bool TermTooShort { get; private set; }

    // Kişi Ara: form ne ad ne şehirle gönderildi
    public bool NameOrCityRequired { get; private set; }

    public async Task OnGetAsync()
    {
        Mode = Mode == PersonMode ? PersonMode : SkillMode;
        Q = Q?.Trim();
        City = ValidCityOrNull(City);
        var me = userManager.GetUserId(User)!;

        if (IsPersonMode)
        {
            if (string.IsNullOrEmpty(Q))
            {
                // Ad yoksa ve şehir seçildiyse o şehirdeki bütün üyeler gelir
                if (City is not null)
                    Results = await SearchPeopleAsync(me, null);
                // Sekmeye ilk girişte (form gönderilmeden) uyarı gösterilmez
                else if (Request.Query.ContainsKey("q"))
                    NameOrCityRequired = true;
                return;
            }
            if (Q.Length < MinTermLength)
            {
                TermTooShort = true;
                return;
            }
            Results = await SearchPeopleAsync(me, Q);
            return;
        }

        SkillCatalogEmpty = !await db.Skills.AnyAsync();

        if (SkillId is int skillId)
        {
            SelectedSkill = await db.Skills.FindAsync(skillId);

            // Kutuya seçili yetenekten farklı bir şey yazılmışsa yazılan esas alınır
            // (JavaScript çalışmazsa gizli alanda eski yetenek kalabilir)
            if (SelectedSkill is not null && !string.IsNullOrEmpty(Q) && !TurkishIgnoreCase.Equals(Q, SelectedSkill.Name))
                SelectedSkill = null;

            if (SelectedSkill is not null)
            {
                Q = SelectedSkill.Name;
                Results = await SearchBySkillAsync(me, skillId);
                return;
            }
        }

        if (string.IsNullOrEmpty(Q))
            return;
        if (Q.Length < MinTermLength)
        {
            TermTooShort = true;
            return;
        }

        // Listeden seçmeden Ara: yazılana uyan yetenek doğrudan açılır.
        // Adı aynen yazılmışsa o, değilse açılır listede en üstte görünen (adı yazılanla başlayan önce).
        var matches = await MemberQueries.SearchSkillsAsync(db, Q, maxResults: MaxSuggestions);
        if (matches.Count == 0)
        {
            NoSkillMatch = true;
            return;
        }

        var chosen = matches.FirstOrDefault(s => TurkishIgnoreCase.Equals(s.Name, Q)) ?? matches[0];
        OtherMatchingSkills = matches.Where(s => s.Id != chosen.Id).ToList();
        SelectedSkill = await db.Skills.FindAsync(chosen.Id);
        Q = chosen.Name;
        Results = await SearchBySkillAsync(me, chosen.Id);
    }

    // Otomatik tamamlama: /Search?handler=Skills&term=...
    public async Task<IActionResult> OnGetSkillsAsync(string? term)
    {
        term = term?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < MinTermLength || term.Length > 100)
            return new JsonResult(Array.Empty<SkillOption>());

        return new JsonResult(await MemberQueries.SearchSkillsAsync(db, term, maxResults: MaxSuggestions));
    }

    // Kişi önerileri: /Search?handler=People&term=...&city=...
    public async Task<IActionResult> OnGetPeopleAsync(string? term, string? city)
    {
        term = term?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < MinTermLength || term.Length > 100)
            return new JsonResult(Array.Empty<PersonOption>());

        var me = userManager.GetUserId(User)!;
        var normalized = NormalizeTerm(term);
        var rows = await PeopleMatching(me, normalized, ValidCityOrNull(city))
            // Adı yazılan harflerle başlayanlar önce, sonra ada göre
            .OrderBy(u => u.NormalizedUserName!.StartsWith(normalized) ? 0 : 1)
            .ThenBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Take(MaxSuggestions)
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName, u.City })
            .ToListAsync();

        return new JsonResult(rows.Select(r => new PersonOption(r.Id, r.Name, r.City)));
    }

    private static string? ValidCityOrNull(string? city) => TurkishCities.IsValid(city) ? city : null;

    // Türkçe kurallarla büyük harf (UserName karşılaştırmasıyla aynı): "ışık" -> "IŞIK"
    private static string NormalizeTerm(string term) => UserNameHelper.CleanName(term).ToUpper(UserNameHelper.Turkish);

    // Kişi Ara ve kişi önerilerinin ortak filtresi: üyeler, kişinin kendisi hariç,
    // ad/soyad eşleşmesi (ad boşsa herkes) ve isteğe bağlı şehir
    private IQueryable<ApplicationUser> PeopleMatching(string me, string? normalizedTerm, string? city) =>
        MemberQueries.Members(db)
            .Where(u => u.Id != me)
            .Where(u => normalizedTerm == null || u.NormalizedUserName!.Contains(normalizedTerm))
            .Where(u => city == null || u.City == city);

    private async Task<List<MemberResult>> SearchBySkillAsync(string me, int skillId)
    {
        // Sıralama: bu yetenekte tamamlanmış toplam ders saati (çoktan aza), eşitse ada göre
        var rows = await MemberQueries.Members(db)
            .Where(u => u.Id != me && db.UserSkills.Any(us => us.UserId == u.Id && us.SkillId == skillId))
            .Where(u => City == null || u.City == City)
            .Select(u => new
            {
                u.Id,
                FullName = u.FirstName + " " + u.LastName,
                u.City,
                Hours = db.Appointments
                    .Where(a => a.InstructorId == u.Id && a.SkillId == skillId && a.Status == AppointmentStatus.Completed)
                    .Sum(a => (int)a.DurationHours),
                Skills = db.UserSkills.Where(us => us.UserId == u.Id).OrderBy(us => us.Skill.Name).Select(us => us.Skill.Name).ToList(),
                IsFavorite = db.Favorites.Any(f => f.UserId == me && f.InstructorId == u.Id)
            })
            .OrderByDescending(r => r.Hours)
            .ThenBy(r => r.FullName)
            .Take(MaxResults)
            .ToListAsync();

        return rows.Select(r => new MemberResult(r.Id, r.FullName, r.City, r.Hours, r.Skills, r.IsFavorite)).ToList();
    }

    // term boşsa (yalnızca şehir seçildiyse) o şehirdeki bütün üyeler
    private async Task<List<MemberResult>> SearchPeopleAsync(string me, string? term)
    {
        // Sıralama: toplam verdiği ders saati (çoktan aza), eşitse ada göre
        var rows = await PeopleMatching(me, term is null ? null : NormalizeTerm(term), City)
            .Select(u => new
            {
                u.Id,
                FullName = u.FirstName + " " + u.LastName,
                u.City,
                Hours = db.Appointments
                    .Where(a => a.InstructorId == u.Id && a.Status == AppointmentStatus.Completed)
                    .Sum(a => (int)a.DurationHours),
                Skills = db.UserSkills.Where(us => us.UserId == u.Id).OrderBy(us => us.Skill.Name).Select(us => us.Skill.Name).ToList(),
                IsFavorite = db.Favorites.Any(f => f.UserId == me && f.InstructorId == u.Id)
            })
            .OrderByDescending(r => r.Hours)
            .ThenBy(r => r.FullName)
            .Take(MaxResults)
            .ToListAsync();

        return rows.Select(r => new MemberResult(r.Id, r.FullName, r.City, r.Hours, r.Skills, r.IsFavorite)).ToList();
    }
}
