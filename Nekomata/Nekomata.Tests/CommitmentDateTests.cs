using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.AI.Models.Actions;
using Xunit;

namespace Nekomata.Tests;
public class CommitmentDateTests
{
    private static readonly DateTime Monday = new(2026, 9, 7);
    [Theory]
    [InlineData("I'll send it tomorrow", 8)]
    [InlineData("I'll send it today", 7)]
    [InlineData("We'll deliver by Friday", 11)]
    [InlineData("We'll deliver on Friday", 11)]
    [InlineData("We'll deliver this Friday", 11)]
    [InlineData("We'll deliver on 2026-09-11", 11)]
    public void Resolves_against_message_date(string quote, int day) => Assert.Equal(new DateTime(2026, 9, day), CommitmentDate.FromQuote(quote, Monday));
    [Theory]
    [InlineData("Hopefully by Friday")]
    [InlineData("We'll send it next Friday")]
    [InlineData("We'll send tomorrow if approved")]
    [InlineData("We can deliver Monday or Friday")]
    [InlineData("We'll send tomorrow or on Friday")]
    [InlineData("We'll send by 2026-09-06")]
    [InlineData("We cannot deliver by Friday")]
    [InlineData("We'll send by Friday or later")]
    public void Ambiguity_cannot_suppress_attention(string quote) => Assert.Null(CommitmentDate.FromQuote(quote, Monday));
    [Fact] public void Friday_commitment_followup_is_Monday() => Assert.Equal(new DateTime(2026,9,14,8,0,0), CommitmentDate.FollowUpAt(new(2026,9,11)).LocalDateTime);

    private static EmailMessage Promise(string quote) => new() { Id = "promise", SenderAddress = "supplier", Subject = "Delivery", ReceivedAt = new DateTimeOffset(DateTime.SpecifyKind(Monday.AddHours(10), DateTimeKind.Local)), BodyContent = quote };
    private static ChaseConversationAdvice Advice(string quote) => new() { PromiseMessageId = "promise", PromiseQuote = quote };
    [Fact] public void Promise_expires_into_contextual_chase()
    {
        const string quote = "We'll send the report by Friday";
        var promise = Promise(quote);
        Assert.Equal("Wait", ConversationChaseDecision.Evaluate([promise], "me", Advice(quote), promise.ReceivedAt.AddDays(1)).Action);
        var overdue = ConversationChaseDecision.Evaluate([promise], "me", Advice(quote), promise.ReceivedAt.AddDays(7));
        Assert.Equal("Chase", overdue.Action);
        Assert.Contains(quote, overdue.Draft);
        Assert.Contains("if this is still outstanding", overdue.Draft);
    }
    [Fact] public void Later_receipt_preserves_promise_but_substantive_reply_supersedes_it()
    {
        const string quote = "We'll send the report by Friday";
        var promise = Promise(quote);
        var advice = Advice(quote); advice.AcknowledgementMessageIds = ["receipt"];
        var receipt = new EmailMessage { Id = "receipt", SenderAddress = "supplier", ReceivedAt = promise.ReceivedAt.AddHours(1), BodyContent = "Automatic receipt" };
        Assert.Equal("Wait", ConversationChaseDecision.Evaluate([promise, receipt], "me", advice, receipt.ReceivedAt).Action);
        var response = new EmailMessage { Id = "reply", SenderAddress = "supplier", ReceivedAt = receipt.ReceivedAt.AddHours(1), BodyContent = "The report is attached." };
        Assert.Equal("Respond", ConversationChaseDecision.Evaluate([promise, receipt, response], "me", advice, response.ReceivedAt).Action);
    }
    [Fact] public void Vague_commitment_asks_for_date()
    {
        const string quote = "We'll send the report next week";
        var promise = Promise(quote);
        var decision = ConversationChaseDecision.Evaluate([promise], "me", Advice(quote), promise.ReceivedAt);
        Assert.Equal("Review", decision.Action);
        Assert.Contains("What date", decision.Evidence);
        Assert.Null(decision.WaitUntil);
    }
}
