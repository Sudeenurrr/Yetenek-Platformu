using System.Globalization;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Models;

namespace YetenekPlatformu.Data;

public static class SkillSeeder
{
    // Veri seti: Data/Seed/skills.csv (tek sütun "Ad", UTF-8). Kaynak: Wikidata (CC0)
    private static readonly string[] SeedFilePath = ["Data", "Seed", "skills.csv"];

    // Uygulama açılırken dosyadaki yetenekleri Skills tablosuna ekler.
    // Yalnızca tabloda olmayan adlar eklenir; tekrar çalıştığında aynı yetenek ikinci kez eklenmez.
    // Dosyadan çıkarılan bir ad tablodan silinmez (o yeteneği eklemiş üyeler olabilir).
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Skill>>();

        var path = Path.Combine([env.ContentRootPath, .. SeedFilePath]);
        if (!File.Exists(path))
        {
            logger.LogWarning("Yetenek veri seti bulunamadı: {Path}", path);
            return;
        }

        // Türkçe kurala göre büyük/küçük harf farkını yok say (veritabanındaki Turkish_CI_AS ile aynı)
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

        var names = (await File.ReadAllLinesAsync(path))
            .Skip(1) // başlık satırı: "Ad"
            .Select(line => line.Trim().Trim('"').Trim())
            .Where(name => name.Length > 0)
            .Distinct(comparer)
            .ToList();

        var existing = new HashSet<string>(await db.Skills.Select(s => s.Name).ToListAsync(), comparer);
        var toAdd = names.Where(name => !existing.Contains(name)).ToList();
        if (toAdd.Count == 0)
            return;

        db.Skills.AddRange(toAdd.Select(name => new Skill { Name = name }));
        await db.SaveChangesAsync();
        logger.LogInformation("{Count} yetenek veri setinden eklendi.", toAdd.Count);
    }
}
