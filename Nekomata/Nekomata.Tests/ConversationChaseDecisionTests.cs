using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Xunit;

namespace Nekomata.Tests;

public class ConversationChaseDecisionTests
{
    [Fact]
    public void Receipt_classification_does_not_require_canned_wording()
    {
        var now = DateTimeOffset.Now;
        var result = ConversationChaseDecision.Evaluate(
            [Message("me", now.AddDays(-10)), Message("supplier", now, "Thank you for contacting Wida. Your reference is ABC123.")],
            "me", new() { AcknowledgementMessageIds = ["supplier"] }, now);
        Assert.Equal("Chase", result.Action);
    }

    [Fact]
    public void Inconsistent_acknowledgement_analysis_requests_review()
    {
        var now = DateTimeOffset.Now;
        var result = ConversationChaseDecision.Evaluate([Message("supplier", now)], "me",
            new() { Summary = "No substantive response has been received." }, now);
        Assert.Equal("Review", result.Action);
        Assert.Empty(result.Draft);
    }
    [Fact]
    public void Sent_message_clears_due_chase_then_new_reply_requires_action()
    {
        var now = DateTimeOffset.Now;
        var original = Message("me", now.AddDays(-10));
        Assert.Equal("Chase", ConversationChaseDecision.Evaluate([original], "me", new(), now).Action);
        var sent = Message("me", now);
        var waiting = ConversationChaseDecision.Evaluate([original, sent], "me", new(), now);
        Assert.Equal("Wait", waiting.Action);
        Assert.NotNull(waiting.WaitUntil);
        var reply = Message("supplier", now.AddMinutes(1), "Please provide the error logs.");
        var responding = ConversationChaseDecision.Evaluate([original, sent, reply], "me", new(), now.AddMinutes(2));
        Assert.Equal("Respond", responding.Action);
        Assert.Null(responding.WaitUntil);
    }

    [Fact]
    public void Unchanged_conversation_becomes_due_when_wait_expires()
    {
        var sentAt = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);
        var messages = new[] { Message("me", sentAt) };
        Assert.Equal("Wait", ConversationChaseDecision.Evaluate(messages, "me", new(), sentAt).Action);
        Assert.Equal("Chase", ConversationChaseDecision.Evaluate(messages, "me", new(), sentAt.AddDays(3)).Action);
    }
    [Fact]
    public void Automatic_receipt_does_not_reset_overdue_chase_or_invent_retesting()
    {
        var now = DateTimeOffset.Now;
        var result = ConversationChaseDecision.Evaluate(
            [Message("me", now.AddDays(-10), "Please see the original request"), Message("supplier", now, "This is an automatic acknowledgement. We received your request.")],
            "me", new() { AcknowledgementMessageIds = ["supplier"], SuggestedReply = "We're still seeing the fault after retesting." }, now);
        Assert.Equal("Chase", result.Action);
        Assert.Null(result.WaitUntil);
        Assert.DoesNotContain("still seeing", result.Draft);
        Assert.DoesNotContain("retesting", result.Draft);
    }

    [Fact]
    public void Acknowledgement_with_question_is_not_ignored()
    {
        var now = DateTimeOffset.Now;
        var result = ConversationChaseDecision.Evaluate(
            [Message("me", now.AddDays(-10)), Message("supplier", now, "We received your request. Can you send the logs?")],
            "me", new() { AcknowledgementMessageIds = ["supplier"] }, now);
        Assert.Equal("Respond", result.Action);
    }

    [Fact]
    public void Forwarded_original_request_is_not_a_verified_chase()
    {
        var now = DateTimeOffset.Now;
        const string request = "Please see below ticket that has come in this morning";
        var result = ConversationChaseDecision.Evaluate([Message("me", now.AddDays(-10), request)], "me",
            new() { LastChaseMessageId = "me", ChaseQuote = request }, now);
        Assert.Contains("No verified chase", result.Evidence);
    }
    private static EmailMessage Message(string sender, DateTimeOffset at, string content = "") =>
        new() { Id = sender, SenderAddress = sender, ReceivedAt = at, BodyContent = content };

    [Fact]
    public void New_inbound_reply_requires_response_not_chase()
    {
        var now = DateTimeOffset.Now;
        var result = ConversationChaseDecision.Evaluate([Message("me", now.AddDays(-4)), Message("supplier", now)], "me", new(), now);
        Assert.Equal("Respond", result.Action);
        Assert.Null(result.WaitUntil);
    }

    [Fact]
    public void Recent_outbound_waits_two_workdays_over_weekend()
    {
        var friday = new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
        var result = ConversationChaseDecision.Evaluate([Message("me", friday)], "me", new(), friday.AddHours(1));
        Assert.Equal("Wait", result.Action);
        Assert.Equal(DayOfWeek.Tuesday, result.WaitUntil!.Value.DayOfWeek);
    }

    [Fact]
    public void Unsupported_ai_promise_cannot_suppress_reply()
    {
        var now = DateTimeOffset.Now;
        var advice = new ChaseConversationAdvice { PromiseMessageId = "supplier", PromiseQuote = "I promise to fix it", PromisedDate = now.AddDays(3).ToString("yyyy-MM-dd") };
        var result = ConversationChaseDecision.Evaluate([Message("supplier", now, "Please send more information")], "me", advice, now);
        Assert.Equal("Respond", result.Action);
        Assert.Null(result.WaitUntil);
    }

    [Fact]
    public void Unsupported_chase_quote_is_not_reported_as_evidence()
    {
        var now = DateTimeOffset.Now;
        var result = ConversationChaseDecision.Evaluate([Message("me", now.AddDays(-8), "Original request")], "me",
            new() { LastChaseMessageId = "me", ChaseQuote = "Just chasing this again" }, now);
        Assert.Equal("Chase", result.Action);
        Assert.Contains("No verified chase", result.Evidence);
    }
}
