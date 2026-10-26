using Nekomata.Core.Guardian.Context;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Xunit;

namespace Nekomata.Tests;

public sealed class EmailReplyContextBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Builds_thread_in_chronological_order_and_identifies_sent_mail()
    {
        var received = Message("2", "supplier@example.com", Now.AddHours(1), "The change is ready.");
        var sent = Message("1", "david@example.com", Now, "Can you update ticket 4698?");

        var result = EmailReplyContextBuilder.Build(received, received.BodyContent, [received, sent], [], "david@example.com", Now);

        Assert.True(result.ConversationEvidence.IndexOf("Can you update", StringComparison.Ordinal) <
                    result.ConversationEvidence.IndexOf("change is ready", StringComparison.Ordinal));
        Assert.Contains("You (sent)", result.ConversationEvidence);
        Assert.Equal(2, result.ConversationMessageCount);
    }

    [Fact]
    public void Shared_participant_confirms_an_existing_booking()
    {
        var message = Message("1", "supplier@example.com", Now, "Shall we discuss the specials routine?");
        var calendar = new CalendarEvent
        {
            Id = "event", Subject = "Specials routine review", Start = Now.AddDays(1), End = Now.AddDays(1).AddMinutes(30),
            Attendees = ["supplier@example.com"]
        };

        var result = EmailReplyContextBuilder.Build(message, message.BodyContent, [message], [calendar], "david@example.com", Now);

        Assert.True(result.HasConfirmedBooking);
        Assert.Contains("CONFIRMED MATCH", result.CalendarEvidence);
        Assert.Contains("shared participant", result.CalendarEvidence);
    }

    [Fact]
    public void Shared_ticket_reference_confirms_booked_work()
    {
        var message = Message("1", "supplier@example.com", Now, "Update requested for ticket #4698.");
        var calendar = new CalendarEvent
        {
            Id = "event", Subject = "Work on #4698", Start = Now.AddDays(1), End = Now.AddDays(1).AddHours(1)
        };

        var result = EmailReplyContextBuilder.Build(message, message.BodyContent, [message], [calendar], "david@example.com", Now);

        Assert.True(result.HasConfirmedBooking);
        Assert.Contains("shared reference 4698", result.CalendarEvidence);
    }

    [Fact]
    public void Unrelated_calendar_entries_are_not_supplied_as_evidence()
    {
        var message = Message("1", "supplier@example.com", Now, "Please review ticket #4698.");
        var calendar = new CalendarEvent
        {
            Id = "event", Subject = "Payroll planning", Start = Now.AddDays(1), End = Now.AddDays(1).AddHours(1),
            Attendees = ["finance@example.com"]
        };

        var result = EmailReplyContextBuilder.Build(message, message.BodyContent, [message], [calendar], "david@example.com", Now);

        Assert.False(result.HasConfirmedBooking);
        Assert.Empty(result.CalendarMatches);
        Assert.Contains("Do not infer", result.CalendarEvidence);
    }

    [Fact]
    public void Own_mailbox_and_calendar_year_do_not_match_every_calendar_entry()
    {
        var message = Message("1", "supplier@example.com", Now, "Can you confirm progress during 2026?");
        var calendar = new CalendarEvent
        {
            Id = "event", Subject = "Unrelated 2026 finance review", Start = Now.AddDays(1), End = Now.AddDays(1).AddHours(1),
            Organiser = "david@example.com", Attendees = ["david@example.com"]
        };

        var result = EmailReplyContextBuilder.Build(message, message.BodyContent, [message], [calendar], "david@example.com", Now);

        Assert.Empty(result.CalendarMatches);
    }

    private static EmailMessage Message(string id, string sender, DateTimeOffset at, string content) => new()
    {
        Id = id, ConversationId = "thread", Subject = "Specials routine #4698", SenderName = sender,
        SenderAddress = sender, ReceivedAt = at, BodyContent = content, BodyPreview = content
    };
}
