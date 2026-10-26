namespace Nekomata.AI.Models.Meetings;

public class MeetingAction
{
    public bool Selected { get; set; } = true;

    public string ActionType { get; set; } = "";

    public string TargetType { get; set; } = "";

    public string TargetName { get; set; } = "";

    public long? TaskId { get; set; }

    public long? ProjectId { get; set; }

    public string Property { get; set; } = "";

    public string NewValue { get; set; } = "";

    public string Description { get; set; } = "";

    public string Priority { get; set; } = "Normal";

    public int? EstimatedMinutes { get; set; }

    public string DueAt { get; set; } = "";

    public decimal? EstimatedBusinessValue { get; set; }

    public string Reason { get; set; } = "";
}
