using Nekomata.Core.Guardian.Briefing;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianContextBriefingServiceTests
{
    private readonly GuardianContextBriefingService _service = new();

    [Fact]
    public void Unloaded_area_requests_evidence()
    {
        var result = _service.Build(new() { Area = "Inbox" });

        Assert.Equal("AWAITING EVIDENCE", result.StateLabel);
        Assert.False(result.HasAction);
    }

    [Fact]
    public void Actionable_signal_leads_context_over_higher_scored_information()
    {
        var result = _service.Build(new()
        {
            Area = "Tasks", IsLoaded = true,
            Signals =
            [
                new() { Title = "Informational change", Significance = 95 },
                new() { Title = "Chase supplier", Evidence = "Three working days without reply.", RecommendedAction = "OPEN TICKET", Significance = 70, IsActionable = true, ContextId = "42" }
            ]
        });

        Assert.Equal("Chase supplier", result.Headline);
        Assert.True(result.HasAction);
        Assert.Equal("42", result.ContextId);
        Assert.Contains("1 ACTIONABLE", result.StateLabel);
    }

    [Fact]
    public void Empty_loaded_area_is_all_clear()
    {
        var result = _service.Build(new() { Area = "Calendar", IsLoaded = true });

        Assert.Equal("ALL CLEAR", result.StateLabel);
        Assert.Contains("Nothing", result.Headline);
    }

    [Fact]
    public void Refreshing_state_does_not_offer_stale_action()
    {
        var result = _service.Build(new()
        {
            Area = "Inbox", IsLoaded = true, IsBusy = true,
            Signals = [new() { Title = "Urgent", IsActionable = true, Significance = 100 }]
        });

        Assert.Equal("REFRESHING", result.StateLabel);
        Assert.False(result.HasAction);
    }
}
