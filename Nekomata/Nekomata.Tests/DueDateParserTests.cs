using Nekomata.Core.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class DueDateParserTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 10, 0, 0); // Wednesday

    [Theory]
    [InlineData("Friday", "2026-10-16 17:00")]
    [InlineData("by Friday", "2026-10-16 17:00")]
    [InlineData("tomorrow", "2026-10-15 17:00")]
    [InlineData("tomorrow morning", "2026-10-15 10:00")]
    [InlineData("end of day", "2026-10-14 17:00")]
    [InlineData("the end of the week", "2026-10-16 17:00")]
    [InlineData("next week", "2026-10-23 17:00")]
    [InlineData("Monday morning", "2026-10-19 10:00")]
    [InlineData("next Monday", "2026-10-19 17:00")]
    [InlineData("Wednesday", "2026-10-21 17:00")]
    [InlineData("the 20th", "2026-10-20 17:00")]
    [InlineData("the 3rd", "2026-11-03 17:00")]
    [InlineData("20 Oct", "2026-10-20 17:00")]
    [InlineData("5th November", "2026-11-05 17:00")]
    public void Parses(string text, string expected) =>
        Assert.Equal(DateTime.Parse(expected), DueDateParser.Parse(text, Now));

    [Theory]
    [InlineData("")]
    [InlineData("whenever")]
    [InlineData("soon")]
    public void Unknown_IsNull(string text) => Assert.Null(DueDateParser.Parse(text, Now));
}
