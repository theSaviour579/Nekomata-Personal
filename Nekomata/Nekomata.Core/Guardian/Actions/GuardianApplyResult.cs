namespace Nekomata.Core.Guardian.Actions;

public class GuardianApplyResult
{
    public bool Success { get; set; }
    public int TasksCreated { get; set; }
    public List<long> CreatedTaskIds { get; set; } = [];
    public List<string> Messages { get; set; } = [];
    public List<GuardianAppliedAction> Actions { get; set; } = [];

    public string Summary => Success
        ? $"{Actions.Count} action{(Actions.Count == 1 ? "" : "s")} applied."
        : "Guardian could not apply the selected proposal.";

    public string UserSummary
    {
        get
        {
            var lines = Actions.Select(action => $"✓ {action.Title}")
                .Concat(Messages)
                .ToList();
            return lines.Count == 0 ? Summary : string.Join(Environment.NewLine, lines);
        }
    }
}
