namespace Nekomata.Core.Guardian.Actions;

public class GuardianAppliedAction
{
    public string Type { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public long? EntityId { get; set; }

    public DateTime AppliedAt { get; set; } =
        DateTime.Now;
}