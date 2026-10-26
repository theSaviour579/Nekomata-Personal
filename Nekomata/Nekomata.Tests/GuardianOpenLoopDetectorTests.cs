using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianOpenLoopDetectorTests
{
    [Theory]
    [InlineData(GuardianOpenLoopSignalKind.Email, true, false, 0, OpenLoopType.AwaitingThem)]
    [InlineData(GuardianOpenLoopSignalKind.Meeting, false, true, 0, OpenLoopType.AwaitingMe)]
    public void Detects_structured_open_loop_signals(
        GuardianOpenLoopSignalKind kind, bool request, bool prep, int age, OpenLoopType expected)
    {
        var result = GuardianOpenLoopDetector.Detect(new GuardianOpenLoopSignal
        {
            Kind = kind, Title = "Loose end", IsOutbound = request, ContainsQuestionOrRequest = request,
            PreparationRequired = prep, AgeDays = age,
            EventId = Guid.NewGuid()
        });

        Assert.NotNull(result);
        Assert.Equal(expected, result.Type);
        Assert.NotEqual(Guid.Empty, result.SourceEventId);
    }

    [Fact]
    public void Detects_request_to_arrange_time_as_meeting_required()
    {
        var result = GuardianOpenLoopDetector.Detect(new GuardianOpenLoopSignal
        {
            Kind = GuardianOpenLoopSignalKind.Message, Title = "Arrange time", RequestsMeetingArrangement = true,
            TeamsMessageId = "message-1", EventId = Guid.NewGuid()
        });

        Assert.Equal(OpenLoopType.MeetingRequired, result?.Type);
        Assert.Equal("MeetingScheduled", result?.ExpectedEvidenceKind);
    }
}
