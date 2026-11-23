using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Account.Profile;

// Aldığım Dersler: yalnızca tamamlanmış dersler. Ödenmemiş derste "Ödeme Yap" (Pay sayfası), üstte toplam ödemem.
[Authorize(Roles = AppRoles.Member)]
public class LessonsTakenModel(ApplicationDbContext db, PaymentService payments, UserManager<ApplicationUser> userManager) : PageModel
{
    public LessonListViewModel Lessons { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        var me = userManager.GetUserId(User)!;
        var rows = await LessonQueries.CompletedAsStudentAsync(db, me);
        Lessons = new LessonListViewModel
        {
            Rows = rows,
            EmptyText = "Henüz tamamlanmış bir dersiniz yok.",
            AsStudent = true,
            PaymentTotal = await payments.TotalSentAsync(me)
        };
    }
}
