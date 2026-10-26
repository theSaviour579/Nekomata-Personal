using Nekomata.Core.Commitments;
using Xunit;

namespace Nekomata.Tests;

public sealed class CommitmentEngineTests
{
    private static readonly DateTimeOffset Now = new(2026,9,12,9,0,0,TimeSpan.Zero);

    [Fact]
    public void Explicit_promise_creates_high_confidence_commitment()
    {
        var value=Assert.Single(new CommitmentEngine().Detect([Evidence("I'll send you the revised figures by Friday",true)],Now));
        Assert.Equal(CommitmentDirection.OwedByMe,value.Direction);
        Assert.True(value.Confidence>=90);
        Assert.NotNull(value.DueAt);
    }

    [Fact]
    public void Vague_language_is_lower_confidence()
    {
        var value=Assert.Single(new CommitmentEngine().Detect([Evidence("I'll have a look at the figures",false)],Now));
        Assert.True(value.Confidence<60);
        Assert.Equal(CommitmentDirection.OwedToMe,value.Direction);
    }

    [Fact]
    public void Same_commitment_across_email_and_teams_is_deduplicated()
    {
        var first=Evidence("I will send the revised figures tomorrow",true) with { SourceType="Email", CorrelationKey="pricing-42" };
        var second=Evidence("I will send the revised figures tomorrow",true) with { SourceType="Teams", CorrelationKey="pricing-42" };
        var value=Assert.Single(new CommitmentEngine().Detect([first,second],Now));
        Assert.Contains("deduplicated",value.EvidenceSummary);
    }

    [Fact]
    public void Completion_evidence_closes_commitment()
    {
        var engine=new CommitmentEngine();
        var value=Assert.Single(engine.Detect([Evidence("I will send the revised figures tomorrow",true) with { CorrelationKey="pricing-42" }],Now));
        var completed=engine.Reconcile(value,Evidence("The revised figures are attached and completed",true) with { CorrelationKey="pricing-42",ObservedAt=Now.AddHours(2) },Now.AddHours(2));
        Assert.Equal(CommitmentStatus.Completed,completed.Status);
    }

    [Fact]
    public void Delivery_on_the_same_thread_closes_commitment_without_completion_words()
    {
        var engine=new CommitmentEngine();
        var value=Assert.Single(engine.Detect([Evidence("I'll send the data tomorrow",true) with { CorrelationKey="thread-9" }],Now));
        var reply=Evidence("Hi Sam, here's the data you wanted. Any questions let me know?\n[Attachment included]",true) with { CorrelationKey="thread-9",ObservedAt=Now.AddDays(1) };
        Assert.Equal(CommitmentStatus.Completed,engine.Reconcile(value,reply,Now.AddDays(1)).Status);
    }

    [Fact]
    public void Delivery_on_another_thread_or_with_a_new_promise_does_not_close()
    {
        var engine=new CommitmentEngine();
        var value=Assert.Single(engine.Detect([Evidence("I'll send the data tomorrow",true) with { CorrelationKey="thread-9" }],Now));
        var otherThread=Evidence("Please find attached the report",true) with { CorrelationKey="thread-10",ObservedAt=Now.AddDays(1) };
        var newPromise=Evidence("Please find attached part one. I'll send the rest on Monday",true) with { CorrelationKey="thread-9",ObservedAt=Now.AddDays(1) };
        Assert.NotEqual(CommitmentStatus.Completed,engine.Reconcile(value,otherThread,Now.AddDays(1)).Status);
        Assert.NotEqual(CommitmentStatus.Completed,engine.Reconcile(value,newPromise,Now.AddDays(1)).Status);
    }

    [Fact]
    public void Overdue_owed_by_me_requires_attention_but_healthy_waiting_external_does_not()
    {
        var mine=Assert.Single(new CommitmentEngine().Detect([Evidence("I will send the figures",true) with { ExplicitDueAt=Now.AddHours(-1) }],Now));
        var theirs=Assert.Single(new CommitmentEngine().Detect([Evidence("I will send the figures",false) with { ExplicitDueAt=Now.AddDays(3) }],Now));
        Assert.True(CommitmentEngine.RequiresAttention(mine));
        Assert.False(CommitmentEngine.RequiresAttention(theirs));
    }

    private static CommitmentEvidence Evidence(string body,bool mine)=>new("Email","msg-1",body,mine?"Alex":"Alex",mine?"Alex":"Alex",Now,mine);
}
