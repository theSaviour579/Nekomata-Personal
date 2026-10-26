namespace Nekomata.Models.Guardian;

public sealed class GuardianContextSignal
{
    public string Title { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string RecommendedAction { get; set; } = "Review";
    public int Significance { get; set; }
    public bool IsActionable { get; set; }
    public DateTimeOffset? ChangedAt { get; set; }
    public string ContextId { get; set; } = "";
}

public sealed class GuardianContextBriefingInput
{
    public string Area { get; set; } = "Workspace";
    public bool IsLoaded { get; set; }
    public bool IsBusy { get; set; }
    public DateTimeOffset? LastRefreshed { get; set; }
    public IReadOnlyList<GuardianContextSignal> Signals { get; set; } = [];
}

public sealed class GuardianContextBriefing
{
    public string Area { get; set; } = "Workspace";
    public string StateLabel { get; set; } = "AWAITING EVIDENCE";
    public string Headline { get; set; } = "Open this area to build its briefing.";
    public string Summary { get; set; } = "Guardian will identify material changes and the best next action.";
    public string NextAction { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string ContextId { get; set; } = "";
    public int ConfidencePercent { get; set; }
    public bool HasAction { get; set; }
}
