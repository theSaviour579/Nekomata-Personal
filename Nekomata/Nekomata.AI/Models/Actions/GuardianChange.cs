namespace Nekomata.AI.Models.Actions;

public class GuardianChange
{
    public bool Selected { get; set; } = true;

    public string EntityType { get; set; } = "";
    // Project, Task, Mission, Memory, Calendar

    public long EntityId { get; set; }

    public string Property { get; set; } = "";

    public string OldValue { get; set; } = "";

    public string NewValue { get; set; } = "";

    public string Reason { get; set; } = "";

    public int Confidence { get; set; } = 100;

    public string ReviewDisposition { get; set; } = "";
    public string FeedbackReason { get; set; } = "";
    public string OriginalTitle { get; set; } = "";
    public string OriginalValue { get; set; } = "";
    public long OriginalEntityId { get; set; }
    public string ReplacementInstruction { get; set; } = "";
    public bool FeedbackRecorded { get; set; }
    public bool IsRemoved => ReviewDisposition.Equals("Removed", StringComparison.OrdinalIgnoreCase);
    public bool HasReviewFeedback => !string.IsNullOrWhiteSpace(ReviewDisposition);
    public string ReviewStatusLabel => ReviewDisposition switch
    {
        "Removed" => "REMOVED FROM PROPOSAL",
        "Replaced" => $"REPLACED · originally {OriginalTitle}",
        _ => ""
    };

    public bool IsCalendarChange => EntityType.Equals("Calendar", StringComparison.OrdinalIgnoreCase);

    public string CalendarTitle
    {
        get
        {
            if (!IsCalendarChange) return string.Empty;
            var parts = NewValue.Split('|', StringSplitOptions.TrimEntries);
            return parts.Length >= 3 ? parts[2] : Property;
        }
    }

    public string CalendarTimeLabel
    {
        get
        {
            if (!IsCalendarChange) return string.Empty;
            var parts = NewValue.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !DateTimeOffset.TryParse(parts[0], out var start) || !DateTimeOffset.TryParse(parts[1], out var end))
                return NewValue;
            return $"{start:ddd dd MMM}  {start:HH:mm}–{end:HH:mm}";
        }
    }
}
