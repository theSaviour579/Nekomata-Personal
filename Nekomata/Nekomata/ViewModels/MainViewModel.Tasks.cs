using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Tasks;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task CompleteTaskAsync(
        NekomataTask? task)
    {
        if (task is null || GuardianBusy)
            return;

        var result = MessageBox.Show(
            $"Mark \"{task.Title}\" as completed?",
            "Complete Task",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        GuardianBusy = true;

        try
        {
            var taskRepository =
                _services.GetRequiredService<ITaskRepository>();

            var memoryRepository =
                _services.GetRequiredService<
                    IGuardianMemoryRepository>();

            await memoryRepository.AddAsync(
                new GuardianMemory
                {
                    Category = "TaskCompleted",
                    Importance = 55,
                    Source = "User",

                    Summary =
                        $"Completed task '{task.Title}'.",

                    Detail = $"""
                        Priority: {task.Priority}
                        Estimated time: {task.EstimatedMinutes} minutes
                        Business value: {task.EstimatedBusinessValue:C}
                        """,

                    ProjectId = task.ProjectId,
                    TaskId = task.Id
                });

            await taskRepository.CompleteAsync(task.Id);

            await _workspaceCoordinator.RefreshAsync();

            if (task.ProjectId is not null)
            {
                await OfferProjectCompletionAsync(
                    task.ProjectId.Value);
            }

            GuardianResponse =
                $"Task completed: {task.Title}.";

            ChatHistory.Add(
                new()
                {
                    Role = "assistant",
                    Content = GuardianResponse
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Task Completion Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            GuardianBusy = false;
        }
    }

    private async Task OfferProjectCompletionAsync(
        long projectId)
    {
        var projectRepository =
            _services.GetRequiredService<IProjectRepository>();

        var memoryRepository =
            _services.GetRequiredService<
                IGuardianMemoryRepository>();

        var project =
            await projectRepository.GetByIdAsync(projectId);

        if (project is null)
            return;

        if (project.ProgressPercent < 100)
            return;

        if (string.Equals(
                project.Status,
                "Completed",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var result = MessageBox.Show(
            $"All tasks for \"{project.Name}\" are complete.\n\n" +
            "Would you like to mark the project as completed?",
            "Project Ready to Complete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        project.Status = "Completed";

        await projectRepository.SaveAsync(project);

        await memoryRepository.AddAsync(
            new GuardianMemory
            {
                Category = "ProjectCompleted",
                Importance = 90,
                Source = "User",

                Summary =
                    $"Completed project '{project.Name}'.",

                Detail = $"""
                    Final progress: 100%
                    Business value: {project.EstimatedBusinessValue:C}
                    Completed at: {DateTime.Now:dd MMM yyyy HH:mm}
                    """,

                ProjectId = project.Id
            });

        GuardianResponse =
            $"Project completed: {project.Name}.";

        ChatHistory.Add(
            new()
            {
                Role = "assistant",
                Content = GuardianResponse
            });

        GuardianPanelExpanded = true;
    }
}