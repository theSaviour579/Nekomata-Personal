namespace Nekomata.AI.Models.Actions;

public class ProposedTask
{
    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public string Priority { get; set; } = "Normal";

    public int EstimatedMinutes { get; set; }

    public DateTime? DueAt { get; set; }

    public long? ProjectId { get; set; }

    public decimal EstimatedBusinessValue { get; set; }

    public bool RequiresSql { get; set; }

    public bool RequiresFocus { get; set; }

    public string? SuggestedDelegate { get; set; }

    public bool Selected { get; set; } = true;

    public string ReviewDisposition { get; set; } = "";
    public string FeedbackReason { get; set; } = "";
    public string OriginalTitle { get; set; } = "";
    public string ReplacementInstruction { get; set; } = "";
    public bool FeedbackRecorded { get; set; }
    public bool IsRemoved => ReviewDisposition.Equals("Removed", StringComparison.OrdinalIgnoreCase);
    public bool HasReviewFeedback => !string.IsNullOrWhiteSpace(ReviewDisposition);
    public string ReviewStatusLabel => ReviewDisposition switch
    {
        "Removed" => "REMOVED FROM PROPOSAL",
        "Replaced" => $"REPLACED · originally {OriginalTitle}",
        _ => ""
    };
}
