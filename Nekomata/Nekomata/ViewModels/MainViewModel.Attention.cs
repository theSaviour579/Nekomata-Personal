using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.Models.Workspace;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private readonly DispatcherTimer _attentionTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    [ObservableProperty]
    private ObservableCollection<GuardianAttentionItem> attentionItems = [];

    public IReadOnlyList<GuardianAttentionItem> VisibleAttentionItems =>
        AttentionItems
            .Where(item => item.IsActive)
            .Where(item => !item.Key.StartsWith("halo:followup:", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => AttentionSeverityRank(item.Severity))
            .ThenByDescending(item => item.CreatedAt)
            .ToList();

    public int ActiveAttentionCount => VisibleAttentionItems.Count;
    public bool HasActiveAttention => ActiveAttentionCount > 0;
    public bool HasAttentionHistory => VisibleAttentionItems.Count > 0;
    public string AttentionButtonLabel => HasActiveAttention ? $"ATTENTION ({ActiveAttentionCount})" : "ATTENTION";

    private void InitialiseAttentionCentre()
    {
        LoadAttentionItems();
        _attentionTimer.Tick += (_, _) => WakeSnoozedAttentionItems();
        _attentionTimer.Start();
    }

    [RelayCommand]
    private void ShowAttention() => WorkspaceMode = WorkspaceMode.Attention;

    [RelayCommand]
    private async Task ActAttentionItemAsync(GuardianAttentionItem? item)
    {
        if (item is null) return;
        switch (item.ActionKind)
        {
            case "open_email":
                if (!string.IsNullOrWhiteSpace(item.WebLink))
                    Process.Start(new ProcessStartInfo(item.WebLink) { UseShellExecute = true });
                else
                    WorkspaceMode = WorkspaceMode.Email;
                break;
            case "open_halo":
                WorkspaceMode = WorkspaceMode.Battle;
                break;
            case "open_halo_ticket":
                OpenHaloTicket(item.ContextId);
                break;
            case "start_calendar":
                WorkspaceMode = WorkspaceMode.Calendar;
                if (_planAlertEvent is not null && string.Equals(_planAlertEvent.Id, item.ContextId, StringComparison.OrdinalIgnoreCase))
                    await StartPlanAlertAsync();
                break;
            case "open_calendar":
                WorkspaceMode = WorkspaceMode.Calendar;
                break;
            default:
                WorkspaceMode = WorkspaceMode.Dashboard;
                break;
        }
    }

    [RelayCommand]
    private void SnoozeAttentionItem(GuardianAttentionItem? item)
    {
        if (item is null) return;
        item.Status = "Snoozed";
        item.SnoozedUntil = DateTimeOffset.Now.AddMinutes(15);
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
    }

    [RelayCommand]
    private void ResolveAttentionItem(GuardianAttentionItem? item)
    {
        if (item is null) return;
        item.Status = "Resolved";
        item.SnoozedUntil = null;
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
    }

    [RelayCommand]
    private void ClearResolvedAttention()
    {
        foreach (var item in AttentionItems.Where(item =>
                     item.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase)).ToList())
            AttentionItems.Remove(item);
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
    }

    private void RaiseAttention(
        string key,
        string source,
        string severity,
        string title,
        string detail,
        string actionKind,
        string? contextId = null,
        string? webLink = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var existing = AttentionItems.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Source = source;
            existing.Severity = severity;
            existing.Title = title;
            existing.Detail = detail;
            existing.ActionKind = actionKind;
            existing.ContextId = contextId ?? "";
            existing.WebLink = webLink ?? "";
            SaveAttentionItems();
            RaiseAttentionSummaryChanged();
            return;
        }

        var item = new GuardianAttentionItem
        {
            Key = key,
            Source = source,
            Severity = severity,
            Title = title,
            Detail = detail,
            ActionKind = actionKind,
            ContextId = contextId ?? "",
            WebLink = webLink ?? "",
            CreatedAt = DateTimeOffset.Now
        };
        AttentionItems.Insert(0, item);
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
        NotifyAttention();
    }

    private void ResolveAttention(string key)
    {
        var item = AttentionItems.FirstOrDefault(candidate => candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (item is null || item.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase)) return;
        item.Status = "Resolved";
        item.SnoozedUntil = null;
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
    }

    private void ResolveAttentionByPrefix(string prefix, string? exceptKey = null)
    {
        var changed = false;
        foreach (var item in AttentionItems.Where(item =>
                     item.IsActive &&
                     item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                     !item.Key.Equals(exceptKey, StringComparison.OrdinalIgnoreCase)))
        {
            item.Status = "Resolved";
            item.SnoozedUntil = null;
            changed = true;
        }

        if (!changed)
            return;

        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
    }

    private static int AttentionSeverityRank(string severity) =>
        severity.ToLowerInvariant() switch
        {
            "critical" => 0,
            "high" => 1,
            "warning" => 2,
            _ => 3
        };
    private void WakeSnoozedAttentionItems()
    {
        var now = DateTimeOffset.Now;
        var changed = false;
        foreach (var item in AttentionItems.Where(item =>
                     item.Status.Equals("Snoozed", StringComparison.OrdinalIgnoreCase) &&
                     item.SnoozedUntil <= now))
        {
            item.Status = "Active";
            item.SnoozedUntil = null;
            changed = true;
        }
        if (!changed) return;
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
        NotifyAttention();
    }

    private void NotifyAttention()
    {
        System.Media.SystemSounds.Exclamation.Play();
        var window = Application.Current?.MainWindow;
        if (window is null || window.WindowState != WindowState.Minimized) return;
        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Window = new WindowInteropHelper(window).Handle,
            Flags = 3 | 12,
            Count = 5,
            Timeout = 0
        };
        FlashWindowEx(ref info);
    }

    private void LoadAttentionItems()
    {
        try
        {
            if (!File.Exists(AttentionStorePath)) return;
            var items = JsonSerializer.Deserialize<List<GuardianAttentionItem>>(File.ReadAllText(AttentionStorePath)) ?? [];
            foreach (var item in items.OrderByDescending(item => item.CreatedAt).Take(100))
                AttentionItems.Add(item);
            WakeSnoozedAttentionItems();
            RaiseAttentionSummaryChanged();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Could not load Guardian attention history: " + ex);
        }
    }

    private void SaveAttentionItems()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AttentionStorePath)!);
            var retained = AttentionItems
                .Where(item => item.IsActive || item.CreatedAt > DateTimeOffset.Now.AddDays(-14))
                .OrderByDescending(item => item.CreatedAt)
                .Take(100)
                .ToList();
            File.WriteAllText(AttentionStorePath, JsonSerializer.Serialize(retained));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Could not save Guardian attention history: " + ex);
        }
    }

    private void RaiseAttentionSummaryChanged()
    {
        OnPropertyChanged(nameof(VisibleAttentionItems));
        OnPropertyChanged(nameof(ActiveAttentionCount));
        OnPropertyChanged(nameof(HasActiveAttention));
        OnPropertyChanged(nameof(HasAttentionHistory));
        OnPropertyChanged(nameof(AttentionButtonLabel));
    }

    private static string AttentionStorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nekomata",
        "attention-items.json");

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);
}