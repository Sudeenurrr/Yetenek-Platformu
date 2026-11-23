using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using YetenekPlatformu.Settings;

namespace YetenekPlatformu.Services;

// Yüz Yüze buluşma yeri araması (OpenStreetMap Nominatim). İstek tarayıcıdan değil sunucudan gider:
//   - Bütün site için iki istek arasında en az MinIntervalMilliseconds (saniyede en fazla 1 istek); sırada fazla bekleyen
//     istek "yoğun" mesajı alır. Aynı şehirde aynı arama CacheHours boyunca bellekten cevaplanır.
//   - Arama Türkiye ile sınırlıdır, dil Türkçe; sonuçlar iki kişinin ortak şehrine göre elenir.
//   - Servis çalışmazsa sade bir mesaj döner; kullanıcı yeri elle yazabilir, randevu alma etkilenmez.
// Sunucuya giden adres her zaman ayardaki sabit adrestir; kullanıcı yalnızca arama metnini verir (kodlanarak eklenir).
public class PlaceSearchService(
    HttpClient http,
    IMemoryCache cache,
    IOptions<NominatimOptions> options,
    ILogger<PlaceSearchService> logger)
{
    public const int MinQueryLength = 3;
    public const int MaxQueryLength = 100;
    public const int PlaceNameMaxLength = 300;   // Appointments / WaitlistEntries.MeetingPlaceName
    public const string UnavailableMessage = "Konum araması şu an yapılamıyor. Buluşma yerini elle yazabilirsiniz.";

    private const int ResultLimit = 5;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // Bütün site için tek kapı (sınıf her istekte yeniden oluşsa da kapı ve son istek zamanı ortaktır)
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime lastRequestUtc = DateTime.MinValue;

    public record PlaceResult(string Name, decimal Latitude, decimal Longitude);

    public record SearchResult(bool Succeeded, string? Error, IReadOnlyList<PlaceResult> Places)
    {
        public static SearchResult Fail(string error) => new(false, error, []);
    }

    // Arama: metin en az 3, en fazla 100 karakter; city iki kişinin ortak şehri
    public async Task<SearchResult> SearchAsync(string? query, string city, CancellationToken cancellationToken = default)
    {
        var text = CollapseSpaces(query);
        if (text.Length < MinQueryLength)
            return SearchResult.Fail($"Aramak için en az {MinQueryLength} karakter yazın.");
        if (text.Length > MaxQueryLength)
            return SearchResult.Fail($"Arama en fazla {MaxQueryLength} karakter olabilir.");

        var cacheKey = $"nominatim|{city.ToLower(Turkish)}|{text.ToLower(Turkish)}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<PlaceResult>? cached) && cached is not null)
            return new SearchResult(true, null, cached);

        var settings = options.Value;
        if (!await Gate.WaitAsync(settings.MaxQueueWaitMilliseconds, cancellationToken))
            return SearchResult.Fail("Konum araması şu an yoğun. Lütfen birkaç saniye sonra tekrar deneyin.");
        try
        {
            // Saniyede en fazla 1 istek: önceki istekten bu yana yeterli süre geçmediyse beklenir
            var wait = lastRequestUtc.AddMilliseconds(settings.MinIntervalMilliseconds) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);

            var url = "search?q=" + Uri.EscapeDataString($"{text}, {city}")
                + "&format=jsonv2&countrycodes=tr&accept-language=tr&addressdetails=1&limit=10";
            lastRequestUtc = DateTime.UtcNow;

            using var response = await http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Konum araması başarısız: HTTP {Status}.", (int)response.StatusCode);
                return SearchResult.Fail(UnavailableMessage);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var places = ReadPlaces(json.RootElement, city);

            cache.Set(cacheKey, places, TimeSpan.FromHours(settings.CacheHours));
            return new SearchResult(true, null, places);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
            && !cancellationToken.IsCancellationRequested)
        {
            // İnternet yok, zaman aşımı ya da beklenmeyen cevap
            logger.LogWarning("Konum araması yapılamadı: {Error}", ex.Message);
            return SearchResult.Fail(UnavailableMessage);
        }
        finally
        {
            Gate.Release();
        }
    }

    // Nominatim cevabı: yalnızca ortak şehirdeki yerler, en fazla 5 sonuç
    private static List<PlaceResult> ReadPlaces(JsonElement root, string city)
    {
        var places = new List<PlaceResult>();
        if (root.ValueKind != JsonValueKind.Array)
            return places;

        foreach (var item in root.EnumerateArray())
        {
            if (!InCity(item, city)
                || !TryGetString(item, "display_name", out var name)
                || !TryGetString(item, "lat", out var latText) || !TryGetString(item, "lon", out var lonText)
                || !decimal.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !decimal.TryParse(lonText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                continue;

            lat = Math.Round(lat, 6);
            lon = Math.Round(lon, 6);
            if (!IsInTurkey(lat, lon))
                continue;

            name = CollapseSpaces(name);
            if (name.EndsWith(", Türkiye", StringComparison.Ordinal))
                name = name[..^", Türkiye".Length];
            if (name.Length > PlaceNameMaxLength)
                name = name[..PlaceNameMaxLength].TrimEnd(' ', ',');

            if (places.Any(p => p.Name == name))
                continue;
            places.Add(new PlaceResult(name, lat, lon));
            if (places.Count == ResultLimit)
                break;
        }
        return places;
    }

    // Yer ortak şehirde mi: adres bilgisindeki il (province), şehir ya da bölge adı şehirle aynıysa (Türkçe, büyük/küçük harf duyarsız)
    private static bool InCity(JsonElement item, string city)
    {
        if (!item.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var key in new[] { "province", "city", "state", "town" })
            if (TryGetString(address, key, out var value) && string.Compare(value.Trim(), city, Turkish, CompareOptions.IgnoreCase) == 0)
                return true;
        return false;
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    // Türkiye'nin yaklaşık sınırları (enlem 35.8–42.2, boylam 25.6–44.9)
    public static bool IsInTurkey(decimal latitude, decimal longitude) =>
        latitude is >= 35.8m and <= 42.2m && longitude is >= 25.6m and <= 44.9m;

    // Yüz Yüze buluşma yeri kontrolü (sunucuda): ad zorunlu ve en fazla 300 karakter; koordinatlar ya ikisi birden
    // boştur (elle yazılmış yer) ya da ikisi de doludur ve Türkiye sınırları içindedir.
    public static bool TryValidatePlace(string? name, decimal? latitude, decimal? longitude,
        out string cleanedName, out decimal? lat, out decimal? lon, out string error)
    {
        cleanedName = CollapseSpaces(name);
        lat = latitude is decimal a ? Math.Round(a, 6) : null;
        lon = longitude is decimal o ? Math.Round(o, 6) : null;
        error = string.Empty;

        if (cleanedName.Length == 0)
            error = "Yüz Yüze randevu için buluşma yerini seçin ya da yazın.";
        else if (cleanedName.Length > PlaceNameMaxLength)
            error = $"Buluşma yeri en fazla {PlaceNameMaxLength} karakter olabilir.";
        else if (lat.HasValue != lon.HasValue)
            error = "Buluşma yerinin konumu eksik. Lütfen yeri yeniden seçin ya da elle yazın.";
        else if (lat is decimal la && lon is decimal lo && !IsInTurkey(la, lo))
            error = "Buluşma yeri Türkiye sınırları içinde olmalıdır.";

        return error.Length == 0;
    }

    // Formdan gelen koordinat metni: boşsa koordinat yok (elle yazılmış yer); doluysa noktalı ondalık sayı olmalı
    // (Türkçe ayarda ondalık ayırıcı virgül olduğu için sayı olarak bağlanmaz, metin olarak alınıp burada okunur)
    public static bool TryParseCoordinate(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return false;
        value = parsed;
        return true;
    }

    // Harita bağlantısı (openstreetmap.org): yalnızca sayılardan, nokta ondalık ayırıcıyla oluşturulur
    public static string MapUrl(decimal latitude, decimal longitude)
    {
        var la = latitude.ToString("0.######", CultureInfo.InvariantCulture);
        var lo = longitude.ToString("0.######", CultureInfo.InvariantCulture);
        return $"https://www.openstreetmap.org/?mlat={la}&mlon={lo}#map=17/{la}/{lo}";
    }

    private static string CollapseSpaces(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
