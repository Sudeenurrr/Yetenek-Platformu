namespace YetenekPlatformu.Models;

// Favori eğitmen. PK: UserId + InstructorId
public class Favorite
{
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public string InstructorId { get; set; } = string.Empty;
    public ApplicationUser Instructor { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
