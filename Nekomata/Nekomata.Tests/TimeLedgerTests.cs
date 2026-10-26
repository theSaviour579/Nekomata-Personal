using Nekomata.Core.Planning;
using Xunit;

namespace Nekomata.Tests;

public sealed class TimeLedgerTests
{
    [Theory]
    [InlineData("OUTLOOK", "Inbox - sam@x.com - Outlook", false, false, false, false, TimeCategory.Email)]
    [InlineData("ms-teams", "Chat | Microsoft Teams", false, false, false, false, TimeCategory.Chat)]
    [InlineData("devenv", "Nekomata - Visual Studio", false, false, false, false, TimeCategory.Projects)]
    [InlineData("devenv", "Nekomata - Visual Studio", false, false, true, false, TimeCategory.Focus)]
    [InlineData("WINWORD", "Policy.docx - Word", false, false, false, false, TimeCategory.Documents)]
    [InlineData("msedge", "Users - Microsoft 365 admin center", false, false, false, false, TimeCategory.Admin)]
    [InlineData("OUTLOOK", "Inbox", true, false, false, false, TimeCategory.Meetings)]
    [InlineData("OUTLOOK", "Inbox", false, false, false, true, TimeCategory.Away)]
    public void Categorises(string app, string title, bool meeting, bool call, bool focus, bool idle, TimeCategory expected) =>
        Assert.Equal(expected, TimeLedger.Categorise(app, title, meeting, call, focus, idle));

    [Fact]
    public void Reactive_share_and_lunch_gap()
    {
        var day = new TimeEntry { Date = new DateTime(2026, 10, 19) };
        TimeLedger.Add(day, day.Date.AddHours(9), 120, TimeCategory.Email);
        TimeLedger.Add(day, day.Date.AddHours(11), 60, TimeCategory.Chat);
        TimeLedger.Add(day, day.Date.AddHours(14), 60, TimeCategory.Projects);
        Assert.Equal(75, TimeLedger.ReactivePercent([day]));

        TimeLedger.NoteInput(day, day.Date.AddHours(12));
        TimeLedger.NoteInput(day, day.Date.AddHours(12).AddMinutes(40));
        Assert.Equal(40, day.LongestMiddayBreak, 1);
    }
}
