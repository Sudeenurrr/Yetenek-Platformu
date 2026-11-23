namespace YetenekPlatformu.Models;

// Kullanıcı ile yetenek arasındaki çoka-çok bağlantı. PK: UserId + SkillId
public class UserSkill
{
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
