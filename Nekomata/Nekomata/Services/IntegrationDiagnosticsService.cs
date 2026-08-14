using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Diagnostics;
using Nekomata.Data.Database;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Services.Halo;
using Nekomata.Services.KnowBe4;

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
            await CheckAsync("PostgreSQL", CheckDatabaseAsync, cancellationToken),
            await CheckAsync("Backups", CheckBackupsAsync, cancellationToken),
            await CheckAsync("OpenAI", CheckOpenAiAsync, cancellationToken),
            await CheckAsync("Microsoft 365", CheckMicrosoftGraphAsync, cancellationToken),
            await CheckAsync("Halo", CheckHaloAsync, cancellationToken),
            await CheckAsync("KnowBe4", CheckKnowBe4Async, cancellationToken),
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

    private async Task<(string Summary, string Detail)> CheckDatabaseAsync(CancellationToken ct)
    {
        var database = _services.GetRequiredService<NekomataDbContext>();
        await using var connection = database.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(MAX(version), 0) FROM assistant.schema_migrations;";
        var version = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        return ($"Connected · schema v{version}", "PostgreSQL accepted a query successfully.");
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

    private async Task<(string Summary, string Detail)> CheckBackupsAsync(CancellationToken ct)
    {
        var status = await _services.GetRequiredService<DatabaseBackupService>().GetStatusAsync(ct);
        if (!status.ToolingAvailable) throw new InvalidOperationException(status.ToolingDetail);
        if (!status.AutomaticConfigured)
            throw new DiagnosticNotConfiguredException("Automatic encryption password is not configured; manual encrypted backups remain available.");
        if (status.LatestBackupAt is null)
            throw new InvalidOperationException("Automatic backup is configured, but no backup has completed yet.");
        if (!status.IsFresh)
            throw new InvalidOperationException($"Latest backup is stale ({status.LatestBackupAt:g}).");
        return ("Protected", status.FreshnessLabel + " · encrypted and verified before creation.");
    }

    private async Task<(string Summary, string Detail)> CheckMicrosoftGraphAsync(CancellationToken ct)
    {
        var calendar = _services.GetRequiredService<ICalendarService>();
        var start = DateTimeOffset.Now.Date;
        var events = await calendar.GetEventsAsync(start, start.AddDays(1), ct);
        return ("Connected", $"Calendar access succeeded · {events.Count} event(s) today.");
    }

    private async Task<(string Summary, string Detail)> CheckHaloAsync(CancellationToken ct)
    {
        var options = _services.GetRequiredService<HaloOptions>();
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            throw new DiagnosticNotConfiguredException("Credentials are not configured; the fake client is active.");
        var tickets = await _services.GetRequiredService<IHaloClient>().GetMyTicketsAsync(ct);
        return ("Connected", $"Ticket access succeeded · {tickets.Count} assigned ticket(s).");
    }

    private async Task<(string Summary, string Detail)> CheckKnowBe4Async(CancellationToken ct)
    {
        var options = _services.GetRequiredService<KnowBe4Options>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new DiagnosticNotConfiguredException("API key is not configured.");
        var client = _services.GetService<KnowBe4Client>() ??
            throw new DiagnosticNotConfiguredException("The KnowBe4 client is not enabled.");
        var failures = await client.GetRecentFailuresAsync(ct);
        return ("Connected", $"Security API access succeeded · {failures.Count} recent failure(s).");
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
