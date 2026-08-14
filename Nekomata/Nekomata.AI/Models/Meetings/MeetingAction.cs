namespace Nekomata.AI.Models.Meetings;

public class MeetingAction
{
    public bool Selected { get; set; } = true;

    public string ActionType { get; set; } = "";

    public string TargetType { get; set; } = "";

    public string TargetName { get; set; } = "";

    public string Property { get; set; } = "";

    public string NewValue { get; set; } = "";

    public string Reason { get; set; } = "";
}