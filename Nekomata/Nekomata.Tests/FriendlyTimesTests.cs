using Nekomata.Core.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class FriendlyTimesTests
{
    [Fact]
    public void Turns_iso_ranges_into_plain_english()
    {
        var today = new DateTime(2026, 10, 22);
        Assert.Equal("- 04 November: Wed 4 Nov, 09:00–11:00",
            FriendlyTimes.Humanise("- 04 November: 2026-11-04T09:00:00+00:00 to 2026-11-04T11:00:00+00:00", today));
        Assert.Equal("Starts Thu 5 Nov 09:00.", FriendlyTimes.Humanise("Starts 2026-11-05T09:00:00+00:00.", today));
        Assert.Equal("Nothing to change.", FriendlyTimes.Humanise("Nothing to change.", today));
    }
}
