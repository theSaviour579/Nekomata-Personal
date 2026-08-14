using CommunityToolkit.Mvvm.ComponentModel;

namespace Nekomata.UI.ViewModels;

public partial class GuardianAttentionItem : ObservableObject
{
    public string Key { get; set; } = "";
    public string Source { get; set; } = "Guardian";
    public string Severity { get; set; } = "Info";
    public string Title { get; set; } = "Attention required";
    public string Detail { get; set; } = "";
    public string ActionKind { get; set; } = "open_dashboard";
    public string ContextId { get; set; } = "";
    public string WebLink { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [ObservableProperty] private string status = "Active";
    [ObservableProperty] private DateTimeOffset? snoozedUntil;

    public bool IsActive => Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
    public string CreatedLabel => CreatedAt.LocalDateTime.ToString("ddd HH:mm");
    public string SnoozeLabel => SnoozedUntil is null ? "" : $"Snoozed until {SnoozedUntil.Value.LocalDateTime:HH:mm}";
    public string ActionLabel => ActionKind switch
    {
        "open_email" => "OPEN EMAIL",
        "open_halo" => "OPEN BATTLE MODE",
        "open_halo_ticket" => "OPEN TICKET IN HALO",
        "start_calendar" => "START / OPEN PLAN",
        "open_calendar" => "OPEN CALENDAR",
        _ => "OPEN DASHBOARD"
    };

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(IsActive));
    partial void OnSnoozedUntilChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(SnoozeLabel));
}