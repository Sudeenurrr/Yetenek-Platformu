namespace YetenekPlatformu.Models;

// Dolu bir saat için bekleme listesi kaydı. Sıra CreatedAt'e göre belirlenir.
public class WaitlistEntry
{
    public int Id { get; set; }

    // Sırada bekleyen kişi
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public string InstructorId { get; set; } = string.Empty;
    public ApplicationUser Instructor { get; set; } = null!;

    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;

    public DateTime StartTime { get; set; }

    public byte DurationHours { get; set; }

    // Sıra gelince talebin hazır oluşturulabilmesi için
    public MeetingType MeetingType { get; set; }
    public string? MeetingPlaceName { get; set; }
    public decimal? MeetingLatitude { get; set; }
    public decimal? MeetingLongitude { get; set; }

    public DateTime CreatedAt { get; set; }

    public WaitlistStatus Status { get; set; }

    // Sıra gelince oluşturulan randevu
    public int? ActivatedAppointmentId { get; set; }
    public Appointment? ActivatedAppointment { get; set; }
}
