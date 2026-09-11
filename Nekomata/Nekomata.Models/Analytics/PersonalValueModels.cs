namespace Nekomata.Models.Analytics;

public sealed record RoleGoalDefinition
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int MinPercent { get; init; }
    public int MaxPercent { get; init; }
    public List<string> Keywords { get; init; } = [];
}

public sealed record PersonalRoleProfile
{
    public string JobTitle { get; init; } = "";
    public string Source { get; init; } = "Manual";
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<RoleGoalDefinition> Goals { get; init; } = [];
}

public sealed class RoleGoalProgress
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int Minutes { get; init; }
    public int ActualPercent { get; init; }
    public int MinPercent { get; init; }
    public int MaxPercent { get; init; }
    public string Status => ActualPercent < MinPercent ? "Needs more focus" : ActualPercent > MaxPercent ? "Above target" : "On target";
    public string AllocationLabel => $"{ActualPercent}% · target {MinPercent}–{MaxPercent}%";
}

public sealed class PersonalWorkEpisode
{
    public string Title { get; init; } = "";
    public string GoalName { get; init; } = "Unaligned work";
    public int Minutes { get; init; }
    public decimal BusinessValue { get; init; }
    public DateTime FinishedAt { get; init; }
    public string Detail => $"{Minutes} min · {GoalName}";
}

public sealed class PersonalValueSummary
{
    public string JobTitle { get; init; } = "Your role";
    public string RoleSource { get; init; } = "Manual";
    public string PeriodLabel { get; init; } = "This week";
    public int TotalMinutes { get; init; }
    public decimal DeliveredValue { get; init; }
    public int AlignmentPercent { get; init; }
    public List<RoleGoalProgress> Goals { get; init; } = [];
    public List<PersonalWorkEpisode> Episodes { get; init; } = [];
    public List<string> Insights { get; init; } = [];
    public string FocusLabel => $"{TotalMinutes / 60}h {TotalMinutes % 60}m";
    public string ValueLabel => DeliveredValue.ToString("C0");
    public string AlignmentLabel => $"{AlignmentPercent}%";
}
