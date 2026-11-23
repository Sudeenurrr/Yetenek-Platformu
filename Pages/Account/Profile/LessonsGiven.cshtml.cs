using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Account.Profile;

// Verdiğim Dersler: yalnızca tamamlanmış dersler. Her derste gelen gönüllü ödeme, üstte toplam kazancım.
[Authorize(Roles = AppRoles.Member)]
public class LessonsGivenModel(ApplicationDbContext db, PaymentService payments, UserManager<ApplicationUser> userManager) : PageModel
{
    public LessonListViewModel Lessons { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        var me = userManager.GetUserId(User)!;
        var rows = await LessonQueries.CompletedAsInstructorAsync(db, me);
        Lessons = new LessonListViewModel
        {
            Rows = rows,
            EmptyText = "Henüz tamamlanmış bir dersiniz yok.",
            AsStudent = false,
            PaymentTotal = await payments.TotalReceivedAsync(me)
        };
    }
}
