using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;

namespace YetenekPlatformu.Pages.Appointments;

// Değişiklik Öner: onaylanmış randevu için yeni gün/saat/süre ve (Yüz Yüze randevuda) yeni buluşma yeri önerilir;
// ikisi birlikte ya da biri tek başına. Takvim Randevu Al ile aynıdır (wwwroot/js/appointment.js),
// yer araması da (wwwroot/js/place-search.js; yalnızca "Ara" / Enter, istek sunucudan Nominatim'e gider).
// Bütün kurallar (taraf, durum, saat aralığı, geçmiş saat, 30 gün, çakışma, aynı şehir, yer) AppointmentChangeService'te,
// sunucuda kontrol edilir. Telefon ve IBAN bu sayfaya gönderilmez.
[Authorize(Roles = AppRoles.Member)]
public class ProposeModel(
    AppointmentChangeService changes,
    AppointmentBookingService booking,
    PlaceSearchService places,
    UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        // Neyi değiştirmek istiyor: gün/saat ve/veya buluşma yeri (en az biri; sunucu kontrol eder)
        public bool ChangeTime { get; set; }
        public bool ChangePlace { get; set; }

        // Gün/saat yalnızca ChangeTime seçiliyse zorunludur (sunucu kontrol eder)
        public DateOnly? Date { get; set; }
        public int? StartHour { get; set; }
        public int DurationHours { get; set; } = 1;

        // Yeni buluşma yeri. Koordinatlar metin olarak alınır ve noktalı ondalıkla okunur; elle yazılan yerde boştur.
        public string? PlaceName { get; set; }
        public string? PlaceLatitude { get; set; }
        public string? PlaceLongitude { get; set; }
    }

    public AppointmentChangeService.ProposalTarget? Target { get; private set; }

    public DateOnly MinDate => booking.Today;
    public DateOnly MaxDate => booking.LastBookableDay;
    public int MaxDaysAhead => booking.MaxDaysAhead;
    public int DayStartHour => booking.DayStartHour;
    public int DayEndHour => booking.DayEndHour;

    private string Me => userManager.GetUserId(User)!;

    public async Task<IActionResult> OnGetAsync()
    {
        Target = await changes.FindTargetAsync(Id, Me);
        if (Target is null)
            return NotFoundPage();

        // Başlangıçta gün/saat değişikliği seçili, süre mevcut süreyle aynı
        Input.ChangeTime = true;
        Input.DurationHours = Target.DurationHours;
        return Page();
    }

    // Seçilen günün saatleri: /Appointments/Propose?handler=Slots&id=...&date=yyyy-MM-dd
    public async Task<IActionResult> OnGetSlotsAsync(DateOnly? date)
    {
        if (date is not DateOnly day || day < booking.Today || day > booking.LastBookableDay)
            return BadRequest();

        var slots = await changes.GetDaySlotsAsync(Id, Me, day);
        return slots is null ? BadRequest() : new JsonResult(slots);
    }

    // Yeni buluşma yeri araması: /Appointments/Propose?handler=Places&id=...&q=...
    // Yalnızca randevunun iki tarafı, Onaylandı Yüz Yüze randevu ve iki kişinin şehri hâlâ aynıysa; o şehir içinde aranır.
    // Sayfaya yalnızca yer adı ve koordinat döner.
    public async Task<IActionResult> OnGetPlacesAsync(string? q, CancellationToken cancellationToken)
    {
        var city = await changes.FindPlaceSearchCityAsync(Id, Me);
        if (city is null)
            return BadRequest();

        var result = await places.SearchAsync(q, city, cancellationToken);
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
        Target = await changes.FindTargetAsync(Id, Me);
        if (Target is null)
            return NotFoundPage();

        if (!ModelState.IsValid)
            return Page();

        if (!PlaceSearchService.TryParseCoordinate(Input.PlaceLatitude, out var lat) || !PlaceSearchService.TryParseCoordinate(Input.PlaceLongitude, out var lon))
        {
            ModelState.AddModelError(string.Empty, "Buluşma yerinin konumu geçersiz. Lütfen yeri yeniden seçin ya da elle yazın.");
            Input.PlaceLatitude = Input.PlaceLongitude = null;
            return Page();
        }

        var result = await changes.ProposeAsync(Id, Me, new AppointmentChangeService.ProposalInput(
            Input.ChangeTime, Input.Date, Input.StartHour, Input.DurationHours,
            Input.ChangePlace, Input.PlaceName, lat, lon));
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            // Seçilen saat bir daha gönderilmesin; gün seçili kalır, saatler yeniden yüklenir
            ModelState.Remove("Input.StartHour");
            Input.StartHour = null;
            return Page();
        }

        TempData["StatusMessage"] = result.Message;
        return RedirectToPage(Target.IsStudent ? "/Appointments/Sent" : "/Appointments/Incoming", pageHandler: null, routeValues: null, fragment: $"randevu-{Id}");
    }

    private PageResult NotFoundPage()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }
}
