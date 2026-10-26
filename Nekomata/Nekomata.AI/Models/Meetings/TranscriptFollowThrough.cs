namespace Nekomata.AI.Models.Meetings;

// What came out of a meeting, from its transcript.
public sealed class TranscriptFollowThrough
{
    public string Summary { get; set; } = "";
    public List<string> Decisions { get; set; } = [];
    public List<FollowThroughAction> UserActions { get; set; } = [];
    public List<FollowThroughAction> OthersActions { get; set; } = [];
    /// <summary>A follow-up email body to the attendees, in the user's voice.</summary>
    public string FollowUpEmail { get; set; } = "";
}

public sealed class FollowThroughAction
{
    /// <summary>Who owns it (first name); empty for the user's own.</summary>
    public string Who { get; set; } = "";
    public string What { get; set; } = "";
    /// <summary>The deadline as said ("Thursday", "end of next week"), or empty.</summary>
    public string When { get; set; } = "";
}
