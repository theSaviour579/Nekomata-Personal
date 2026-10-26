using Nekomata.Core.Guardian;
using Nekomata.Core.Guardian.Commands;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianJudgementTests
{
    [Fact]
    public void Says_no_and_leave_it_when_nothing_needs_the_user()
    {
        var answer = GuardianJudgement.AnythingToWorryAbout(new SituationFacts
        {
            MeetingsWithoutPrep = ["the ops review at 14:00"]
        });
        Assert.True(answer.AllClear);
        Assert.StartsWith("No. Nothing critical, nothing overdue that you owe, and no overdue tasks. I'd leave things alone.", answer.Spoken);
        Assert.Contains("half an eye on: the ops review at 14:00", answer.Spoken);
        Assert.Contains("ANYTHING TO WORRY ABOUT? NO", answer.Written);
    }

    [Fact]
    public void Admits_what_it_cannot_see()
    {
        var answer = GuardianJudgement.AnythingToWorryAbout(new SituationFacts { CalendarKnown = false, MailKnown = false });
        Assert.True(answer.AllClear);
        Assert.StartsWith("Not as far as I can see.", answer.Spoken);
        Assert.Contains("can't see your calendar or your email right now", answer.Spoken);
        Assert.Contains("Couldn't see: your calendar, your email", answer.Written);
    }

    [Fact]
    public void Ranks_what_matters_and_says_what_can_wait()
    {
        var answer = GuardianJudgement.AnythingToWorryAbout(new SituationFacts
        {
            NeedsYou = [("Critical", "the renewal has to be signed today")],
            OverduePromises = ["send Sam the reporting data"],
            ApprovalsWaiting = 2,
            MeetingsWithoutPrep = ["the ops review at 14:00"]
        });
        Assert.False(answer.AllClear);
        Assert.StartsWith("Two things. The renewal has to be signed today. Then your promise to send Sam the reporting data is overdue.", answer.Spoken);
        Assert.Contains("Everything else can wait.", answer.Spoken);
        Assert.Contains("CAN WAIT", answer.Written);
        Assert.Contains("NOW · The renewal has to be signed today", answer.Written);
    }

    [Fact]
    public void Failing_background_jobs_are_raised_so_silence_is_never_mistaken_for_all_clear()
    {
        var answer = GuardianJudgement.AnythingToWorryAbout(new SituationFacts { FailingJobs = ["email check"] });
        Assert.False(answer.AllClear);
        Assert.Contains("my email check isn't running properly", answer.Spoken);
    }

    [Fact]
    public void Scorecard_counts_judgement_calls_and_all_clears_that_were_wrong()
    {
        var at = new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(1));
        Nekomata.Models.Guardian.WorkdayEvent E(string type, string outcome, int hours) =>
            new() { EventType = type, Outcome = outcome, Category = "Guardian", OccurredAt = at.AddHours(hours), Narrative = type };
        var text = GuardianScorecard.Build(new ScorecardInputs(
            [E("GuardianChallenge", "Raised", 0), E("ChallengeResponse", "Accepted", 0), E("GuardianChallenge", "Raised", 1), E("ChallengeResponse", "Overridden", 1),
             E("AllClearAsked", "AllClear", 2), E("GuardianMissed", "Missed", 4), E("AllClearAsked", "AllClear", 30)],
            0, 0, []), at.AddDays(-1), at.AddDays(3));
        Assert.Contains("Pushed back 2 times when you took work on · you took the advice 1, kept it 1", text);
        Assert.Contains("Said nothing needed you 2 times · 1 of those, you caught something within hours", text);
    }

    [Theory]
    [InlineData("Anything I need to worry about?", GuardianCommandIntent.AnythingToWorry, "")]
    [InlineData("should I be worried about anything", GuardianCommandIntent.AnythingToWorry, "")]
    [InlineData("can I relax", GuardianCommandIntent.AnythingToWorry, "")]
    public void Parses(string text, GuardianCommandIntent intent, string argument)
    {
        var m = GuardianCommandParser.Parse(text);
        Assert.Equal(intent, m.Intent);
        Assert.Equal(argument, m.Argument);
    }
}
