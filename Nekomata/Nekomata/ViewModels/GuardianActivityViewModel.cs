using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.Core.Workspace;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

public partial class GuardianActivityViewModel : ObservableObject
{
    private readonly IGuardianAuditRepository _audit;
    private readonly GuardianUndoService _undo;
    private readonly IWorkspaceCoordinator _workspace;

    public ObservableCollection<GuardianAuditEntry> Entries { get; } = [];

    [ObservableProperty] private bool busy;
    [ObservableProperty] private string status = "Loading Guardian activity…";

    public GuardianActivityViewModel(IGuardianAuditRepository audit, GuardianUndoService undo,
        IWorkspaceCoordinator workspace)
    {
        _audit = audit;
        _undo = undo;
        _workspace = workspace;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        Busy = true;
        try
        {
            Entries.Clear();
            foreach (var entry in await _audit.GetRecentAsync()) Entries.Add(entry);
            Status = Entries.Count == 0 ? "Guardian has not applied any recorded actions yet." : $"{Entries.Count} recorded action{(Entries.Count == 1 ? "" : "s")}.";
        }
        catch (Exception ex) { Status = $"Activity unavailable: {ex.Message}"; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task UndoBatchAsync(GuardianAuditEntry entry)
    {
        Busy = true;
        try
        {
            var result = await _undo.UndoBatchAsync(entry.BatchId);
            Status = result.Message;
            if (result.Success) await _workspace.RefreshAsync();
            await LoadAsync();
            Status = result.Message;
        }
        catch (Exception ex) { Status = $"Undo failed safely: {ex.Message}"; }
        finally { Busy = false; }
    }
}
