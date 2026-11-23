namespace YetenekPlatformu.Settings;

// Konum arama (OpenStreetMap Nominatim). Değerler appsettings.json içindeki "Nominatim" bölümünden okunur.
// Kullanım kuralları: saniyede en fazla 1 istek, anlamlı User-Agent, otomatik tamamlama yok, sonuçlara atıf.
public class NominatimOptions
{
    public const string SectionName = "Nominatim";

    // Servis adresi (sonunda "/" olmalı)
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org/";

    // Uygulamayı tanıtan User-Agent (kişisel e-posta yazılmaz)
    public string UserAgent { get; set; } = "YetenekPlatformu/1.0 (bitirme projesi)";

    // Bütün site için iki istek arasındaki en kısa süre (milisaniye)
    public int MinIntervalMilliseconds { get; set; } = 1100;

    // Sırada bekleyen istek en fazla bu kadar bekler, sonra "yoğun" mesajı döner (milisaniye)
    public int MaxQueueWaitMilliseconds { get; set; } = 4000;

    // Servis bu sürede cevap vermezse "arama yapılamıyor" mesajı gösterilir (saniye)
    public int TimeoutSeconds { get; set; } = 5;

    // Aynı şehirde aynı arama bu süre boyunca sunucu belleğinden cevaplanır (saat)
    public int CacheHours { get; set; } = 24;
}
