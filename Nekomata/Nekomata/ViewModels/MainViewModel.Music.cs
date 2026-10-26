using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NAudio.CoreAudioApi;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

// Music, through the existing Spotify connection (Premium; it controls whichever device Spotify is active on):
//   * commands: play / pause / next / previous / shuffle / volume / what's playing / play <artist, playlist, song> / focus music;
//   * a call starting (any app holding the mic) pauses Spotify, and it resumes when the call ends;
//   * while Guardian speaks, Spotify is turned down on this PC (its Windows volume, so no Spotify call is needed)
//     and brought back afterwards.
// Settings switch: "music" (the call pause and the dip while speaking).
public partial class MainViewModel
{
    private bool _musicPausedForCall;
    private float? _musicDuckedFrom;

    private async Task MusicAsync(string action, string detail)
    {
        var spotify = _services.GetRequiredService<SpotifyPlaybackService>();
        if (!spotify.IsConfigured || !spotify.HasSavedConnection)
        {
            await RespondAndSpeakAsync("Spotify isn't connected yet. Use Connect Spotify on the home page, then ask again.");
            return;
        }
        try
        {
            switch (action)
            {
                case "play":
                    SetMusicPlaying(true);
                    await RespondAndSpeakAsync($"Playing {await spotify.PlayAsync()}.");
                    return;
                case "focus":
                    SetMusicPlaying(true);
                    await RespondAndSpeakAsync($"Playing {await spotify.PlayFocusAsync()}.");
                    return;
                case "search":
                    var label = await spotify.PlaySearchAsync(detail);
                    SetMusicPlaying(true);
                    await RespondAndSpeakAsync($"Playing {label}.");
                    return;
                case "pause":
                    await spotify.PauseAsync();
                    SetMusicPlaying(false);
                    ShowPaletteResponse("Music paused.");
                    return;
                case "next":
                case "previous":
                    if (action == "next") await spotify.NextAsync(); else await spotify.PreviousAsync();
                    await Task.Delay(700);
                    var now = await spotify.GetStateAsync();
                    ShowPaletteResponse(now.Track == "Nothing playing" ? "Skipped." : $"Now playing {now.Track}{(now.Artist.Length > 0 ? $" by {now.Artist}" : "")}.");
                    return;
                case "shuffle":
                    await spotify.SetShuffleAsync(detail == "on");
                    ShowPaletteResponse(detail == "on" ? "Shuffle on." : "Shuffle off.");
                    return;
                case "volume":
                    var state = await spotify.GetStateAsync();
                    var target = detail.StartsWith('+') || detail.StartsWith('-')
                        ? state.VolumePercent + int.Parse(detail)
                        : int.TryParse(detail, out var v) ? v : state.VolumePercent;
                    target = Math.Clamp(target, 0, 100);
                    await spotify.SetVolumeAsync(target);
                    ShowPaletteResponse($"Volume {target}%.");
                    return;
                case "now":
                    var playing = await spotify.GetStateAsync();
                    await RespondAndSpeakAsync(!playing.IsConnected ? "Spotify isn't open anywhere at the moment."
                        : playing.Track == "Nothing playing" ? "Nothing's playing."
                        : $"{(playing.IsPlaying ? "This is" : "Paused on")} {playing.Track}{(playing.Artist.Length > 0 ? $" by {playing.Artist}" : "")}.");
                    return;
            }
        }
        catch (Exception ex)
        {
            await RespondAndSpeakAsync($"Spotify didn't manage that: {ex.GetBaseException().Message}");
        }
        finally
        {
            // Keep the now-playing strip on Home in step with what the command did.
            await SafeAsync(async () => { await RefreshSpotifyStateAsync(); return true; }, false);
        }
    }

    private void SetMusicPlaying(bool playing)
    {
        SpotifyIsPlaying = playing;
        OnPropertyChanged(nameof(SpotifyPlayPauseLabel));
    }

    // Called by the call guard.
    private async Task PauseMusicForCallAsync()
    {
        if (!FeatureOn("music")) return;
        var spotify = _services.GetRequiredService<SpotifyPlaybackService>();
        if (!spotify.HasSavedConnection) return;
        try
        {
            var state = await spotify.GetStateAsync();
            if (!state.IsPlaying) return;
            await spotify.PauseAsync();
            _musicPausedForCall = true;
            SetMusicPlaying(false);
        }
        catch (Exception ex) { Debug.WriteLine($"Music pause for call failed safely: {ex.Message}"); }
    }

    private async Task ResumeMusicAfterCallAsync()
    {
        if (!_musicPausedForCall) return;
        _musicPausedForCall = false;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (LiveCallReason() is not null) { _musicPausedForCall = true; return; }
            await _services.GetRequiredService<SpotifyPlaybackService>().ResumeAsync();
            SetMusicPlaying(true);
        }
        catch (Exception ex) { Debug.WriteLine($"Music resume after call failed safely: {ex.Message}"); }
    }

    /// <summary>Turns Spotify down on this PC while Guardian speaks, and back up afterwards.</summary>
    private void DuckMusic(bool duck)
    {
        if (!FeatureOn("music") || (duck && _musicDuckedFrom is not null) || (!duck && _musicDuckedFrom is null)) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                string name;
                try { name = Process.GetProcessById((int)session.GetProcessID).ProcessName; }
                catch { continue; }
                if (!name.Equals("Spotify", StringComparison.OrdinalIgnoreCase)) continue;
                if (duck)
                {
                    if (session.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) continue;
                    _musicDuckedFrom ??= session.SimpleAudioVolume.Volume;
                    session.SimpleAudioVolume.Volume = Math.Min(session.SimpleAudioVolume.Volume, 0.2f);
                }
                else if (_musicDuckedFrom is { } volume) session.SimpleAudioVolume.Volume = volume;
            }
            if (!duck) _musicDuckedFrom = null;
        }
        catch (Exception ex) { Debug.WriteLine($"Music dip failed safely: {ex.Message}"); _musicDuckedFrom = duck ? _musicDuckedFrom : null; }
    }
}
