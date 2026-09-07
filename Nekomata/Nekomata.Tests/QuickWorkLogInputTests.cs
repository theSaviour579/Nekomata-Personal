using Nekomata.Core.Guardian.Outcomes;
using Xunit;

namespace Nekomata.Tests;

public sealed class QuickWorkLogInputTests
{
    [Fact]
    public void ComputesEndFromDuration()
    {
        var now=new DateTime(2026,9,3,16,0,0);
        Assert.True(RetrospectiveObjectiveInput.TryParseDurationToday("14:30","45",now,out var period,out _));
        Assert.Equal(new DateTime(2026,9,3,15,15,0),period!.FinishedAt);
    }

    [Theory]
    [InlineData("14:30","0")]
    [InlineData("14:30","-10")]
    [InlineData("14:30","abc")]
    [InlineData("14:30","999999")]
    [InlineData("14:30","2.5")]
    [InlineData("25:00","15")]
    [InlineData("15:45","60")]
    [InlineData("23:30","90")]
    public void RejectsInvalidOrFutureWork(string start,string minutes)
    {
        Assert.False(RetrospectiveObjectiveInput.TryParseDurationToday(start,minutes,new DateTime(2026,9,3,16,0,0),out _,out var error));
        Assert.NotEmpty(error);
    }
}
