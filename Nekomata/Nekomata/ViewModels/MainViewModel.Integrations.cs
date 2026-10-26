using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using Nekomata.Core.Engines;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Models.Missions;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nekomata.UI.Services;
using Nekomata.UI.Views;
using Nekomata.Data.Repositories;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    public int MicrosoftToDoCount => Workspace.IntegrationMissionCandidates
        .Count(item => item.SourceType.Equals("Microsoft To Do", StringComparison.OrdinalIgnoreCase));

    public int MicrosoftPlannerCount => Workspace.IntegrationMissionCandidates
        .Count(item => item.SourceType.Equals("Microsoft Planner", StringComparison.OrdinalIgnoreCase));

    public bool IsMicrosoftTasksConnected => Workspace.Integrations
        .Any(item => item.Name.Equals("Microsoft Tasks", StringComparison.OrdinalIgnoreCase) && item.Connected);

    public string MicrosoftTasksIntegrationStatus
    {
        get
        {
            var status = Workspace.Integrations
                .FirstOrDefault(item => item.Name.Equals("Microsoft Tasks", StringComparison.OrdinalIgnoreCase));
            if (status is null) return "Connect Microsoft 365 to import To Do and Planner";
            if (!status.Connected) return status.Status == "Not connected"
                ? "Connect Microsoft 365 to import To Do and Planner"
                : $"Connection issue · {status.ErrorMessage}";
            return $"{status.Status} · synced {status.LastRefresh:HH:mm}";
        }
    }

    public bool IsSpotifyConfigured => _services.GetRequiredService<SpotifyPlaybackService>().IsConfigured;
    public string PreferredMediaProvider => string.IsNullOrWhiteSpace(_personalProfile.Current.PreferredMediaProvider)
        ? "Spotify" : _personalProfile.Current.PreferredMediaProvider;
    public string PreferredMediaTitle => PreferredMediaProvider.ToUpperInvariant();
    public Visibility SpotifyControlsVisibility => PreferredMediaProvider == "Spotify" ? Visibility.Visible : Visibility.Collapsed;
    public string PreferredMediaActionLabel => PreferredMediaProvider == "Spotify" ? "OPEN SPOTIFY" : $"OPEN {PreferredMediaProvider.ToUpperInvariant()}";

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

    private async Task InitialisePreferredMediaAsync()
    {
        if (PreferredMediaProvider == "Spotify")
        {
            await InitialiseSpotifyArrivalAsync();
            return;
        }
        _spotifyStateTimer?.Stop();
        SpotifyIntegrationStatus = PreferredMediaProvider switch
        {
            "Apple Music" => "Apple Music selected · ready to open your saved page",
            "YouTube Music" => "YouTube Music selected · ready to open your saved page",
            _ => "Radio selected · ready to open your saved station"
        };
        SpotifyTrack = DisplayMediaAddress(PreferredMediaProvider switch
        {
            "Apple Music" => _personalProfile.Current.AppleMusicUrl,
            "YouTube Music" => _personalProfile.Current.YouTubeMusicUrl,
            _ => _personalProfile.Current.RadioStationUrl
        });
        SpotifyArtist = "Browser playback · controls remain with the selected service";
        SpotifyDevice = string.Empty;
    }

    [RelayCommand]
    private void OpenPreferredMedia()
    {
        try
        {
            if (PreferredMediaProvider == "Spotify") { OpenSpotify(); return; }
            var value = PreferredMediaProvider switch
            {
                "Apple Music" => _personalProfile.Current.AppleMusicUrl,
                "YouTube Music" => _personalProfile.Current.YouTubeMusicUrl,
                _ => _personalProfile.Current.RadioStationUrl
            };
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                throw new InvalidOperationException("Add a valid link in Personal settings first.");
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) { SpotifyIntegrationStatus = $"Could not open {PreferredMediaProvider} · {ex.Message}"; }
    }

    private static string DisplayMediaAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.Host + uri.AbsolutePath.TrimEnd('/') : "No link configured";

    private async Task InitialiseSpotifyArrivalAsync()
    {
        var spotify = _services.GetRequiredService<SpotifyPlaybackService>();
        _spotifyStateTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _spotifyStateTimer.Tick -= SpotifyStateTimerTick;
        _spotifyStateTimer.Tick += SpotifyStateTimerTick;
        _spotifyStateTimer.Start();
        var currentState = await RefreshSpotifyStateAsync();
        if (!spotify.HasSavedConnection) return;
        if (IsDifferentPlaylistPlaying(currentState, spotify))
        {
            await RaiseSpotifyArrivalPromptAsync(currentState!);
            return;
        }
        try
        {
            SpotifyBusy = true;
            SpotifyIntegrationStatus = "Starting the Arrival Mix on shuffle...";
            await spotify.StartArrivalAsync();
        }
        catch (Exception ex) { SpotifyIntegrationStatus = $"Arrival Mode waiting - {ex.Message}"; }
        finally { SpotifyBusy = false; await RefreshSpotifyStateAsync(); }
    }

    private static bool IsDifferentPlaylistPlaying(SpotifyPlaybackState? state, SpotifyPlaybackService spotify) =>
        state is { IsPlaying: true, ContextUri: { Length: > 0 } contextUri } &&
        !contextUri.Equals(spotify.ArrivalPlaylistUri, StringComparison.OrdinalIgnoreCase);

    // Ambient, not a modal: Guardian mentions it once by voice and leaves the actual
    // decision as an Attention Centre item, so an unattended launch never sits blocked
    // on a Windows dialog nobody is there to answer.
    private async Task RaiseSpotifyArrivalPromptAsync(SpotifyPlaybackState state)
    {
        var nowPlaying = string.IsNullOrWhiteSpace(state.Artist) ? state.Track : $"{state.Track} - {state.Artist}";
        SpotifyIntegrationStatus = $"Keeping \"{state.Track}\" playing \u00b7 swap from Attention if you want the Arrival Mix.";
        RaiseAttention(
            "spotify:arrival-swap",
            "Spotify",
            "Info",
            "Swap to the Arrival Mix?",
            $"\"{nowPlaying}\" was already playing when Nekomata started. Approve to switch to the Arrival Mix on shuffle, or resolve this to keep it playing.",
            "spotify_arrival_swap",
            expiresAt: DateTimeOffset.Now.AddHours(3));
        await SpeakGuardianAsync(
            $"You're already listening to {nowPlaying}. I'll leave it running - there's an Attention item if you'd like the Arrival Mix instead.");
    }

    private async Task SwapToArrivalMixFromAttentionAsync(GuardianAttentionItem item)
    {
        ResolveAttention(item.Key);
        var spotify = _services.GetRequiredService<SpotifyPlaybackService>();
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
                OnPropertyChanged(nameof(SpotifyConnectLabel));
            }

            // Connecting an account and choosing an Arrival Mix are separate jobs.
            // OAuth must always happen first so CONNECT SPOTIFY never looks like a
            // playlist-setting button.
            if (!spotify.HasArrivalPlaylist)
            {
                SpotifyBusy = false;
                SpotifyIntegrationStatus = "Spotify connected · choose an Arrival Mix to start playback";
                if (!ConfigureSpotifyArrivalMix()) return;
                SpotifyBusy = true;
            }

            SpotifyIntegrationStatus = "Starting the Arrival Mix on shuffle...";
            await spotify.StartArrivalAsync();
        }
        catch (Exception ex) { SpotifyIntegrationStatus = $"Spotify connection issue - {ex.Message}"; }
        finally { SpotifyBusy = false; OnPropertyChanged(nameof(SpotifyConnectLabel)); await RefreshSpotifyStateAsync(); }
    }

    [RelayCommand]
    private void ChangeSpotifyArrivalMix()
    {
        if (ConfigureSpotifyArrivalMix())
            SpotifyIntegrationStatus = "Arrival Mix saved · use Play Arrival Mix when ready";
    }

    private bool ConfigureSpotifyArrivalMix()
    {
        var window = new SpotifyArrivalMixWindow(_services.GetRequiredService<SpotifyPlaybackService>())
        {
            Owner = Application.Current.MainWindow
        };
        return window.ShowDialog() == true;
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

    private async Task<SpotifyPlaybackState?> RefreshSpotifyStateAsync()
    {
        if (SpotifyBusy) return null;
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
        return state;
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

    private bool _integrationRefreshBusy;

    private void InitialiseIntegrationRefresh()
    {
        // Background refresh runs on its own timers. This is the explicit, on-demand refresh.
    }

    [RelayCommand]
    private async Task RefreshIntegrationsAsync()
    {
        if (_integrationRefreshBusy) return;
        _integrationRefreshBusy = true;
        try
        {
            var previousWorkspace=Workspace;
            Workspace = await _workspaceCoordinator.RefreshAsync();
            await RefreshProjectCommunicationAttentionAsync();
            await RefreshCalendarAsync();
            var reconciliation=await _services.GetRequiredService<Nekomata.Core.Guardian.Reconciliation.GuardianDayReconciliationService>()
                .ReconcileAsync(previousWorkspace,Workspace,DateTimeOffset.Now);
            var exceptions=await _services.GetRequiredService<Nekomata.Core.Guardian.Exceptions.GuardianExceptionManagementService>()
                .EvaluateAsync(reconciliation,Workspace,DateTimeOffset.Now);
            foreach(var item in exceptions.Exceptions)
                RaiseAttention(item.Key,"Guardian",item.Severity,item.Title,item.Detail,item.ActionKind,item.ContextId);
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
            RefreshContextualBriefings();
        }
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
