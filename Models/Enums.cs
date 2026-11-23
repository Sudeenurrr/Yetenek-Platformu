namespace YetenekPlatformu.Models;

// Randevu durumları. Sayısal değerler veritabanına yazılır, değiştirmeyin.
public enum AppointmentStatus
{
    Pending = 0,          // Beklemede
    Approved = 1,         // Onaylandı
    Rejected = 2,         // Reddedildi
    Cancelled = 3,        // İptal Edildi
    ChangeProposed = 4,   // Değişiklik Önerildi
    Completed = 5,        // Tamamlandı
    Expired = 6           // Süresi Doldu
}

public enum MeetingType
{
    Online = 0,
    FaceToFace = 1        // Yüz Yüze
}

public enum WaitlistStatus
{
    Waiting = 0,          // Bekliyor
    Activated = 1,        // Aktifleşti
    Withdrawn = 2,        // Vazgeçildi
    Invalid = 3           // Geçersiz
}
