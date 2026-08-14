using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nekomata.Integrations.MicrosoftGraph.Mail;

namespace Nekomata.UI.ViewModels;

public partial class EmailTriageItem : ObservableObject
{
    public required EmailMessage Message { get; init; }

    [ObservableProperty] private string classification = "FYI";
    [ObservableProperty] private int priorityScore = 50;
    [ObservableProperty] private string reason = "Unread inbox message.";
    [ObservableProperty] private string suggestedAction = "Review";
    [ObservableProperty] private bool replyRecommended;
    [ObservableProperty] private string draftText = "";
    [ObservableProperty] private string draftStatus = "";
    [ObservableProperty] private string outlookDraftId = "";
    [ObservableProperty] private string outlookDraftWebLink = "";
    [ObservableProperty] private bool replySent;
    [ObservableProperty] private bool categoryApplied;
    [ObservableProperty] private bool isManagement;
    [ObservableProperty] private string fullContent = "";
    [ObservableProperty] private bool isBodyExpanded;
    [ObservableProperty] private DateTime? selectedMeetingDate;
    [ObservableProperty] private int requestedMeetingMinutes = 30;
    [ObservableProperty] private string meetingSuggestions = "";
    [ObservableProperty] private ObservableCollection<MeetingDateOption> meetingDateOptions = [];
    [ObservableProperty] private string meetingTimePreference = "Any";

    public bool IsFiltered =>
        Classification.Equals("Marketing", StringComparison.OrdinalIgnoreCase) ||
        Classification.Equals("Spam", StringComparison.OrdinalIgnoreCase);

    public string CcLine => Message.CcRecipients.Count == 0
        ? string.Empty
        : $"CC: {string.Join(", ", Message.CcRecipients)}";
    public string SenderLine => string.IsNullOrWhiteSpace(Message.SenderAddress)
        ? Message.SenderName
        : $"{Message.SenderName} <{Message.SenderAddress}>";
    public string ReceivedLabel => Message.ReceivedAt.LocalDateTime.ToString("ddd HH:mm");
    public string AttachmentLabel => Message.HasAttachments ? " • attachment" : "";
    public string PriorityLabel => $"PRIORITY {PriorityScore}/100";
    public string DisplayContent => string.IsNullOrWhiteSpace(FullContent) ? Message.BodyPreview : FullContent;
    public string ManagementActionLabel => IsManagement ? "REMOVE MANAGEMENT FLAG" : "MARK SENDER AS MANAGEMENT";
    public string BodyToggleLabel => IsBodyExpanded ? "COLLAPSE MESSAGE" : "READ FULL MESSAGE";

    partial void OnClassificationChanged(string value) => OnPropertyChanged(nameof(IsFiltered));
    partial void OnPriorityScoreChanged(int value) => OnPropertyChanged(nameof(PriorityLabel));
    partial void OnFullContentChanged(string value) => OnPropertyChanged(nameof(DisplayContent));
    partial void OnIsManagementChanged(bool value) => OnPropertyChanged(nameof(ManagementActionLabel));
    partial void OnIsBodyExpandedChanged(bool value) => OnPropertyChanged(nameof(BodyToggleLabel));
}