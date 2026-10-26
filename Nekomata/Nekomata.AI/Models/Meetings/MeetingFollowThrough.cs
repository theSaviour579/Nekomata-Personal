namespace Nekomata.AI.Models.Meetings;

public sealed class MeetingFollowThrough
{
    public List<MeetingFollowThroughItem> Items { get; set; } = [];
    public List<string> Questions { get; set; } = [];
}
public sealed class MeetingFollowThroughItem
{
    public string Kind { get; set; } = "Action";
    public string Title { get; set; } = "";
    public string Owner { get; set; } = "";
    public string DueDate { get; set; } = "";
    public string Evidence { get; set; } = "";
}
