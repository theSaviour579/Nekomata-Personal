using Nekomata.Core.Guardian;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianScorecardTests
{
    private static readonly DateTimeOffset To = new(2026, 10, 23, 17, 0, 0, TimeSpan.FromHours(1));
    private static WorkdayEvent E(string type, string category = "Monitoring", string outcome = "", string text = "", int daysAgo = 1) =>
        new() { EventType = type, Category = category, Outcome = outcome, Narrative = text, OccurredAt = To.AddDays(-daysAgo) };

    [Fact]
    public void Reports_useful_noisy_missed_and_forecast()
    {
        var events = new List<WorkdayEvent>
        {
            E("AttentionRequired"), E("AttentionRequired"), E("AttentionRequired"), E("AttentionRequired", "Duties"),
            E("AttentionResolved", outcome: "Dismissed"), E("AttentionResolved", outcome: "Dismissed"),
            E("AttentionOpened", "Duties"),
            E("GuardianMissed", "Guardian", text: "The deadline moved on Tuesday and I only heard from Sam"),
            E("ActionConfirmed", "Guardian", "Done", "log a ticket"), E("ActionConfirmed", "Guardian", "Failed", "book focus time: calendar refused"),
            E("AttentionRequired", daysAgo: 20) // outside the week
        };
        var text = GuardianScorecard.Build(new ScorecardInputs(events, 5, 1, ["the review moved to March, not May"]), To.AddDays(-7), To);

        Assert.Contains("25% of the 4 Needs-you items were opened; 50% were dismissed", text);
        Assert.Contains("1 thing you had to catch yourself", text);
        Assert.Contains("Mostly noise: Monitoring (2 of 3 dismissed)", text);
        Assert.Contains("The deadline moved on Tuesday", text);
        Assert.Contains("2 confirmed · 1 failed", text);
        Assert.Contains("Corrected: the review moved", text);
    }

    [Fact]
    public void Says_when_there_is_no_evidence()
    {
        var text = GuardianScorecard.Build(new ScorecardInputs([], 0, 0, []), To.AddDays(-7), To);
        Assert.Contains("Not enough evidence yet", text);
    }
}

public sealed class ScorecardCommandTests
{
    [Theory]
    [InlineData("how is Guardian doing?", Nekomata.Core.Guardian.Commands.GuardianCommandIntent.Scorecard, "week")]
    [InlineData("how have you done this month", Nekomata.Core.Guardian.Commands.GuardianCommandIntent.Scorecard, "month")]
    [InlineData("you should have caught that: the backup failed on Tuesday", Nekomata.Core.Guardian.Commands.GuardianCommandIntent.MissedIssue, "the backup failed on Tuesday")]
    [InlineData("You missed something - Phil's email about the budget", Nekomata.Core.Guardian.Commands.GuardianCommandIntent.MissedIssue, "Phil's email about the budget")]
    [InlineData("what did I miss?", Nekomata.Core.Guardian.Commands.GuardianCommandIntent.WhatDidIMiss, "")]
    [InlineData("What have you sent to the AI?", Nekomata.Core.Guardian.Commands.GuardianCommandIntent.AiPrivacyReport, "")]
    [InlineData("ai log", Nekomata.Core.Guardian.Commands.GuardianCommandIntent.AiPrivacyReport, "")]
    public void Parses(string text, Nekomata.Core.Guardian.Commands.GuardianCommandIntent intent, string argument)
    {
        var m = Nekomata.Core.Guardian.Commands.GuardianCommandParser.Parse(text);
        Assert.Equal(intent, m.Intent);
        Assert.Equal(argument, m.Argument);
    }
}
