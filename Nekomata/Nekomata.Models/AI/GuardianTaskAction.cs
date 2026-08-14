namespace Nekomata.Models.AI;

public class GuardianTaskAction
{
    public string ActionType { get; set; } = "";
    // Add, Update, Complete, Reopen, Delete

    public long? TaskId { get; set; }

    public long? ProjectId { get; set; }

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public string? Status { get; set; }

    public string? Priority { get; set; }

    public string? Owner { get; set; }

    public int? EstimatedMinutes { get; set; }

    public DateTime? DueAt { get; set; }

    public decimal? EstimatedBusinessValue { get; set; }

    public string? Category { get; set; }

    public string? Reason { get; set; }

    public int Confidence { get; set; }

    public List<string> ConfidenceReasons { get; set; }
        = [];

    public bool NeedsReview =>
        Confidence < 80;

    public bool Selected { get; set; } = true;

    public bool RequiresConfirmation { get; set; } =
        true;
}