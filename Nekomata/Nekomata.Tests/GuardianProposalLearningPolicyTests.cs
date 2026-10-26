using System.Text.Json;
using Nekomata.Core.Guardian.Learning;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianProposalLearningPolicyTests
{
    [Fact]
    public void Removed_similar_suggestion_is_suppressed()
    {
        var memory = new GuardianMemory
        {
            Category = "ProposalFeedback", Summary = "Removed proposal: Build monthly spend dataset",
            Metadata = JsonSerializer.Serialize(new { disposition = "Removed", originalTitle = "Build monthly spend dataset" })
        };

        Assert.True(new GuardianProposalLearningPolicy().ShouldSuppress("Build the monthly spend dataset", [memory]));
    }

    [Fact]
    public void Unrelated_work_is_not_suppressed()
    {
        var memory = new GuardianMemory
        {
            Category = "ProposalFeedback", Summary = "Removed proposal: Build monthly spend dataset",
            Metadata = JsonSerializer.Serialize(new { disposition = "Removed", originalTitle = "Build monthly spend dataset" })
        };

        Assert.False(new GuardianProposalLearningPolicy().ShouldSuppress("Review phishing simulation failures", [memory]));
    }

    [Fact]
    public void Guidance_includes_replacement_evidence()
    {
        var memory = new GuardianMemory
        {
            Category = "ProposalFeedback", ProjectId = 7, CreatedAt = DateTime.UtcNow,
            Detail = "Replace 'large task' with 'small verified step'."
        };

        var guidance = new GuardianProposalLearningPolicy().BuildGuidance([memory], 7);

        Assert.Contains("small verified step", guidance);
    }

    [Fact]
    public void Replaced_original_is_suppressed_in_later_plans()
    {
        var memory = new GuardianMemory
        {
            Category = "ProposalFeedback", Summary = "Replaced proposal: Broad project review",
            Metadata = JsonSerializer.Serialize(new { disposition = "Replaced", originalTitle = "Broad project review", replacement = "Review open risks" })
        };

        Assert.True(new GuardianProposalLearningPolicy().ShouldSuppress("Broad project review", [memory]));
    }

    [Fact]
    public void Feedback_can_inform_a_later_project()
    {
        var memory = new GuardianMemory
        {
            Category = "ProposalFeedback", ProjectId = 7, CreatedAt = DateTime.UtcNow,
            Detail = "Do not propose a broad review; prefer a verified next step."
        };

        var guidance = new GuardianProposalLearningPolicy().BuildGuidance([memory], 99);

        Assert.Contains("verified next step", guidance);
    }
}
