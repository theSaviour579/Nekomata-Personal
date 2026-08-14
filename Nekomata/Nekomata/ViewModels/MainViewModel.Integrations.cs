using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using Nekomata.Core.Engines;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;
using Nekomata.Services.Halo;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    public bool IsKnowBe4Connected => Workspace.Integrations
        .Any(item => item.Name.Equals("KnowBe4", StringComparison.OrdinalIgnoreCase) && item.Connected);

    public int KnowBe4FailureCount => Workspace.IntegrationMissionCandidates
        .Count(item => item.SourceType.Equals("KnowBe4", StringComparison.OrdinalIgnoreCase));

    public string KnowBe4IntegrationStatus
    {
        get
        {
            var status = Workspace.Integrations
                .FirstOrDefault(item => item.Name.Equals("KnowBe4", StringComparison.OrdinalIgnoreCase));
            if (status is null) return "Waiting for first security sync";
            if (!status.Connected) return "Connection issue · refresh to retry";
            return status.LastRefresh is DateTime refreshed
                ? $"Connected · checked {refreshed:HH:mm}"
                : "Connected";
        }
    }

    public bool IsSpotifyConfigured =>
        !string.IsNullOrWhiteSpace(_services.GetService<IConfiguration>()?["Spotify:ClientId"]);

    [ObservableProperty] private string spotifyIntegrationStatus = "Preparing Arrival Mode...";
    [ObservableProperty] private string spotifyTrack = "Nothing playing";
    [ObservableProperty] private string spotifyArtist = string.Empty;
    [ObservableProperty] private string spotifyDevice = string.Empty;
    [ObservableProperty] private bool spotifyIsPlaying;
    [ObservableProperty] private bool spotifyShuffleEnabled;
    [ObservableProperty] private int spotifyVolume = 50;
    [ObservableProperty] private bool spotifyBusy;

    public string SpotifyPlayPauseLabel => SpotifyIsPlaying ? "PAUSE" : "PLAY";
    public string SpotifyShuffleLabel => SpotifyShuffleEnabled ? "SHUFFLE ON" : "SHUFFLE OFF";
    public string SpotifyConnectLabel => _services.GetRequiredService<SpotifyPlaybackService>().HasSavedConnection
        ? "PLAY ARRIVAL MIX"
        : "CONNECT SPOTIFY";
    private DispatcherTimer? _spotifyStateTimer;

    private async Task InitialiseSpotifyArrivalAsync()
    {
        var spotify = _services.GetRequiredService<SpotifyPlaybackService>();
        _spotifyStateTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _spotifyStateTimer.Tick -= SpotifyStateTimerTick;
        _spotifyStateTimer.Tick += SpotifyStateTimerTick;
        _spotifyStateTimer.Start();
        await RefreshSpotifyStateAsync();
        if (!spotify.HasSavedConnection) return;
        try
        {
            SpotifyBusy = true;
            SpotifyIntegrationStatus = "Starting the Arrival Mix on shuffle...";
            await spotify.StartArrivalAsync();
        }
        catch (Exception ex) { SpotifyIntegrationStatus = $"Arrival Mode waiting - {ex.Message}"; }
        finally { SpotifyBusy = false; await RefreshSpotifyStateAsync(); }
    }

    private async void SpotifyStateTimerTick(object? sender, EventArgs e) => await RefreshSpotifyStateAsync();

    [RelayCommand]
    private async Task ConnectSpotifyAsync()
    {
        var spotify = _services.GetRequiredService<SpotifyPlaybackService>();
        try
        {
            SpotifyBusy = true;
            if (!spotify.HasSavedConnection)
            {
                SpotifyIntegrationStatus = "Complete the Spotify consent in your browser...";
                await spotify.ConnectAsync();
            }
            SpotifyIntegrationStatus = "Starting the Arrival Mix on shuffle...";
            await spotify.StartArrivalAsync();
        }
        catch (Exception ex) { SpotifyIntegrationStatus = $"Spotify connection issue - {ex.Message}"; }
        finally { SpotifyBusy = false; OnPropertyChanged(nameof(SpotifyConnectLabel)); await RefreshSpotifyStateAsync(); }
    }

    [RelayCommand]
    private async Task ToggleSpotifyPlaybackAsync()
    {
        var spotify = _services.GetRequiredService<SpotifyPlaybackService>();
        await RunSpotifyCommandAsync(() => SpotifyIsPlaying ? spotify.PauseAsync() : spotify.ResumeAsync());
    }

    [RelayCommand] private async Task SpotifyPreviousAsync() => await RunSpotifyCommandAsync(() => _services.GetRequiredService<SpotifyPlaybackService>().PreviousAsync());
    [RelayCommand] private async Task SpotifyNextAsync() => await RunSpotifyCommandAsync(() => _services.GetRequiredService<SpotifyPlaybackService>().NextAsync());
    [RelayCommand] private async Task ToggleSpotifyShuffleAsync() => await RunSpotifyCommandAsync(() => _services.GetRequiredService<SpotifyPlaybackService>().SetShuffleAsync(!SpotifyShuffleEnabled));
    [RelayCommand] private async Task SpotifyVolumeDownAsync() => await SetSpotifyVolumeAsync(SpotifyVolume - 10);
    [RelayCommand] private async Task SpotifyVolumeUpAsync() => await SetSpotifyVolumeAsync(SpotifyVolume + 10);

    private async Task SetSpotifyVolumeAsync(int volume)
    {
        volume = Math.Clamp(volume, 0, 100);
        await RunSpotifyCommandAsync(() => _services.GetRequiredService<SpotifyPlaybackService>().SetVolumeAsync(volume));
    }

    private async Task RunSpotifyCommandAsync(Func<Task> action)
    {
        if (SpotifyBusy) return;
        try { SpotifyBusy = true; await action(); }
        catch (Exception ex) { SpotifyIntegrationStatus = $"Spotify control issue - {ex.Message}"; }
        finally { SpotifyBusy = false; await Task.Delay(250); await RefreshSpotifyStateAsync(); }
    }

    private async Task RefreshSpotifyStateAsync()
    {
        if (SpotifyBusy) return;
        var state = await _services.GetRequiredService<SpotifyPlaybackService>().GetStateAsync();
        SpotifyIntegrationStatus = state.Status;
        SpotifyTrack = state.Track;
        SpotifyArtist = state.Artist;
        SpotifyDevice = state.Device;
        SpotifyIsPlaying = state.IsPlaying;
        SpotifyShuffleEnabled = state.ShuffleEnabled;
        SpotifyVolume = state.VolumePercent;
        OnPropertyChanged(nameof(SpotifyPlayPauseLabel));
        OnPropertyChanged(nameof(SpotifyShuffleLabel));
        OnPropertyChanged(nameof(SpotifyConnectLabel));
    }
    [RelayCommand]
    private void OpenSpotify()
    {
        try
        {
            Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("https://open.spotify.com") { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void ShowKnowBe4Attention() => WorkspaceMode = WorkspaceMode.Attention;
    private readonly HashSet<string> _seenUrgentHaloTickets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenStaleHaloTickets = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _integrationRefreshTimer;
    private bool _integrationRefreshBusy;

    public IReadOnlyList<MissionCandidate> UrgentHaloTickets =>
        Workspace.IntegrationMissionCandidates
            .Where(candidate => candidate.SourceType.Equals("Halo", StringComparison.OrdinalIgnoreCase) && candidate.RequiresImmediateAttention)
            .OrderByDescending(candidate => candidate.Priority.Equals("Critical", StringComparison.OrdinalIgnoreCase))
            .ThenBy(candidate => candidate.DueAt ?? DateTime.MaxValue)
            .ToList();

    public bool HasUrgentHaloTickets => UrgentHaloTickets.Count > 0;

    public IReadOnlyList<MissionCandidate> HaloWatchlistTickets =>
        Workspace.IntegrationMissionCandidates
            .Where(candidate => candidate.SourceType.Equals("Halo", StringComparison.OrdinalIgnoreCase))
            .Where(candidate => candidate.IsAwaitingExternalResponse)
            .OrderByDescending(candidate => candidate.IsActionable)
            .ThenByDescending(candidate => candidate.WaitingBusinessDays)
            .ThenBy(candidate => candidate.WaitingOwnerLabel)
            .ThenBy(candidate => candidate.Title)
            .ToList();

    public int HaloWatchlistCount => HaloWatchlistTickets.Count;
    public int HaloWatchlistChaseCount => HaloWatchlistTickets.Count(ticket => ticket.IsActionable);
    public string HaloWatchlistButtonLabel => HaloWatchlistCount > 0
        ? $"HALO WATCHLIST ({HaloWatchlistCount})"
        : "HALO WATCHLIST";
    public string HaloWatchlistSummary => HaloWatchlistCount == 0
        ? "No assigned Halo tickets are currently waiting on another party."
        : $"{HaloWatchlistCount} waiting · {HaloWatchlistChaseCount} chase{(HaloWatchlistChaseCount == 1 ? string.Empty : "s")} due";

    [RelayCommand]
    private void ShowHaloWatchlist() => WorkspaceMode = WorkspaceMode.HaloWatchlist;

    [RelayCommand]
    private void OpenHaloWatchlistTicket(MissionCandidate? ticket)
    {
        if (ticket is null) return;
        OpenHaloTicket(ticket.SourceRecordId);
    }

    public string HaloIntegrationStatus
    {
        get
        {
            var halo = Workspace.Integrations.FirstOrDefault(item => item.Name.Equals("Halo", StringComparison.OrdinalIgnoreCase));
            if (halo is null) return "Halo has not synced yet.";
            if (!halo.Connected) return $"Halo sync failed: {halo.ErrorMessage}";
            return $"{halo.MissionCount} monitored open · synced {halo.LastRefresh:HH:mm}";
        }
    }

    private void InitialiseIntegrationRefresh()
    {
        var options = _services.GetRequiredService<HaloOptions>();
        _integrationRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(Math.Clamp(options.RefreshIntervalMinutes, 1, 60))
        };
        _integrationRefreshTimer.Tick += async (_, _) => await RefreshIntegrationsAsync();
        _integrationRefreshTimer.Start();
    }

    [RelayCommand]
    private async Task RefreshIntegrationsAsync()
    {
        if (_integrationRefreshBusy) return;
        _integrationRefreshBusy = true;
        try
        {
            Workspace = await _workspaceCoordinator.RefreshAsync();
            await RefreshDailyBriefingContextAsync();
            await RefreshCalendarAwareObjectiveAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Integration refresh failed: {ex}");
        }
        finally
        {
            _integrationRefreshBusy = false;
        }
    }

    public IReadOnlyList<MissionCandidate> BattleTickets =>
        Workspace.IntegrationMissionCandidates
            .Where(candidate => candidate.SourceType.Equals("Halo", StringComparison.OrdinalIgnoreCase) && candidate.IsP1)
            .OrderBy(candidate => candidate.DueAt ?? DateTime.MaxValue)
            .ToList();

    public MissionCandidate? BattlePrimaryTicket => BattleTickets.FirstOrDefault();
    public int ActiveBattleCount => BattleTickets.Count;
    public string BattleIncidentLabel => ActiveBattleCount == 1
        ? "1 ACTIVE CRITICAL INCIDENT"
        : $"{ActiveBattleCount} ACTIVE CRITICAL INCIDENTS";
    public string BattleSlaCountdown
    {
        get
        {
            var due = BattlePrimaryTicket?.DueAt;
            if (due is null) return "SLA DEADLINE NOT SUPPLIED";
            var remaining = due.Value - CurrentDateTime;
            return remaining <= TimeSpan.Zero
                ? $"SLA BREACHED BY {FormatBattleDuration(remaining.Duration())}"
                : $"SLA DUE IN {FormatBattleDuration(remaining)}";
        }
    }
    public string BattleSlaDeadline => BattlePrimaryTicket?.DueAt is DateTime due
        ? $"Response deadline {due:HH:mm}"
        : "Response deadline unavailable";
    public string BattleEffortLabel => BattlePrimaryTicket is null
        ? "Effort unavailable"
        : $"Estimated response effort {BattlePrimaryTicket.EstimatedMinutes} min";

    [RelayCommand]
    private void ExitBattleMode() => WorkspaceMode = WorkspaceMode.Dashboard;

    [RelayCommand]
    private void OpenHaloBattleTicket(MissionCandidate? ticket)
    {
        if (ticket is null) return;
        OpenHaloTicket(ticket.SourceRecordId);
    }

    private void OpenHaloTicket(string? ticketId)
    {
        if (string.IsNullOrWhiteSpace(ticketId)) return;
        var baseUrl = _services.GetRequiredService<HaloOptions>().BaseUrl.TrimEnd('/');
        var ticketUrl = $"{baseUrl}/tickets?id={Uri.EscapeDataString(ticketId)}";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ticketUrl)
        {
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task RefreshBattleStatusAsync() => await RefreshIntegrationsAsync();

    private static string FormatBattleDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}H {duration.Minutes:00}M";
        return $"{Math.Max(0, duration.Minutes)}M {duration.Seconds:00}S";
    }

    private void HandleKnowBe4Alerts(NekomataWorkspace updatedWorkspace)
    {
        var failures = updatedWorkspace.IntegrationMissionCandidates
            .Where(candidate => candidate.SourceType.Equals("KnowBe4", StringComparison.OrdinalIgnoreCase))
            .Where(candidate => candidate.RequiresImmediateAttention)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SourceRecordId))
            .ToList();
        var newlyDetected = 0;

        foreach (var failure in failures)
        {
            var key = $"knowbe4:failure:{failure.SourceRecordId}";
            var alreadyTracked = AttentionItems.Any(item =>
                item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            RaiseAttention(
                key,
                "KNOWBE4",
                "High",
                failure.Title,
                failure.Description,
                "open_dashboard",
                failure.SourceRecordId);
            if (!alreadyTracked)
                newlyDetected++;
        }

        if (newlyDetected > 0)
        {
            _ = SpeakGuardianAsync(
                newlyDetected == 1
                    ? "A KnowBe4 simulation failure has been detected. I have added it to the Attention Centre."
                    : $"{newlyDetected} new KnowBe4 simulation failures have been detected. I have added them to the Attention Centre.",
                interrupt: true);
        }
    }
    private void HandleUrgentHaloAlerts(NekomataWorkspace updatedWorkspace)
    {
        HandleStaleHaloFollowUps(updatedWorkspace);

        var p1Tickets = updatedWorkspace.IntegrationMissionCandidates
            .Where(candidate => candidate.SourceType.Equals("Halo", StringComparison.OrdinalIgnoreCase) && candidate.IsP1)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SourceRecordId))
            .ToList();

        var activeIds = p1Tickets
            .Select(ticket => ticket.SourceRecordId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clearedIds = _seenUrgentHaloTickets
            .Where(ticketId => !activeIds.Contains(ticketId))
            .ToList();

        foreach (var clearedId in clearedIds)
        {
            _seenUrgentHaloTickets.Remove(clearedId);
            ResolveAttention($"halo:p1:{clearedId}");
        }

        foreach (var staleAttention in AttentionItems
                     .Where(item => item.Key.StartsWith("halo:p1:", StringComparison.OrdinalIgnoreCase))
                     .Where(item => !activeIds.Contains(item.ContextId))
                     .ToList())
        {
            ResolveAttention(staleAttention.Key);
        }

        if (p1Tickets.Count == 0)
        {
            if (WorkspaceMode == WorkspaceMode.Battle)
            {
                WorkspaceMode = WorkspaceMode.Dashboard;
                System.Diagnostics.Debug.WriteLine("Battle Mode stood down automatically: no active P1 tickets remain.");
            }
            return;
        }

        var newP1Tickets = p1Tickets
            .Where(candidate => _seenUrgentHaloTickets.Add(candidate.SourceRecordId!))
            .ToList();
        if (newP1Tickets.Count == 0) return;

        WorkspaceMode = WorkspaceMode.Battle;
        System.Diagnostics.Debug.WriteLine($"Battle Mode activated for Halo ticket(s): {string.Join(", ", newP1Tickets.Select(ticket => ticket.SourceRecordId))}");

        foreach (var ticket in newP1Tickets)
        {
            _ = SpeakGuardianAsync(
                $"Priority one interruption. {ticket.Title}. Battle Mode is now active.",
                interrupt: true);
            RaiseAttention(
                $"halo:p1:{ticket.SourceRecordId}",
                "HALO",
                "Critical",
                ticket.Title,
                $"A Critical/P1 Halo ticket requires immediate attention. Priority: {ticket.Priority}; estimated effort: {ticket.EstimatedMinutes} minutes.",
                "open_halo",
                ticket.SourceRecordId);
        }
    }
    private void HandleStaleHaloFollowUps(NekomataWorkspace updatedWorkspace)
    {
        // Routine Halo chasing belongs exclusively in Halo Watchlist. Retire
        // legacy Attention Centre entries without affecting Critical/P1 alerts.
        _seenStaleHaloTickets.Clear();
        ResolveAttentionByPrefix("halo:followup:");
    }
    public bool HasCapacityPushbacks => Workspace.Capacity.PushbackSuggestions.Count > 0;

    private void RefreshTimeAwareCapacity()
    {
        // A workspace rebuild creates a fresh CapacitySummary. Reapply the latest
        // same-day calendar snapshot before calculating task load.
        ApplyCachedTodayCalendarCapacity(DateTimeOffset.Now);
        _services.GetRequiredService<ICapacityEngine>().Calculate(Workspace);
        var capacity = Workspace.Capacity;
        Workspace.Briefing.AvailableMinutesToday = capacity.AvailableMinutesToday;
        Workspace.Briefing.PlannedMinutesToday = capacity.PlannedMinutesToday;
        Workspace.Briefing.RemainingCapacityMinutes = capacity.RemainingMinutesToday;
        Workspace.Briefing.CapacityUsedPercent = capacity.UtilisationPercent;
        Workspace.Briefing.IsOverCapacity = capacity.IsOverCapacity;
        Workspace.Briefing.OvertimeMinutesWorked = capacity.OvertimeMinutesWorked;
        Workspace.Briefing.ExpectedOvertimeMinutes = capacity.ExpectedOvertimeMinutes;
        Workspace.Briefing.BurnoutRisk = capacity.BurnoutRisk;
        Workspace.Briefing.ExpectedFinishAt = capacity.ExpectedFinishAt;
        Workspace.Briefing.CapacitySummary = BuildLiveCapacitySummary(capacity);
        var capacityAttentionKey = $"capacity:{DateTime.Today:yyyy-MM-dd}";
        if (capacity.IsOverCapacity)
        {
            RaiseAttention(capacityAttentionKey, "CAPACITY", capacity.BurnoutRisk,
                "TODAY IS OVER CAPACITY",
                $"{capacity.ExpectedOvertimeMinutes} minutes extend beyond the configured workday. Expected finish: {capacity.ExpectedFinishAt:HH:mm}.",
                "open_calendar");
        }
        else
        {
            ResolveAttention(capacityAttentionKey);
        }
        CapacityBreakdownText = $"Calendar {FormatCapacityDuration(capacity.ScheduledMinutesToday)} | " +
            $"Unscheduled {FormatCapacityDuration(capacity.PlannedMinutesToday)} | " +
            $"Workday {FormatCapacityDuration(capacity.WorkingMinutesToday)}";
        OnPropertyChanged(nameof(Workspace));
        OnPropertyChanged(nameof(HasCapacityPushbacks));
        OnPropertyChanged(nameof(BriefingSummary));
    }

    private static string FormatCapacityDuration(int minutes) =>
        minutes < 60 ? $"{minutes}m" : $"{minutes / 60}h {minutes % 60:00}m";

    private static string BuildLiveCapacitySummary(CapacitySummary capacity)
    {
        if (capacity.WorkdayEnd.Date == DateTime.Today && DateTime.Now >= capacity.WorkdayEnd)
            return $"Workday complete | {capacity.UtilisationPercent:F0}% of working hours booked.";
        if (capacity.IsWorkingOvertime)
            return $"Overtime: {capacity.OvertimeMinutesWorked} min worked · {capacity.ExpectedOvertimeMinutes} min still committed.";
        if (capacity.IsOverCapacity)
            return $"Expected finish {capacity.ExpectedFinishAt:HH:mm} · {capacity.ExpectedOvertimeMinutes} min beyond capacity.";
        return $"{capacity.RemainingMinutesToday} workable minutes remain · expected finish {capacity.ExpectedFinishAt:HH:mm}.";
    }}