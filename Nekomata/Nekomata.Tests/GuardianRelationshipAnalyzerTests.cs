using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Guardian.Relationships;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Guardian;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Context;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianRelationshipAnalyzerTests
{
    [Fact]
    public void Conversation_is_reduced_to_aggregate_contact_evidence()
    {
        var start = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var messages = new[]
        {
            Message("outgoing", "me@example.com", start, "Could you provide an update?"),
            Message("receipt", "supplier@example.com", start.AddMinutes(2), "Automatic receipt"),
            Message("reply", "supplier@example.com", start.AddHours(3), "The change is ready for review.")
        };
        var advice = new ChaseConversationAdvice
        {
            AcknowledgementMessageIds = ["receipt"],
            LastChaseMessageId = "outgoing"
        };

        var result = Assert.Single(GuardianRelationshipAnalyzer.Analyse(messages, "me@example.com", advice));

        Assert.Equal("supplier@example.com", result.ContactAddress);
        Assert.Equal(2, result.MessageCount);
        Assert.Equal(1, result.AcknowledgementCount);
        Assert.Equal(1, result.SubstantiveReplyCount);
        Assert.Equal(1, result.ResponseCount);
        Assert.Equal(180, result.TotalResponseMinutes);
        Assert.Equal(1, result.ChaseResponseCount);
    }

    [Fact]
    public void Established_slow_response_pattern_extends_routine_chase_window()
    {
        var monday = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);
        var profile = new GuardianRelationshipProfile
        {
            ContactAddress="supplier@example.com",ConversationCount=3,ResponseCount=4,
            TotalResponseMinutes=4 * 3 * 1440
        };

        var ordinary = ConversationChaseDecision.Evaluate(
            [Message("sent", "me@example.com", monday, "Original request")],
            "me@example.com", new(), monday.AddDays(3));
        var learned = ConversationChaseDecision.Evaluate(
            [Message("sent", "me@example.com", monday, "Original request")],
            "me@example.com", new(), monday.AddDays(3), profile);

        Assert.Equal("Chase", ordinary.Action);
        Assert.Equal("Wait", learned.Action);
        Assert.Contains("Relationship evidence", learned.Evidence);
        Assert.Equal(DayOfWeek.Friday, learned.WaitUntil!.Value.DayOfWeek);
    }

    [Fact]
    public void Sparse_history_never_changes_default_chase_timing()
    {
        var profile = new GuardianRelationshipProfile
        {
            ContactAddress="supplier@example.com",ConversationCount=1,ResponseCount=1,
            TotalResponseMinutes=7 * 1440
        };

        Assert.False(profile.HasEnoughEvidence);
        Assert.Equal(2, profile.RecommendedWorkingDayWait);
    }

    [Fact]
    public async Task Recent_mail_is_observed_incrementally_without_storing_content()
    {
        var repository = new FakeRelationshipRepository();
        var service = new GuardianRelationshipIntelligenceService(repository);
        var start = new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);
        var sent = Message("sent", "me@example.com", start, "Could you review this?");
        var reply = Message("reply", "alex@example.com", start.AddHours(2), "Reviewed and approved.");

        var result = await service.ObserveRecentMailAsync([reply], [sent], "me@example.com", start.AddHours(3));

        var observation = Assert.Single(repository.Observations);
        Assert.Equal("Email", observation.Channel);
        Assert.Equal(50, observation.EvidenceQuality);
        Assert.Equal(120, observation.TotalResponseMinutes);
        Assert.Equal(1, result.ConversationsObserved);
        Assert.DoesNotContain("Reviewed and approved", System.Text.Json.JsonSerializer.Serialize(observation));
    }

    [Fact]
    public async Task Teams_response_patterns_are_kept_separate_from_email_identity()
    {
        var repository = new FakeRelationshipRepository();
        var service = new GuardianRelationshipIntelligenceService(repository);
        var start = new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

        await service.ObserveTeamsAsync([
            new GraphChatMessage { Id="mine",ChatId="chat",Sender="Alex",Body="Can you check this?",CreatedAt=start,IsFromCurrentUser=true },
            new GraphChatMessage { Id="theirs",ChatId="chat",Sender="Alex Parry",Body="Done",CreatedAt=start.AddMinutes(20) }
        ]);

        var observation = Assert.Single(repository.Observations);
        Assert.Equal("teams:alex-parry", observation.ContactAddress);
        Assert.Equal("Teams", observation.Channel);
        Assert.Equal(20, observation.TotalResponseMinutes);
    }

    [Fact]
    public async Task Courtesy_email_does_not_create_an_unusual_delay()
    {
        var repository = new FakeRelationshipRepository
        {
            Profile = new GuardianRelationshipProfile
            {
                ContactAddress="alex@example.com",ContactName="Alex",ConversationCount=4,
                ResponseCount=5,TotalResponseMinutes=5*60
            }
        };
        var service = new GuardianRelationshipIntelligenceService(repository);
        var start = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var inbound = Message("in", "alex@example.com", start, "The work is complete.");
        var thanks = Message("out", "me@example.com", start.AddMinutes(5), "Thanks, that is perfect.");

        var result = await service.ObserveRecentMailAsync([inbound], [thanks], "me@example.com", start.AddDays(10));

        Assert.Empty(result.UnusualDelays);
    }

    private static EmailMessage Message(string id, string sender, DateTimeOffset at, string content) => new()
    {
        Id=id,SenderAddress=sender,SenderName=sender,ReceivedAt=at,BodyContent=content,
        ConversationId="conversation-1",Subject="Project update"
    };

    private sealed class FakeRelationshipRepository : IGuardianRelationshipRepository
    {
        public List<GuardianRelationshipObservation> Observations { get; } = [];
        public GuardianRelationshipProfile? Profile { get; init; }
        public Task UpsertObservationAsync(GuardianRelationshipObservation observation)
        {
            Observations.RemoveAll(x=>x.ContactAddress==observation.ContactAddress&&x.ConversationId==observation.ConversationId);
            Observations.Add(observation); return Task.CompletedTask;
        }
        public Task<GuardianRelationshipProfile?> GetProfileAsync(string contactAddress) => Task.FromResult(Profile);
        public Task<IReadOnlyList<GuardianRelationshipProfile>> GetProfilesAsync(int count = 100) => Task.FromResult<IReadOnlyList<GuardianRelationshipProfile>>([]);
        public Task SetExcludedAsync(string contactAddress,string displayName,bool excluded,string note="") => Task.CompletedTask;
    }
}
