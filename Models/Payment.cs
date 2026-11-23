namespace YetenekPlatformu.Models;

// Gönüllü ödeme. Ders başına en fazla bir ödeme (AppointmentId benzersiz).
public class Payment
{
    public int Id { get; set; }

    // Ödeyen (dersi alan). Üye silinince boş olur ("Silinmiş Üye")
    public string? SenderId { get; set; }
    public ApplicationUser? Sender { get; set; }

    // Alan (dersi veren). Üye silinince boş olur ("Silinmiş Üye")
    public string? ReceiverId { get; set; }
    public ApplicationUser? Receiver { get; set; }

    public int AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;

    // Yalnızca PaymentOptions.AllowedAmounts içindeki değerler
    public decimal Amount { get; set; }

    public DateTime CreatedAt { get; set; }
}
