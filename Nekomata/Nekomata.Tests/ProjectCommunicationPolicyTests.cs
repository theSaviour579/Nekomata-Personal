using Nekomata.Core.Guardian.Context;
using Xunit;

namespace Nekomata.Tests;

public sealed class ProjectCommunicationPolicyTests
{
    [Theory]
    [InlineData("I've chased the supplier; please update the end user.")]
    [InlineData("Customer needs an update now that we have heard back.")]
    [InlineData("Could you respond to the requester with the progress update?")]
    public void Detects_project_updates_that_require_a_user_response(string text) =>
        Assert.True(ProjectCommunicationPolicy.RequiresEndUserResponse(text));

    [Theory]
    [InlineData("General project discussion")]
    [InlineData("The supplier sent an update")]
    [InlineData("Please update the documentation")]
    [InlineData("UPDATE public.customer SET grouping_ac = 'SDENCL' WHERE account IN ('2009800')")]
    [InlineData("update customer set usern1 = 0 where usern1 = 353;")]
    [InlineData("I am a hacker and I have successfully gained access to your operating system and account.")]
    public void Does_not_invent_user_response_loops(string text) =>
        Assert.False(ProjectCommunicationPolicy.RequiresEndUserResponse(text));

    [Theory]
    [InlineData("We are awaiting the supplier response")]
    [InlineData("Blocked on the external developer")]
    [InlineData("This is with the vendor now")]
    public void Detects_explicit_external_dependencies(string text) =>
        Assert.True(ProjectCommunicationPolicy.IndicatesExternalWait(text));

    [Theory]
    [InlineData("SELECT * FROM supplier WHERE status = 'pending'")]
    [InlineData("I am a hacker and gained access to your account while waiting for bitcoin")]
    public void Machine_or_unsafe_text_does_not_create_external_waits(string text) =>
        Assert.False(ProjectCommunicationPolicy.IndicatesExternalWait(text));

    [Fact]
    public void Next_working_morning_skips_weekends()
    {
        var friday = new DateTimeOffset(2026, 9, 11, 16, 0, 0, TimeSpan.FromHours(1));
        Assert.Equal(DayOfWeek.Monday, ProjectCommunicationPolicy.NextWorkingMorning(friday).DayOfWeek);
        Assert.Equal(9, ProjectCommunicationPolicy.NextWorkingMorning(friday).Hour);
    }
}
