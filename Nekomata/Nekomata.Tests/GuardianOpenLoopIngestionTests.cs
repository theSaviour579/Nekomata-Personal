using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianOpenLoopIngestionTests
{
    [Fact]
    public void Excludes_teams_invitation_boilerplate()
    {
        var body = "________________________________________________________________________________\nMicrosoft Teams meeting\nJoin: https://teams.microsoft.com/meet/123";
        Assert.Equal("", GuardianOpenLoopIngestionService.ExtractAuthoredText(body));
        Assert.False(GuardianOpenLoopIngestionService.IsRequest(GuardianOpenLoopIngestionService.ExtractAuthoredText(body)));
    }

    [Fact]
    public void Ignores_requests_inside_quoted_email_history()
    {
        var body = "I think it was Alex\n\nRegards,\nAlex\n________________________________\nFrom: Craig\nWho set it up?";
        var authored = GuardianOpenLoopIngestionService.ExtractAuthoredText(body);
        Assert.Equal("I think it was Alex\n\nRegards,\nAlex", authored.Replace("\r", ""));
        Assert.False(GuardianOpenLoopIngestionService.IsRequest(authored));
    }

    [Theory]
    [InlineData("Could you please arrange a meeting with me?", true)]
    [InlineData("I have a Techary meeting which clashes with Elaine's meeting", false)]
    [InlineData("Are you an active meeting contributor?", false)]
    public void Teams_requires_direct_scheduling_instruction(string message, bool expected) =>
        Assert.Equal(expected, GuardianOpenLoopIngestionService.RequestsMeeting(message));

    [Theory]
    [InlineData("Could you please confirm the deployment date?", true)]
    [InlineData("Can someone try and check the backup connection please?", true)]
    [InlineData("Do I just send it to you when completed?", true)]
    [InlineData("It's where the ASM has the relationship, if that makes sense?", false)]
    [InlineData("In the meantime, please see attached data, hope it helps!", false)]
    [InlineData("Please don't try to access anything until my next update.", false)]
    public void Email_requires_a_direct_ask(string message, bool expected) =>
        Assert.Equal(expected, GuardianOpenLoopIngestionService.IsRequest(message));

    [Fact]
    public async Task Captures_recipient_and_does_not_reprocess_checkpointed_mail()
    {
        var loops = new FakeOpenLoopRepository();
        var checkpoints = new FakeCheckpointRepository();
        var service = new GuardianOpenLoopIngestionService(new GuardianOpenLoopService(loops), checkpoints);
        var sent = new EmailMessage
        {
            Id = "m1", ConversationId = "c1", Subject = "Deployment", BodyPreview = "Could you confirm the date?",
            ReceivedAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.FromHours(1)),
            ToRecipients = ["Alex Smith"]
        };

        await service.IngestMailAsync([], [sent]);
        await service.IngestMailAsync([], [sent]);

        var captured = Assert.Single(loops.Items);
        Assert.Equal("Alex Smith", captured.WaitingOn);
        Assert.Equal("Could you confirm the date?", captured.Detail);
    }

    private sealed class FakeOpenLoopRepository : IGuardianOpenLoopRepository
    {
        public List<GuardianOpenLoop> Items { get; } = [];
        public Task<GuardianOpenLoop> UpsertAsync(GuardianOpenLoop openLoop) { Items.Add(openLoop); return Task.FromResult(openLoop); }
        public Task<List<GuardianOpenLoop>> GetOpenAsync(int count = 200) => Task.FromResult(Items.Where(x => x.Status == OpenLoopStatus.Open).ToList());
        public Task<List<GuardianOpenLoop>> GetAwaitingAsync(int count = 200) => Task.FromResult(Items.Where(x => x.Type == OpenLoopType.AwaitingThem).ToList());
        public Task<bool> ResolveAsync(Guid id, GuardianLoopEvidence evidence) => Task.FromResult(false);
    }

    private sealed class FakeCheckpointRepository : IGuardianSignalCheckpointRepository
    {
        private readonly Dictionary<string, DateTimeOffset> _values = [];
        public Task<DateTimeOffset?> GetAsync(string source) => Task.FromResult(_values.TryGetValue(source, out var value) ? (DateTimeOffset?)value : null);
        public Task SetAsync(string source, DateTimeOffset observedThrough) { _values[source] = observedThrough; return Task.CompletedTask; }
    }
}
