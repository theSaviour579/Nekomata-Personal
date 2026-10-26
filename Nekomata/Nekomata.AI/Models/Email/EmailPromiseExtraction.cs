namespace Nekomata.AI.Models.Email;

// Commitments the user made in his own sent emails, extracted for "what did I promise today?" and for turning
// promises into tasks automatically.
public sealed class EmailPromiseExtraction
{
    public List<EmailPromise> Promises { get; set; } = [];
    public string Reasoning { get; set; } = "";
}

public sealed class EmailPromise
{
    /// <summary>First name of the person it was promised to.</summary>
    public string To { get; set; } = "";
    /// <summary>The action, phrased as a task title ("Send Sam the PP reporting data").</summary>
    public string What { get; set; } = "";
    /// <summary>Local "yyyy-MM-ddTHH:mm" when the user stated or clearly implied a time, otherwise empty.</summary>
    public string Due { get; set; } = "";
    public int EstimatedMinutes { get; set; }
    /// <summary>Subject of the email it came from.</summary>
    public string Subject { get; set; } = "";
}
