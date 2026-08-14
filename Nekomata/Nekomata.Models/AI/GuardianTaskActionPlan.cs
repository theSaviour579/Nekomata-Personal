namespace Nekomata.Models.AI;

public class GuardianTaskActionPlan
{
    public string Summary { get; set; } = "";

    public List<GuardianTaskAction> Actions { get; set; }
        = [];

    public List<string> Questions { get; set; }
        = [];

    public bool CanExecute =>
        Actions.Count > 0 &&
        Questions.Count == 0;
}