namespace YetenekPlatformu.Settings;

// Müsait saatler. Değerler appsettings.json içindeki "Scheduling" bölümünden okunur.
public class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    // İlk dersin başlayabileceği saat (ör. 9 = 09:00)
    public int DayStartHour { get; set; } = 9;

    // Son dersin bitmesi gereken saat (ör. 21 = 21:00)
    public int DayEndHour { get; set; } = 21;

    // Bugünden itibaren en fazla kaç gün sonrasına randevu alınabilir
    public int MaxDaysAhead { get; set; } = 30;

    // Otomatik durum güncelleme görevinin (AppointmentStatusWorker) kaç saniyede bir çalışacağı
    public int StatusUpdateIntervalSeconds { get; set; } = 60;
}
