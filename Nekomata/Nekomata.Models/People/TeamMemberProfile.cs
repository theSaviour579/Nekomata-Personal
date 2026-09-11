namespace Nekomata.Models.People;

public sealed class TeamMemberProfile
{
    public long Id { get; set; }
    public string ExternalId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string Source { get; set; } = "Manual";
    public DateTimeOffset? LastSyncedAt { get; set; }
    public bool Active { get; set; } = true;
    public string AvailabilityStatus { get; set; } = "Available";
    public DateTimeOffset? UnavailableUntil { get; set; }
    public int CapacityPercent { get; set; } = 100;
    public string Responsibilities { get; set; } = "";
    public string Exclusions { get; set; } = "";
    public List<TeamMemberSkill> Skills { get; set; } = [];
    public string SourceLabel => Source == "Microsoft 365" && LastSyncedAt is DateTimeOffset synced ? $"Microsoft 365 · synced {synced.LocalDateTime:g}" : Source;
}

public sealed class TeamMemberSkill
{
    public long SkillId { get; set; }
    public string Name { get; set; } = "";
    public int Proficiency { get; set; } = 2;
    public bool IsPrimary { get; set; }
    public string Aliases { get; set; } = "";
}
