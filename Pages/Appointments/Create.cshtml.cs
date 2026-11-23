using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Appointments;

// Randevu Al: yetenek, gün, saat (1 ya da art arda 2 saat) ve görüşme türü seçilip talep gönderilir.
// Yüz Yüze seçilirse buluşma yeri aranıp seçilir ya da elle yazılır (wwwroot/js/place-search.js).
// Kuralların hepsi AppointmentBookingService'te, sunucuda kontrol edilir.
[Authorize(Roles = AppRoles.Member)]
public class CreateModel(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    AppointmentBookingService booking,
    PlaceSearchService places) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? InstructorId { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "Lütfen bir yetenek seçin.")]
        public int? SkillId { get; set; }

        [Required(ErrorMessage = "Lütfen bir gün seçin.")]
        public DateOnly? Date { get; set; }

        [Required(ErrorMessage = "Lütfen bir saat seçin.")]
        public int? StartHour { get; set; }

        public int DurationHours { get; set; } = 1;

        // Sayı olarak alınır; geçerli bir tür olup olmadığını sunucu Türkçe mesajla kontrol eder
        public int MeetingType { get; set; } = (int)Models.MeetingType.Online;

        // true: seçilen saat dolu, talep yerine bekleme listesine girilir
        public bool JoinWaitlist { get; set; }

        // Yüz Yüze buluşma yeri. Koordinatlar metin olarak alınır ve noktalı ondalıkla okunur
        // (Türkçe ayarda ondalık ayırıcı virgül olduğu için sayı olarak bağlanmaz). Elle yazılan yerde koordinat boştur.
        public string? PlaceName { get; set; }
        public string? PlaceLatitude { get; set; }
        public string? PlaceLongitude { get; set; }
    }

    public bool Found { get; private set; }
    public string InstructorName { get; private set; } = string.Empty;
    public string InstructorCity { get; private set; } = string.Empty;
    public bool CanFaceToFace { get; private set; }
    public List<SkillOption> Skills { get; private set; } = [];

    public DateOnly MinDate => booking.Today;
    public DateOnly MaxDate => booking.LastBookableDay;
    public int MaxDaysAhead => booking.MaxDaysAhead;
    public int DayStartHour => booking.DayStartHour;
    public int DayEndHour => booking.DayEndHour;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
            return NotFoundPage();
        return Page();
    }

    // Takvimin gün aralığı (Türkiye saatine göre bugün ve son gün). Takvim açık kalıp gece yarısı geçerse
    // JavaScript bunu yeniden ister: /Appointments/Create?handler=Range
    public IActionResult OnGetRange() => new JsonResult(new
    {
        today = booking.Today.ToString("yyyy-MM-dd"),
        max = booking.LastBookableDay.ToString("yyyy-MM-dd")
    });

    // Seçilen günün saatleri: /Appointments/Create?handler=Slots&instructorId=...&date=yyyy-MM-dd
    public async Task<IActionResult> OnGetSlotsAsync(DateOnly? date)
    {
        var me = userManager.GetUserId(User)!;
        if (date is not DateOnly day || day < booking.Today || day > booking.LastBookableDay
            || await booking.FindInstructorAsync(InstructorId, me) is null)
            return BadRequest();

        // Yalnızca saat ve durum gönderilir; dolu saatin hangi randevuya ait olduğu gönderilmez
        return new JsonResult(await booking.GetDaySlotsAsync(me, InstructorId!, day));
    }

    // Buluşma yeri araması: /Appointments/Create?handler=Places&instructorId=...&q=...
    // Yalnızca iki kişinin şehri aynıysa (Yüz Yüze seçilebiliyorsa), o şehir içinde aranır. İstek sunucudan Nominatim'e gider;
    // sayfaya yalnızca yer adı ve koordinat döner (kişisel bilgi yok).
    public async Task<IActionResult> OnGetPlacesAsync(string? q, CancellationToken cancellationToken)
    {
        var me = await userManager.GetUserAsync(User);
        var instructor = me is null ? null : await booking.FindInstructorAsync(InstructorId, me.Id);
        if (me is null || instructor is null || !AppointmentBookingService.CanMeetFaceToFace(me, instructor))
            return BadRequest();

        var result = await places.SearchAsync(q, instructor.City, cancellationToken);
        return new JsonResult(new
        {
            ok = result.Succeeded,
            error = result.Error,
            places = result.Places.Select(p => new
            {
                name = p.Name,
                lat = p.Latitude.ToString("0.######", CultureInfo.InvariantCulture),
                lon = p.Longitude.ToString("0.######", CultureInfo.InvariantCulture)
            })
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync())
            return NotFoundPage();

        if (!ModelState.IsValid)
            return Page();

        // Koordinatlar: boşsa elle yazılmış yer; doluysa noktalı ondalık sayı olmalı (kurallar sunucuda: AppointmentBookingService)
        if (!PlaceSearchService.TryParseCoordinate(Input.PlaceLatitude, out var lat) || !PlaceSearchService.TryParseCoordinate(Input.PlaceLongitude, out var lon))
        {
            ModelState.AddModelError(string.Empty, "Buluşma yerinin konumu geçersiz. Lütfen yeri yeniden seçin ya da elle yazın.");
            Input.PlaceLatitude = Input.PlaceLongitude = null;
            return Page();
        }

        var request = new AppointmentBookingService.BookingRequest(
            userManager.GetUserId(User)!, InstructorId, Input.SkillId!.Value, Input.Date!.Value,
            Input.StartHour!.Value, Input.DurationHours, (MeetingType)Input.MeetingType,
            Input.PlaceName, lat, lon);
        var result = Input.JoinWaitlist
            ? await booking.JoinWaitlistAsync(request)
            : await booking.CreateRequestAsync(request);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            // Seçilen saat bir daha gönderilmesin; gün seçili kalır, saatler yeniden yüklenir
            ModelState.Remove("Input.StartHour");
            Input.StartHour = null;
            return Page();
        }

        if (Input.JoinWaitlist)
        {
            TempData["StatusMessage"] = "Bekleme listesine eklendiniz. Saat boşalınca talebiniz otomatik gönderilir.";
            return RedirectToPage("/Appointments/Waitlist");
        }

        TempData["StatusMessage"] = "Randevu talebiniz gönderildi. 24 saat içinde yanıtlanmazsa talep kapanır.";
        return RedirectToPage("/Members/Profile", new { id = InstructorId });
    }

    // Eğitmen bilgileri ve yetenekleri; randevu alınamayacak biriyse false
    private async Task<bool> LoadAsync()
    {
        var me = await userManager.GetUserAsync(User);
        var instructor = me is null ? null : await booking.FindInstructorAsync(InstructorId, me.Id);
        if (me is null || instructor is null)
            return false;

        Found = true;
        InstructorName = $"{instructor.FirstName} {instructor.LastName}";
        InstructorCity = instructor.City;
        CanFaceToFace = AppointmentBookingService.CanMeetFaceToFace(me, instructor);
        Skills = await db.UserSkills
            .Where(us => us.UserId == instructor.Id)
            .OrderBy(us => us.Skill.Name)
            .Select(us => new SkillOption(us.SkillId, us.Skill.Name))
            .ToListAsync();
        return true;
    }

    private PageResult NotFoundPage()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }
}
