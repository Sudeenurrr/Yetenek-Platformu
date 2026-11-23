namespace YetenekPlatformu.Services;

// Randevu zamanları Türkiye saatine göre hesaplanır: sunucu ya da tarayıcı başka saat diliminde olsa bile
// "şu an" ve "bugün" Türkiye'deki saat ve gündür. Veritabanındaki randevu saatleri de Türkiye saatidir.
public static class AppClock
{
    private static readonly TimeZoneInfo Turkey = FindTurkeyTimeZone();

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Turkey);

    public static DateOnly Today => DateOnly.FromDateTime(Now);

    // "Europe/Istanbul" (IANA) her yerde, "Turkey Standard Time" eski Windows sürümlerinde bulunur
    private static TimeZoneInfo FindTurkeyTimeZone()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
                return zone;
        }
        // Türkiye yaz saati uygulamıyor: sabit UTC+3
        return TimeZoneInfo.CreateCustomTimeZone("Turkey", TimeSpan.FromHours(3), "Türkiye", "Türkiye");
    }
}
