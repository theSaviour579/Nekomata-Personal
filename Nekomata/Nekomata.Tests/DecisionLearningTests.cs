using Nekomata.Core.Missions;
using Xunit;
namespace Nekomata.Tests;
public class DecisionLearningTests
{
    private static readonly DateTime Today = new(2026,9,4);
    private static DecisionFeedback Feedback(int days, string reason = "Wrong priority") => new() { ObjectiveKey="a", Day=Today.AddDays(-days), Reason=reason };
    [Fact] public void One_dismissal_cannot_propose_preference() => Assert.False(DecisionLearning.CanPropose([Feedback(0)],"a",Today));
    [Fact] public void Repeated_clicks_same_day_do_not_count() => Assert.False(DecisionLearning.CanPropose([Feedback(0),Feedback(0),Feedback(0)],"a",Today));
    [Fact] public void Three_separate_days_can_propose() => Assert.True(DecisionLearning.CanPropose([Feedback(0),Feedback(1),Feedback(2)],"a",Today));
    [Fact] public void Old_and_different_feedback_do_not_create_priority_preferences() => Assert.False(DecisionLearning.CanPropose([Feedback(0),Feedback(31),Feedback(2,"Wrong owner")],"a",Today));
    [Fact] public void Evidence_alone_does_not_change_ranking() => Assert.Equal(0,DecisionLearning.Penalty([],"a",false,DateTimeOffset.Now));
    [Fact] public void Approved_preference_expires_and_never_affects_urgent_work()
    {
        var now=DateTimeOffset.Now;
        LearnedPreference[] preferences=[new(){ObjectiveKey="a",Until=now.AddDays(7)}];
        Assert.Equal(25,DecisionLearning.Penalty(preferences,"a",false,now));
        Assert.Equal(0,DecisionLearning.Penalty(preferences,"a",true,now));
        Assert.Equal(0,DecisionLearning.Penalty(preferences,"a",false,now.AddDays(8)));
        Assert.Equal(0,DecisionLearning.Penalty(preferences,"other",false,now));
    }
}
