using Nekomata.Data.Local;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Nekomata.Models.People;
using Xunit;

namespace Nekomata.Tests;

public sealed class LocalWorkspaceRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NekomataPersonalTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TasksPersistAcrossRepositoryInstances()
    {
        var path = Path.Combine(_directory, "workspace.json");
        var first = new LocalTaskRepository(new LocalWorkspaceStore(path));
        var id = await first.SaveAsync(new NekomataTask { Title = "Plan the week", Status = "Open", PriorityScore = 80 });

        var second = new LocalTaskRepository(new LocalWorkspaceStore(path));
        var restored = await second.GetByIdAsync(id);

        Assert.NotNull(restored);
        Assert.Equal("Plan the week", restored.Title);
    }

    [Fact]
    public async Task CompletingProjectTasksOnlyChangesMatchingOpenTasks()
    {
        var store = new LocalWorkspaceStore(Path.Combine(_directory, "workspace.json"));
        var projects = new LocalProjectRepository(store);
        var tasks = new LocalTaskRepository(store);
        var projectId = await projects.SaveAsync(new NekomataProject { Name = "Home project" });
        var matchingId = await tasks.SaveAsync(new NekomataTask { Title = "Matching", ProjectId = projectId, Status = "Open" });
        var otherId = await tasks.SaveAsync(new NekomataTask { Title = "Other", Status = "Open" });

        var completed = await tasks.CompleteOpenTasksForProjectAsync(projectId);

        Assert.Equal(1, completed);
        Assert.Equal("Completed", (await tasks.GetByIdAsync(matchingId))!.Status);
        Assert.Equal("Open", (await tasks.GetByIdAsync(otherId))!.Status);
    }

    [Fact]
    public async Task Microsoft_team_profile_keeps_editable_role_skills()
    {
        var path = Path.Combine(_directory, "team-workspace.json");
        var first = new LocalTeamProfileRepository(new LocalWorkspaceStore(path));
        await first.SaveAsync(new TeamMemberProfile
        {
            ExternalId = "m365-person-1", Name = "Alex Morgan", Email = "alex@example.test", Role = "Project Manager",
            Source = "Microsoft 365", Responsibilities = "Own delivery", Skills = [new() { Name = "Planning", Proficiency = 3, IsPrimary = true }]
        });

        var restored = Assert.Single(await new LocalTeamProfileRepository(new LocalWorkspaceStore(path)).GetAllAsync());

        Assert.Equal("m365-person-1", restored.ExternalId);
        Assert.Equal("Project Manager", restored.Role);
        Assert.Equal("Planning", Assert.Single(restored.Skills).Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
