using Nekomata.Core.Guardian.Anticipation;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianAnticipationCommandIntentTests
{
    [Theory]
    [InlineData("What are you anticipating? ")]
    [InlineData("What do you think I need next?")]
    [InlineData("Show anticipated actions")]
    public void Recognises_chat_queue_queries(string message) =>
        Assert.True(GuardianAnticipationCommandIntent.IsQueueQuery(message));

    [Fact]
    public void Does_not_capture_general_prediction_questions() =>
        Assert.False(GuardianAnticipationCommandIntent.IsQueueQuery("What happens next in the industry?"));
}
