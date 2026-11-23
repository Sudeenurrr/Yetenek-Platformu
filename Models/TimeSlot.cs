namespace YetenekPlatformu.Models;

// Bir kişinin dolu olan bir saati. Her randevu hem öğrenci hem eğitmen için satır yazar.
// UserId + SlotStart benzersizdir: çakışma veritabanı seviyesinde engellenir.
public class TimeSlot
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public DateTime SlotStart { get; set; }

    public int AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;

    // true ise değişiklik önerisi için geçici ayrılmış saat
    public bool IsProposal { get; set; }
}
