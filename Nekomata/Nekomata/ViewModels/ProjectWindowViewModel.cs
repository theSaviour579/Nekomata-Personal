using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.Data.Repositories;
using Nekomata.Models.Projects;

namespace Nekomata.UI.ViewModels;

public partial class ProjectWindowViewModel : ObservableObject
{
    private readonly IProjectRepository _repository;

    [ObservableProperty]
    private NekomataProject project;

    public event Action? CloseRequested;

    public IReadOnlyList<string> Priorities { get; } =
    [
        "Low",
        "Normal",
        "High",
        "Critical"
    ];

    public IReadOnlyList<string> Statuses { get; } =
    [
        "Active",
        "On Hold",
        "Completed"
    ];

    public bool IsEdit => Project.Id != 0;

    public ProjectWindowViewModel(
        IProjectRepository repository,
        NekomataProject? project = null)
    {
        _repository = repository;
        Project = project ?? new NekomataProject();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Project.Name))
            return;

        Project.Id = await _repository.SaveAsync(Project);

        CloseRequested?.Invoke();
    }
}