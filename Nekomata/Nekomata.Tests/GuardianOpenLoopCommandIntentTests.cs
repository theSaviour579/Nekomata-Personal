using Nekomata.Core.Guardian.OpenLoops;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianOpenLoopCommandIntentTests
{
    [Theory]
    [InlineData("What needs chasing?")]
    [InlineData("Who needs chasing today?")]
    [InlineData("Does anything need chasing?")]
    public void Chase_query_also_requests_preparation(string message) =>
        Assert.True(GuardianOpenLoopCommandIntent.IsPrepareChases(message));

    [Theory]
    [InlineData("Deal with the things I'm waiting for")]
    [InlineData("Deal with what I’m waiting on")]
    [InlineData("Prepare the chasers")]
    [InlineData("Draft follow-ups for the things I am waiting for")]
    public void Recognises_prepare_chase_phrasings(string message) =>
        Assert.True(GuardianOpenLoopCommandIntent.IsPrepareChases(message));

    [Theory]
    [InlineData("What am I waiting for?")]
    [InlineData("Who am I waiting on?")]
    [InlineData("Show my open loops")]
    public void Recognises_waiting_queries(string message) =>
        Assert.True(GuardianOpenLoopCommandIntent.IsWaitingQuery(message));

    [Fact]
    public void Does_not_treat_general_waiting_discussion_as_action_command() =>
        Assert.False(GuardianOpenLoopCommandIntent.IsPrepareChases("Tell me why waiting for suppliers creates risk"));
}
