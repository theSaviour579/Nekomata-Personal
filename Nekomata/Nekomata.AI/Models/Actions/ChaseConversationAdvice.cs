namespace Nekomata.AI.Models.Actions;

public sealed class ChaseConversationAdvice
{
    public List<string> AcknowledgementMessageIds { get; set; } = [];
    public string Summary { get; set; } = "";
    public string LastChaseMessageId { get; set; } = "";
    public string ChaseQuote { get; set; } = "";
    public string PromiseMessageId { get; set; } = "";
    public string PromiseQuote { get; set; } = "";
    public string PromisedDate { get; set; } = "";
    public string SuggestedReply { get; set; } = "";
}
