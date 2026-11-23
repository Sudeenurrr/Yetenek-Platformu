namespace YetenekPlatformu.Models;

// Randevu talebi. Satır silinmez, yalnızca durumu değişir.
// Öğrenci/eğitmen kullanıcı türü değil, bu randevudaki roldür.
public class Appointment
{
    public int Id { get; set; }

    // Talebi gönderen (dersi alan). Üye silinince boş olur ("Silinmiş Üye")
    public string? StudentId { get; set; }
    public ApplicationUser? Student { get; set; }

    // Talebi alan (dersi veren). Üye silinince boş olur ("Silinmiş Üye")
    public string? InstructorId { get; set; }
    public ApplicationUser? Instructor { get; set; }

    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;

    public DateTime StartTime { get; set; }

    // 1 veya 2 saat
    public byte DurationHours { get; set; }

    public AppointmentStatus Status { get; set; }

    public MeetingType MeetingType { get; set; }

    // Online randevuda onaylarken girilir
    public string? MeetingLink { get; set; }

    // Yüz yüze buluşma yeri (Nominatim)
    public string? MeetingPlaceName { get; set; }
    public decimal? MeetingLatitude { get; set; }
    public decimal? MeetingLongitude { get; set; }

    public DateTime CreatedAt { get; set; }

    // CreatedAt + 24 saat
    public DateTime ExpiresAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    // İptal eden ya da talebi geri çeken kişi
    public string? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }

    // Değişiklik önerisi
    public DateTime? ProposedStartTime { get; set; }
    public byte? ProposedDurationHours { get; set; }
    public string? ProposedByUserId { get; set; }

    // Önerinin yapıldığı an: öneri 24 saat içinde yanıtlanmazsa düşer
    public DateTime? ProposedAt { get; set; }

    // Değişiklik önerisinde önerilen yeni buluşma yeri (yalnızca Yüz Yüze). Ad boşsa öneride yer değişmiyor;
    // elle yazılmış yerde koordinatlar boştur. Kabulde Meeting... alanlarına taşınır.
    public string? ProposedPlaceName { get; set; }
    public decimal? ProposedLatitude { get; set; }
    public decimal? ProposedLongitude { get; set; }

    // Kırmızı nokta: taraf son değişikliği gördü mü
    public bool StudentSeen { get; set; }
    public bool InstructorSeen { get; set; }

    // Aynı anda yapılan değişiklikleri fark etmek için
    public byte[] RowVersion { get; set; } = null!;

    public ICollection<TimeSlot> TimeSlots { get; set; } = new List<TimeSlot>();
}
