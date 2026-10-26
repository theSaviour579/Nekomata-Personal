using CommunityToolkit.Mvvm.ComponentModel;
using Nekomata.Models.Guardian;
using System.Text.Json.Serialization;

namespace Nekomata.UI.ViewModels;

public partial class GuardianAttentionItem : ObservableObject
{
    public string Key { get; set; } = "";
    public string Source { get; set; } = "Guardian";
    public string Severity { get; set; } = "Info";
    public string Title { get; set; } = "Attention required";
    [ObservableProperty] private string detail = "";
    public string ActionKind { get; set; } = "open_dashboard";
    public string ContextId { get; set; } = "";
    public string WebLink { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public bool MeetingPromptShown { get; set; }
    public List<MeetingBriefSource> MeetingSources { get; set; } = [];
    public string CompletionEvidence { get; set; } = "";
    public Guid? DecisionOutcomeId { get; set; }
    public GuardianInterruptionRoute InterruptionRoute { get; set; } = GuardianInterruptionRoute.Immediate;
    public int InterruptionPriorityScore { get; set; }
    public int InterruptionConfidence { get; set; }
    public string InterruptionReason { get; set; } = "";
    public List<string> InterruptionEvidence { get; set; } = [];
    public DateTimeOffset? InterruptionReleasedAt { get; set; }

    [ObservableProperty] private string status = "Active";
    [ObservableProperty] private DateTimeOffset? snoozedUntil;

    public bool IsActive => Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
    public string CreatedLabel => CreatedAt.LocalDateTime.ToString("ddd HH:mm");
    public string SnoozeLabel => SnoozedUntil is null ? "" : $"Snoozed until {SnoozedUntil.Value.LocalDateTime:HH:mm}";
    [JsonIgnore] public Nekomata.Core.Guardian.Authority.GuardianAttentionAssessment AttentionAssessment =>
        Nekomata.Core.Guardian.Authority.GuardianAttentionPolicy.Assess(Source, Severity, Title, Detail, ActionKind);
    [JsonIgnore] public bool RequiresHumanJudgement => AttentionAssessment.RequiresJudgement;
    [JsonIgnore] public string JudgementReason => AttentionAssessment.Reason;
    [JsonIgnore] public string InterruptionRouteLabel => InterruptionRoute switch
    {
        GuardianInterruptionRoute.Immediate => "SURFACED NOW",
        GuardianInterruptionRoute.NaturalBreak => InterruptionReleasedAt is null ? "QUEUED FOR A NATURAL BREAK" : "RELEASED AT A NATURAL BREAK",
        GuardianInterruptionRoute.Briefing => "HELD FOR BRIEFING",
        _ => "SUPPRESSED"
    };
    public string ActionLabel => ActionKind switch
    {
        "meeting_capture" => "REVIEW MEETING NOTES",
        "meeting_preparation" => "OPEN MEETING BRIEF",
        "adaptive_replan" => "REVIEW SWITCH",
        "review_halo_chase" => "REVIEW EMAIL / DRAFT REPLY",
        "review_project_hold" => "REVIEW PROJECT STATE",
        "review_halo_reply" => "DRAFT REPLY",
        "open_email" => "OPEN EMAIL",
        "open_halo" => "OPEN BATTLE MODE",
        "open_halo_ticket" => "OPEN TICKET IN HALO",
        "start_calendar" => "START / OPEN PLAN",
        "open_calendar" => "OPEN CALENDAR",
        "open_diagnostics" => "OPEN DIAGNOSTICS",
        "spotify_arrival_swap" => "SWAP TO ARRIVAL MIX",
        _ => "OPEN DASHBOARD"
    };

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(IsActive));
    partial void OnDetailChanged(string value)
    {
        OnPropertyChanged(nameof(AttentionAssessment));
        OnPropertyChanged(nameof(RequiresHumanJudgement));
        OnPropertyChanged(nameof(JudgementReason));
    }
    partial void OnSnoozedUntilChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(SnoozeLabel));
}

public sealed class MeetingBriefSource
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}
