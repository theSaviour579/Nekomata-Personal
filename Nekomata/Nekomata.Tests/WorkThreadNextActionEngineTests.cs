using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class WorkThreadNextActionEngineTests
{
    [Fact]
    public void Derive_MatureDependency_PreparesFollowUp()
    {
        var thread = Thread(projectId: 7);
        var loop = new GuardianOpenLoop { ProjectId = 7, WaitingOn = "Phil Mathers" };
        var workspace = new NekomataWorkspace { WorkThreads = [thread] };

        var action = Assert.Single(new WorkThreadNextActionEngine().Derive(workspace,
            [new GuardianOpenLoopChase(loop, 3, true, "Chase due", GuardianChaseState.Due)], DateTimeOffset.UtcNow));

        Assert.Equal(WorkThreadNextActionDisposition.Prepare, action.Disposition);
        Assert.Equal("PrepareDependencyFollowUp", action.ActionType);
        Assert.Equal(loop.Id, action.OpenLoopId);
    }

    [Fact]
    public void Derive_CoolingDependency_IsCorrectlyWaiting()
    {
        var now = DateTimeOffset.UtcNow;
        var reconsiderAt = now.AddDays(2);
        var thread = Thread(projectId: 7);
        var loop = new GuardianOpenLoop { ProjectId = 7, WaitingOn = "Phil Mathers" };

        var action = Assert.Single(new WorkThreadNextActionEngine().Derive(
            new NekomataWorkspace { WorkThreads = [thread] },
            [new GuardianOpenLoopChase(loop, 4, false, "Cooling down", GuardianChaseState.CoolingDown, reconsiderAt, 1)], now));

        Assert.Equal(WorkThreadNextActionDisposition.Wait, action.Disposition);
        Assert.Equal("WaitForDependency", action.ActionType);
        Assert.Equal(reconsiderAt, action.ReconsiderAt);
    }

    [Fact]
    public void Derive_UnsatisfiedRequirement_BlocksProgress()
    {
        var thread = Thread(projectId: 7);
        var workspace = new NekomataWorkspace
        {
            WorkThreads = [thread],
            ProcessRequirements = [new ProcessRequirement
            {
                WorkThreadId = thread.Id, Name = "Change Control", State = RequirementState.Required,
                Gate = ReadinessGate.Deployment, Reason = "Required for this system change."
            }]
        };

        var action = Assert.Single(new WorkThreadNextActionEngine().Derive(workspace, [], DateTimeOffset.UtcNow));

        Assert.Equal(WorkThreadNextActionDisposition.Blocked, action.Disposition);
        Assert.Equal("SatisfyProcessRequirement", action.ActionType);
        Assert.Contains("Change Control", action.Title);
    }

    [Fact]
    public void Derive_DoesNotCorrelateUnrelatedNullProjectLoop()
    {
        var thread = Thread(projectId: null);
        var unrelated = new GuardianOpenLoop { ProjectId = null, WaitingOn = "Unrelated person" };

        var action = Assert.Single(new WorkThreadNextActionEngine().Derive(
            new NekomataWorkspace { WorkThreads = [thread] },
            [new GuardianOpenLoopChase(unrelated, 8, true, "Due", GuardianChaseState.Due)], DateTimeOffset.UtcNow));

        Assert.Equal("AwaitNewEvidence", action.ActionType);
    }

    private static GuardianWorkThread Thread(long? projectId) => new()
    {
        Id = Guid.NewGuid(), Title = "Purchasing Review", PrimaryProjectId = projectId
    };
}
