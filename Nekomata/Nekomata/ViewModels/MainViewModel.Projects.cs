using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Projects;
using Nekomata.UI.Windows;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task NewProjectAsync()
    {
        var existingHighestProjectId =
            Workspace.Projects.Count == 0
                ? 0
                : Workspace.Projects.Max(project => project.Id);

        var window =
            _services.GetRequiredService<ProjectWindow>();

        window.Owner =
            Application.Current.MainWindow;

        if (window.ShowDialog() != true)
            return;

        await _workspaceCoordinator.RefreshAsync();

        var createdProject =
            Workspace.Projects
                .Where(project =>
                    project.Id > existingHighestProjectId)
                .OrderByDescending(project => project.Id)
                .FirstOrDefault();

        if (createdProject is null)
            return;

        var memoryRepository =
            _services.GetRequiredService<
                IGuardianMemoryRepository>();

        await memoryRepository.AddAsync(
            new GuardianMemory
            {
                Category = "ProjectCreated",
                Importance = 80,
                Source = "User",

                Summary =
                    $"Created project '{createdProject.Name}'.",

                Detail = $"""
                    Description: {createdProject.Description ?? "No description"}
                    Priority: {createdProject.Priority}
                    Status: {createdProject.Status}
                    Business value: {createdProject.EstimatedBusinessValue:C}
                    Due: {createdProject.DueAt?.ToString("dd MMM yyyy") ?? "No due date"}
                    """,

                ProjectId = createdProject.Id
            });
    }

    [RelayCommand]
    private async Task EditProjectAsync(
        NekomataProject? project)
    {
        if (project is null)
            return;

        var projectRepository =
            _services.GetRequiredService<IProjectRepository>();

        var memoryRepository =
            _services.GetRequiredService<
                IGuardianMemoryRepository>();

        var freshProject =
            await projectRepository.GetByIdAsync(project.Id);

        if (freshProject is null)
            return;

        var originalStatus =
            freshProject.Status;

        var originalPriority =
            freshProject.Priority;

        var originalProgress =
            freshProject.ProgressPercent;

        var originalBusinessValue =
            freshProject.EstimatedBusinessValue;

        var originalDueDate =
            freshProject.DueAt;

        var viewModel =
            new ProjectWindowViewModel(
                projectRepository,
                freshProject);

        var window =
            new ProjectWindow(viewModel)
            {
                Owner = Application.Current.MainWindow
            };

        if (window.ShowDialog() != true)
            return;

        await _workspaceCoordinator.RefreshAsync();

        var updatedProject =
            await projectRepository.GetByIdAsync(project.Id);

        if (updatedProject is null)
            return;

        await memoryRepository.AddAsync(
            new GuardianMemory
            {
                Category = "ProjectUpdated",
                Importance = 65,
                Source = "User",

                Summary =
                    $"Updated project '{updatedProject.Name}'.",

                Detail = $"""
                    Status: {originalStatus} → {updatedProject.Status}
                    Priority: {originalPriority} → {updatedProject.Priority}
                    Progress: {originalProgress}% → {updatedProject.ProgressPercent}%
                    Business value: {originalBusinessValue:C} → {updatedProject.EstimatedBusinessValue:C}
                    Due date: {FormatDate(originalDueDate)} → {FormatDate(updatedProject.DueAt)}
                    """,

                ProjectId = updatedProject.Id
            });
    }

    private static string FormatDate(
        DateTime? value)
    {
        return value?.ToString("dd MMM yyyy")
            ?? "No due date";
    }
}