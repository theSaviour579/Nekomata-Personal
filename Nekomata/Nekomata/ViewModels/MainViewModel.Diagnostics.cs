using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Models.Workspace;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<IntegrationDiagnosticItem> Diagnostics { get; } = [];

    [ObservableProperty]
    private bool diagnosticsBusy;

    [ObservableProperty]
    private string diagnosticsStatus = "Run checks to verify every Nekomata connection.";

    [ObservableProperty]
    private bool diagnosticsRunFailed;

    private DispatcherTimer? _diagnosticsTimer;

    public int HealthyDiagnosticCount => Diagnostics.Count(item => item.IsHealthy);
    public int AttentionDiagnosticCount => Diagnostics.Count(item => item.NeedsAttention);
    public bool HasDiagnosticIssues => DiagnosticsRunFailed || AttentionDiagnosticCount > 0;

    public string DiagnosticStatusButtonLabel => DiagnosticsBusy
        ? "↻ CHECKING"
        : DiagnosticsRunFailed
            ? "⚠ CHECK FAILED"
            : AttentionDiagnosticCount > 0
                ? $"⚠ {AttentionDiagnosticCount} ISSUE{(AttentionDiagnosticCount == 1 ? "" : "S")}"
                : Diagnostics.Count == 0
                    ? "● STATUS"
                    : "● ONLINE";

    public string DiagnosticStatusButtonToolTip => Diagnostics.Count == 0
        ? "Run integration health checks"
        : HasDiagnosticIssues
            ? "Open Diagnostics to review connection issues"
            : $"All configured integrations healthy · last checked {Diagnostics.Max(item => item.CheckedAt):HH:mm}";

    public string DiagnosticsSummary => Diagnostics.Count == 0
        ? "No checks have run yet."
        : $"{HealthyDiagnosticCount} healthy · {AttentionDiagnosticCount} need attention · " +
          $"{Diagnostics.Count - HealthyDiagnosticCount - AttentionDiagnosticCount} optional or not configured";

    [RelayCommand]
    private async Task ShowDiagnosticsAsync()
    {
        WorkspaceMode = WorkspaceMode.Diagnostics;
        if (Diagnostics.Count == 0)
            await RefreshDiagnosticsAsync();
    }

    [RelayCommand]
    private async Task RefreshDiagnosticsAsync()
    {
        if (DiagnosticsBusy)
            return;

        DiagnosticsBusy = true;
        DiagnosticsRunFailed = false;
        DiagnosticsStatus = "Checking database and external services…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var results = await _services
                .GetRequiredService<IntegrationDiagnosticsService>()
                .RunAsync(timeout.Token);

            Diagnostics.Clear();
            foreach (var result in results)
                Diagnostics.Add(result);

            OnPropertyChanged(nameof(HealthyDiagnosticCount));
            OnPropertyChanged(nameof(AttentionDiagnosticCount));
            OnPropertyChanged(nameof(DiagnosticsSummary));
            NotifyDiagnosticIndicatorChanged();
            DiagnosticsStatus = AttentionDiagnosticCount == 0
                ? "Checks completed. Core connections are healthy."
                : "Checks completed. Review the highlighted connections.";
        }
        catch (OperationCanceledException)
        {
            DiagnosticsRunFailed = true;
            DiagnosticsStatus = "Diagnostics timed out after 45 seconds. Try again when services are reachable.";
        }
        catch (Exception ex)
        {
            DiagnosticsRunFailed = true;
            DiagnosticsStatus = "Diagnostics could not complete: " + ex.Message;
        }
        finally
        {
            DiagnosticsBusy = false;
            NotifyDiagnosticIndicatorChanged();
        }
    }

    partial void OnDiagnosticsBusyChanged(bool value) => NotifyDiagnosticIndicatorChanged();

    partial void OnDiagnosticsRunFailedChanged(bool value) => NotifyDiagnosticIndicatorChanged();

    private void InitialiseDiagnosticsMonitoring()
    {
        _diagnosticsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(10)
        };
        _diagnosticsTimer.Tick += async (_, _) => await RefreshDiagnosticsAsync();
        _diagnosticsTimer.Start();
    }

    private void NotifyDiagnosticIndicatorChanged()
    {
        OnPropertyChanged(nameof(HasDiagnosticIssues));
        OnPropertyChanged(nameof(DiagnosticStatusButtonLabel));
        OnPropertyChanged(nameof(DiagnosticStatusButtonToolTip));
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        if (Diagnostics.Count == 0)
        {
            DiagnosticsStatus = "Run the checks before copying a report.";
            return;
        }

        Clipboard.SetText(IntegrationDiagnosticsService.FormatReport(Diagnostics));
        DiagnosticsStatus = "A credential-free diagnostics report was copied to the clipboard.";
    }
}
