using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Actions;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianProposalReviewTests
{
    [Fact]
    public void Impact_counts_only_selected_items_and_totals_new_work()
    {
        var proposal = new GuardianActionResponse
        {
            Tasks =
            [
                new ProposedTask { Selected = true, EstimatedMinutes = 90, EstimatedBusinessValue = 12000 },
                new ProposedTask { Selected = false, EstimatedMinutes = 60, EstimatedBusinessValue = 8000 }
            ],
            Changes =
            [
                new GuardianChange { Selected = true, EntityType = "Calendar" },
                new GuardianChange { Selected = true, EntityType = "Project" },
                new GuardianChange { Selected = false, EntityType = "Task" }
            ]
        };

        var impact = GuardianProposalImpact.From(proposal);

        Assert.Equal(3, impact.SelectedCount);
        Assert.Equal(5, impact.TotalCount);
        Assert.Equal(1, impact.NewTaskCount);
        Assert.Equal(1, impact.CalendarChangeCount);
        Assert.Equal(1, impact.ProjectChangeCount);
        Assert.Equal(0, impact.TaskChangeCount);
        Assert.Equal(90, impact.EstimatedMinutes);
        Assert.Equal(12000, impact.EstimatedBusinessValue);
    }

    [Fact]
    public async Task Apply_uses_review_project_when_response_has_no_project()
    {
        var pipeline = new CapturingPipeline();
        var service = new GuardianApplyService(pipeline);
        var response = new GuardianActionResponse();

        await service.ApplyAsync(response, 42);

        Assert.Equal(42, pipeline.Response?.ProjectId);
    }

    private sealed class CapturingPipeline : IGuardianActionPipeline
    {
        public GuardianActionResponse? Response { get; private set; }

        public Task ExecuteAsync(GuardianActionResponse response, GuardianApplyResult result)
        {
            Response = response;
            return Task.CompletedTask;
        }
    }
}
