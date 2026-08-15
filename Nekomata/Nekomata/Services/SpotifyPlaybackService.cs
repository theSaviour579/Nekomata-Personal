using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nekomata.UI.Services;

public sealed record SpotifyPlaybackState(
    bool IsConnected,
    bool IsPlaying,
    bool ShuffleEnabled,
    int VolumePercent,
    string Track,
    string Artist,
    string Device,
    string Status);

public sealed class SpotifyPlaybackService
{
    private const string DefaultPlaylist = "spotify:playlist:0S78UVuLW857NQ2FaUYwTD";
    private const string DefaultRedirect = "http://127.0.0.1:43821/callback/";
    private const string Scopes = "user-read-playback-state user-modify-playback-state";
    private readonly HttpClient _http = new();
    private readonly string _clientId;
    private readonly string _playlistUri;
    private readonly string _redirectUri;
    private readonly string _tokenPath;
    private SpotifyToken? _token;

    public SpotifyPlaybackService(IConfiguration configuration)
    {
        _clientId = configuration["Spotify:ClientId"] ?? string.Empty;
        _playlistUri = configuration["Spotify:ArrivalPlaylistUri"] ?? DefaultPlaylist;
        _redirectUri = configuration["Spotify:RedirectUri"] ?? DefaultRedirect;
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal");
        Directory.CreateDirectory(folder);
        _tokenPath = Path.Combine(folder, "spotify-token.json");
        LoadToken();
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_clientId);
    public bool HasSavedConnection => _token is not null;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Spotify ClientId is not configured.");
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var callback = new Uri(_redirectUri);
        using var listener = new HttpListener();
        listener.Prefixes.Add(_redirectUri);
        listener.Start();

        var auth = "https://accounts.spotify.com/authorize?" + BuildQuery(new Dictionary<string, string>
        {
            ["client_id"] = _clientId, ["response_type"] = "code", ["redirect_uri"] = _redirectUri,
            ["scope"] = Scopes, ["code_challenge_method"] = "S256", ["code_challenge"] = challenge,
            ["state"] = state, ["show_dialog"] = "true"
        });
        Process.Start(new ProcessStartInfo(auth) { UseShellExecute = true });

        var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(3), cancellationToken);
        var code = context.Request.QueryString["code"];
        var returnedState = context.Request.QueryString["state"];
        var error = context.Request.QueryString["error"];
        var html = string.IsNullOrWhiteSpace(error)
            ? "<h2>Nekomata is connected to Spotify.</h2><p>You can close this window.</p>"
            : "<h2>Spotify connection was not completed.</h2><p>You can close this window.</p>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
        if (!string.IsNullOrWhiteSpace(error)) throw new InvalidOperationException($"Spotify authorization failed: {error}");
        if (returnedState != state || string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Spotify authorization response could not be verified.");

        using var response = await _http.PostAsync("https://accounts.spotify.com/api/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId, ["grant_type"] = "authorization_code", ["code"] = code,
                ["redirect_uri"] = _redirectUri, ["code_verifier"] = verifier
            }), cancellationToken);
        await SetTokenFromResponseAsync(response, preserveRefreshToken: null, cancellationToken);
    }

    public async Task StartArrivalAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTokenAsync(cancellationToken);
        var deviceId = await FindAvailableDeviceAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            TryStartSpotifyMinimized();

            // The desktop client takes a moment to register as a Connect device.
            for (var attempt = 0; attempt < 6 && string.IsNullOrWhiteSpace(deviceId); attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
                deviceId = await FindAvailableDeviceAsync(cancellationToken);
            }
        }

        if (string.IsNullOrWhiteSpace(deviceId))
            throw new InvalidOperationException(
                "No Spotify playback device is available. Start Spotify once, then try again.");

        await SendAsync(HttpMethod.Put, $"me/player/shuffle?state=true{DeviceQuery(deviceId)}", null, cancellationToken);
        await SendAsync(HttpMethod.Put, $"me/player/play{DeviceQuery(deviceId, first: true)}",
            JsonSerializer.Serialize(new { context_uri = _playlistUri }), cancellationToken);
    }

    public Task PauseAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Put, "me/player/pause", null, ct);
    public Task ResumeAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Put, "me/player/play", null, ct);
    public Task NextAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Post, "me/player/next", null, ct);
    public Task PreviousAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Post, "me/player/previous", null, ct);
    public Task SetShuffleAsync(bool enabled, CancellationToken ct = default) => SendAsync(HttpMethod.Put, $"me/player/shuffle?state={enabled.ToString().ToLowerInvariant()}", null, ct);
    public Task SetVolumeAsync(int volume, CancellationToken ct = default) => SendAsync(HttpMethod.Put, $"me/player/volume?volume_percent={Math.Clamp(volume, 0, 100)}", null, ct);

    public async Task<SpotifyPlaybackState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return Empty("Client ID required");
        if (_token is null) return Empty("Connect Spotify to enable Arrival Mode");
        try
        {
            using var response = await ApiAsync(HttpMethod.Get, "me/player", null, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent) return Empty("Spotify is connected · no active device", true);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;
            var device = root.TryGetProperty("device", out var d) ? d : default;
            var item = root.TryGetProperty("item", out var i) ? i : default;
            var artists = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("artists", out var a)
                ? string.Join(", ", a.EnumerateArray().Select(x => x.GetProperty("name").GetString())) : string.Empty;
            return new(true,
                root.TryGetProperty("is_playing", out var playing) && playing.GetBoolean(),
                root.TryGetProperty("shuffle_state", out var shuffle) && shuffle.GetBoolean(),
                device.ValueKind == JsonValueKind.Object && device.TryGetProperty("volume_percent", out var volume) ? volume.GetInt32() : 50,
                item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var name) ? name.GetString() ?? "Nothing playing" : "Nothing playing",
                artists,
                device.ValueKind == JsonValueKind.Object && device.TryGetProperty("name", out var deviceName) ? deviceName.GetString() ?? string.Empty : string.Empty,
                "Premium connected · Arrival Mode ready");
        }
        catch (Exception ex) { return Empty($"Spotify unavailable · {ex.Message}", _token is not null); }
    }

    private static SpotifyPlaybackState Empty(string status, bool connected = false) => new(connected, false, false, 50, "Nothing playing", string.Empty, string.Empty, status);

    private async Task<string?> FindAvailableDeviceAsync(CancellationToken ct)
    {
        using var response = await ApiAsync(HttpMethod.Get, "me/player/devices", null, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var devices = json.RootElement.GetProperty("devices").EnumerateArray().ToList();
        var active = devices.FirstOrDefault(x => x.GetProperty("is_active").GetBoolean());
        var selected = active.ValueKind == JsonValueKind.Object ? active : devices.FirstOrDefault(x => !x.GetProperty("is_restricted").GetBoolean());
        if (selected.ValueKind != JsonValueKind.Object)
            return null;

        var id = selected.GetProperty("id").GetString();
        if (active.ValueKind != JsonValueKind.Object && !string.IsNullOrWhiteSpace(id))
            await SendAsync(HttpMethod.Put, "me/player", JsonSerializer.Serialize(new { device_ids = new[] { id }, play = false }), ct);
        return id;
    }

    private async Task SendAsync(HttpMethod method, string path, string? json, CancellationToken ct)
    {
        using var response = await ApiAsync(method, path, json, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("No active Spotify device was found.");
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> ApiAsync(HttpMethod method, string path, string? json, CancellationToken ct)
    {
        await EnsureTokenAsync(ct);
        var request = new HttpRequestMessage(method, $"https://api.spotify.com/v1/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token!.AccessToken);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
        response.Dispose();
        await RefreshTokenAsync(ct);
        request = new HttpRequestMessage(method, $"https://api.spotify.com/v1/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token!.AccessToken);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _http.SendAsync(request, ct);
    }

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (_token is null) throw new InvalidOperationException("Connect Spotify first.");
        if (_token.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1)) await RefreshTokenAsync(ct);
    }

    private async Task RefreshTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_token?.RefreshToken)) throw new InvalidOperationException("Reconnect Spotify to continue.");
        using var response = await _http.PostAsync("https://accounts.spotify.com/api/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = _token.RefreshToken
            }), ct);
        await SetTokenFromResponseAsync(response, _token.RefreshToken, ct);
    }

    private async Task SetTokenFromResponseAsync(HttpResponseMessage response, string? preserveRefreshToken, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Spotify token request failed ({(int)response.StatusCode}).");
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        _token = new SpotifyToken(
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : preserveRefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
        await File.WriteAllTextAsync(_tokenPath, JsonSerializer.Serialize(_token), ct);
    }

    private void LoadToken()
    {
        try { if (File.Exists(_tokenPath)) _token = JsonSerializer.Deserialize<SpotifyToken>(File.ReadAllText(_tokenPath)); }
        catch { _token = null; }
    }

    private static void TryStartSpotifyMinimized()
    {
        if (Process.GetProcessesByName("Spotify").Length > 0)
            return;

        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Spotify",
                "Spotify.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WindowsApps",
                "Spotify.exe")
        };

        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized
            });
        }
        catch
        {
            // The caller will report that no playback device became available.
        }
    }

    private static string DeviceQuery(string? id, bool first = false) => string.IsNullOrWhiteSpace(id) ? string.Empty : $"{(first ? '?' : '&')}device_id={Uri.EscapeDataString(id)}";
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> values) => string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    private sealed record SpotifyToken(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);
}
