namespace Nekomata.Core.Guardian.Actions;

public class GuardianAppliedAction
{
    public string Type { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public long? EntityId { get; set; }

    public string Operation { get; set; } = "";
    public string? ExternalId { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public bool Reversible { get; set; }
    public string? IrreversibleReason { get; set; }
    public string Reason { get; set; } = "";
    public int Confidence { get; set; }

    public DateTime AppliedAt { get; set; } =
        DateTime.Now;
}
