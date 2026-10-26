namespace Nekomata.AI.Models.Email;

// Which incoming emails genuinely need the user to write back - used to prepare reply drafts in advance.
public sealed class ReplyTriage
{
    public List<ReplyTriageItem> Items { get; set; } = [];
}

public sealed class ReplyTriageItem
{
    /// <summary>The number the email was given in the prompt.</summary>
    public int Number { get; set; }
    public bool NeedsReply { get; set; }
    /// <summary>What the reply should do, in a few words ("confirm Thursday works", "answer whether the budget covers it").</summary>
    public string Gist { get; set; } = "";
}
