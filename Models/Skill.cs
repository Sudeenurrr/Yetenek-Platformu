namespace YetenekPlatformu.Models;

public class Skill
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // Harici veri setindeki kimlik. Veri seti (Data/Seed/skills.csv) yalnızca ad içerdiği için boş kalır.
    public string? ExternalId { get; set; }

    public ICollection<UserSkill> UserSkills { get; set; } = new List<UserSkill>();
}
