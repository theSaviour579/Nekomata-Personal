namespace Nekomata.AI.Models.Actions;

public class GuardianActionResponse
{
    public string Message { get; set; } = "";

    public string ActionType { get; set; } = "";

    public long? ProjectId { get; set; }

    public List<ProposedTask> Tasks { get; set; } = [];

    public List<GuardianChange> Changes { get; set; } = [];

    public List<string> Questions { get; set; } = [];

    public int Confidence { get; set; }

    // ---------------------------------------------
    // Suggested UI action
    // ---------------------------------------------

    public GuardianToolLaunch? SuggestedTool { get; set; }

    public List<GuardianToolLaunch> SuggestedTools { get; set; } = [];

    public int ChangeCount =>
    Changes.Count;

    public int TaskCount =>
        Tasks.Count;

    public bool HasChanges =>
        Changes.Count > 0;

    public bool HasTasks =>
        Tasks.Count > 0;
}