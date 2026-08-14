namespace Nekomata.AI.Models.Meetings;

public class MeetingAnalysisResponse
{
    public string Summary { get; set; } = "";

    public List<MeetingAction> Actions { get; set; } = [];

    public List<string> Questions { get; set; } = [];
}