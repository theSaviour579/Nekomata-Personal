namespace Nekomata.Models.Guardian;

public sealed class GuardianAuditEntry
{
    public long Id { get; set; }
    public Guid BatchId { get; set; }
    public string Operation { get; set; } = "";
    public string EntityType { get; set; } = "";
    public long? EntityId { get; set; }
    public string? ExternalId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Reason { get; set; } = "";
    public int Confidence { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public bool Reversible { get; set; }
    public string? IrreversibleReason { get; set; }
    public string Status { get; set; } = "Applied";
    public DateTime AppliedAt { get; set; }
    public DateTime? UndoneAt { get; set; }
    public string? UndoMessage { get; set; }

    public bool CanUndo => Reversible && Status.Equals("Applied", StringComparison.OrdinalIgnoreCase);
    public string ConfidenceLabel => $"{Confidence}% confidence";
    public string BatchLabel => BatchId.ToString("N")[..8].ToUpperInvariant();
}
