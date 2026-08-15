using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Diagnostics;
using Nekomata.Data.Local;
using Nekomata.Integrations.MicrosoftGraph.Calendar;

namespace Nekomata.UI.Services;

public sealed class IntegrationDiagnosticsService
{
    private static readonly HttpClient OpenAiHttp = new()
    {
        BaseAddress = new Uri("https://api.openai.com/"),
        Timeout = TimeSpan.FromSeconds(12)
    };

    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;

    public IntegrationDiagnosticsService(IServiceProvider services, IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<IntegrationDiagnosticItem>> RunAsync(
        CancellationToken cancellationToken = default)
    {
        var checks = new List<IntegrationDiagnosticItem>
        {
            await CheckAsync("Local workspace", CheckLocalWorkspaceAsync, cancellationToken),
            await CheckAsync("OpenAI", CheckOpenAiAsync, cancellationToken),
            await CheckAsync("Microsoft 365", CheckMicrosoftGraphAsync, cancellationToken),
            await CheckAsync("Spotify", CheckSpotifyAsync, cancellationToken)
        };
        return checks;
    }

    public static string FormatReport(IEnumerable<IntegrationDiagnosticItem> items)
    {
        var lines = new List<string>
        {
            "Nekomata integration diagnostics",
            $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Machine: {Environment.MachineName}",
            $"Application: {typeof(IntegrationDiagnosticsService).Assembly.GetName().Version}",
            string.Empty
        };
        foreach (var item in items)
        {
            lines.Add($"[{item.Status}] {item.Name} — {item.Summary}");
            if (!string.IsNullOrWhiteSpace(item.Detail))
                lines.Add($"  {DiagnosticTextSanitizer.Sanitize(item.Detail)}");
            lines.Add($"  {item.CheckedLabel}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private async Task<(string Summary, string Detail)> CheckLocalWorkspaceAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var store = _services.GetRequiredService<LocalWorkspaceStore>();
        await store.EnsureCreatedAsync();
        return ("Ready", "Tasks and projects are stored privately on this computer.");
    }

    private async Task<(string Summary, string Detail)> CheckOpenAiAsync(CancellationToken ct)
    {
        var key = _configuration["OpenAI:ApiKey"] ??
            Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            throw new DiagnosticNotConfiguredException("API key is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await OpenAiHttp.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI returned HTTP {(int)response.StatusCode}.");
        return ("Connected", "Authentication succeeded with a read-only models request.");
    }

    private async Task<(string Summary, string Detail)> CheckMicrosoftGraphAsync(CancellationToken ct)
    {
        var calendar = _services.GetRequiredService<ICalendarService>();
        var start = DateTimeOffset.Now.Date;
        var events = await calendar.GetEventsAsync(start, start.AddDays(1), ct);
        return ("Connected", $"Calendar access succeeded · {events.Count} event(s) today.");
    }

    private async Task<(string Summary, string Detail)> CheckSpotifyAsync(CancellationToken ct)
    {
        var spotify = _services.GetRequiredService<SpotifyPlaybackService>();
        if (!spotify.IsConfigured || !spotify.HasSavedConnection)
            throw new DiagnosticNotConfiguredException("Spotify has not been connected.");
        var state = await spotify.GetStateAsync(ct);
        if (!state.IsConnected)
            throw new InvalidOperationException(state.Status);
        var device = string.IsNullOrWhiteSpace(state.Device) ? "no active device" : state.Device;
        return ("Connected", $"{device} · {(state.IsPlaying ? "playing" : "paused")}.");
    }

    private static async Task<IntegrationDiagnosticItem> CheckAsync(
        string name,
        Func<CancellationToken, Task<(string Summary, string Detail)>> check,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var (summary, detail) = await check(cancellationToken);
            return new(name, "Healthy", summary, detail, DateTime.Now, stopwatch.ElapsedMilliseconds);
        }
        catch (DiagnosticNotConfiguredException ex)
        {
            return new(name, "Not configured", ex.Message, "This integration is optional.",
                DateTime.Now, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(name, "Attention", "Connection check failed", SafeMessage(ex),
                DateTime.Now, stopwatch.ElapsedMilliseconds);
        }
    }

    private static string SafeMessage(Exception exception)
    {
        return DiagnosticTextSanitizer.Sanitize(exception.GetBaseException().Message);
    }

    private sealed class DiagnosticNotConfiguredException(string message) : Exception(message);
}
