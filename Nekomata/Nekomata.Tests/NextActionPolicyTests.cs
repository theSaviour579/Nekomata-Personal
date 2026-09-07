using Nekomata.Core.Missions;
using Nekomata.Models.Missions;
using Xunit;
namespace Nekomata.Tests;
public class NextActionPolicyTests
{
    private static MissionCandidate Item(string id, int score = 10) => new() { SourceType = "Halo", SourceRecordId = id, Title = id, EstimatedMinutes = 20, Score = score };
    [Fact] public void Highest_ranked_fitting_item_is_first()
    {
        var large = Item("large",100); large.EstimatedMinutes = 60;
        Assert.Equal("best", NextActionPolicy.Select([Item("low"), Item("best",50),large],30,"Me",new HashSet<string>())[0].Title);
    }
    [Fact] public void Urgent_fitting_work_precedes_score()
    {
        var urgent = Item("urgent"); urgent.IsP1=true;
        Assert.Equal("urgent", NextActionPolicy.Select([Item("high",100),urgent],30,"Me",new HashSet<string>())[0].Title);
    }
    [Fact] public void Waiting_and_blocked_work_are_excluded()
    {
        var waiting=Item("waiting"); waiting.IsAwaitingExternalResponse=true;
        var hold=Item("hold"); hold.IsOnHold=true;
        var blocked=Item("blocked"); blocked.IsActionable=false;
        Assert.Empty(NextActionPolicy.Select([waiting,hold,blocked],30,"Me",new HashSet<string>()));
    }
    [Fact] public void Conflicting_ownership_is_excluded()
    {
        var owned=Item("owned"); owned.CurrentOwner="Someone else";
        var delegateWork=Item("delegate"); delegateWork.SuggestedOwner="Someone else";
        Assert.Empty(NextActionPolicy.Select([owned,delegateWork],30,"Me",new HashSet<string>()));
    }
    [Fact] public void Dismissed_identity_is_not_repeated()
    {
        var item=Item("dismissed");
        Assert.Empty(NextActionPolicy.Select([item],30,"Me",new HashSet<string>{NextActionPolicy.Key(item)}));
    }
    [Fact] public void Unknown_duration_and_short_gaps_are_not_suggested()
    {
        var item=Item("unknown"); item.EstimatedMinutes=0;
        Assert.Empty(NextActionPolicy.Select([item],30,"Me",new HashSet<string>()));
        Assert.Empty(NextActionPolicy.Select([Item("normal")],5,"Me",new HashSet<string>()));
    }
    [Fact] public void Time_spent_is_not_completion_evidence()
    {
        var item=Item("overrun"); item.Progress=1;
        Assert.Single(NextActionPolicy.Select([item],30,"Me",new HashSet<string>()));
    }
}
