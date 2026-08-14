namespace Nekomata.Models.Planning;

public class MissionTimelineItem
{
    public string Title { get; set; } = "";

    public string ItemType { get; set; } = "Mission";

    public string SourceType { get; set; } = "";

    public string? SourceRecordId { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime EndAt { get; set; }

    public int DurationMinutes =>
        Math.Max(
            (int)(EndAt - StartAt).TotalMinutes,
            0);

    public string TimeRangeFormatted =>
        $"{StartAt:HH:mm}–{EndAt:HH:mm}";

    public string DurationFormatted
    {
        get
        {
            var minutes = DurationMinutes;

            if (minutes < 60)
                return $"{minutes}m";

            var hours = minutes / 60;
            var remainingMinutes = minutes % 60;

            return remainingMinutes == 0
                ? $"{hours}h"
                : $"{hours}h {remainingMinutes}m";
        }
    }

    public bool IsFixed { get; set; }

    public bool IsCompleted { get; set; }

    public bool IsCurrent =>
        DateTime.Now >= StartAt &&
        DateTime.Now < EndAt;

    public string Status { get; set; } = "Planned";

    public string Description { get; set; } = "";

    public int Score { get; set; }

    public decimal BusinessValue { get; set; }

    public int RemainingMinutes { get; set; }

    public bool IsPartial =>
        RemainingMinutes > 0;
}