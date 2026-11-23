using Microsoft.AspNetCore.Identity;

namespace YetenekPlatformu.Data;

public static class AppRoles
{
    public const string Member = "Uye";
    public const string Admin = "Admin";

    // Uygulama açılırken eksik rolleri oluşturur
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Mevcut rollerin karşılaştırma adını güncel kurala (Türkçe büyük harf) göre yenile
        foreach (var existing in roleManager.Roles.ToList())
        {
            if (existing.NormalizedName != roleManager.NormalizeKey(existing.Name))
            {
                // UpdateAsync karşılaştırma adını yeniden hesaplayıp kaydeder
                await roleManager.UpdateAsync(existing);
            }
        }

        foreach (var role in new[] { Member, Admin })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }
}
