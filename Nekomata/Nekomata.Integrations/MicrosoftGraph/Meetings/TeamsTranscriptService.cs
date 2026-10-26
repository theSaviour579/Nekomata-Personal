using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Nekomata.Integrations.MicrosoftGraph.Authentication;

namespace Nekomata.Integrations.MicrosoftGraph.Meetings;

public interface ITeamsTranscriptService
{
    /// <summary>True when the transcript permission is already granted (no prompt).</summary>
    Task<bool> HasConsentAsync(CancellationToken cancellationToken = default);
    /// <summary>Asks the user to grant the transcript permission (sign-in window). Only call when he asked to enable it.</summary>
    Task<bool> RequestConsentAsync(CancellationToken cancellationToken = default);
    /// <summary>The meeting's transcript as "Name: words" lines, or null if there isn't one (yet).</summary>
    Task<string?> GetTranscriptAsync(string joinUrl, DateTimeOffset meetingStart, CancellationToken cancellationToken = default);
}

// Teams transcripts through Microsoft Graph with the signed-in user's own permission. The scopes are requested on
// their own (incremental consent), so the main sign-in is unaffected if they're never granted. Graph only returns
// transcripts the user can see - in practice meetings he organised with transcription turned on.
public sealed partial class TeamsTranscriptService : ITeamsTranscriptService
{
    public static readonly string[] Scopes = ["OnlineMeetings.Read", "OnlineMeetingTranscript.Read.All"];
    private readonly HttpClient _httpClient;
    private readonly IMicrosoftAuthenticationService _authentication;

    public TeamsTranscriptService(HttpClient httpClient, IMicrosoftAuthenticationService authentication)
    {
        _httpClient = httpClient;
        _authentication = authentication;
    }

    public async Task<bool> HasConsentAsync(CancellationToken cancellationToken = default)
    {
        try { await _authentication.GetTokenForScopesAsync(Scopes, interactive: false, cancellationToken); return true; }
        catch { return false; }
    }

    public async Task<bool> RequestConsentAsync(CancellationToken cancellationToken = default)
    {
        try { await _authentication.GetTokenForScopesAsync(Scopes, interactive: true, cancellationToken); return true; }
        catch { return false; }
    }

    public async Task<string?> GetTranscriptAsync(string joinUrl, DateTimeOffset meetingStart, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(joinUrl)) return null;
        var token = (await _authentication.GetTokenForScopesAsync(Scopes, interactive: false, cancellationToken)).AccessToken;

        var filter = Uri.EscapeDataString($"JoinWebUrl eq '{joinUrl.Replace("'", "''")}'");
        var meetings = await GetAsync<GraphList<GraphOnlineMeeting>>($"me/onlineMeetings?$filter={filter}", token, cancellationToken);
        var meetingId = meetings?.Value.FirstOrDefault()?.Id;
        if (string.IsNullOrWhiteSpace(meetingId)) return null;

        var transcripts = await GetAsync<GraphList<GraphTranscript>>($"me/onlineMeetings/{Uri.EscapeDataString(meetingId)}/transcripts", token, cancellationToken);
        var transcript = transcripts?.Value
            .Where(t => t.CreatedDateTime is null || t.CreatedDateTime >= meetingStart.AddMinutes(-15))
            .OrderByDescending(t => t.CreatedDateTime).FirstOrDefault();
        if (transcript?.Id is null) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"me/onlineMeetings/{Uri.EscapeDataString(meetingId)}/transcripts/{Uri.EscapeDataString(transcript.Id)}/content?$format=text/vtt");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await GraphResponseDiagnostics.EnsureSuccessAsync(response, cancellationToken);
        return FromVtt(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>WebVTT with &lt;v Speaker&gt; voice tags to "Speaker: words", consecutive lines by the same person merged.</summary>
    public static string FromVtt(string vtt)
    {
        var lines = new List<(string Speaker, string Text)>();
        foreach (var raw in vtt.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line == "WEBVTT" || line.Contains("-->") || Regex.IsMatch(line, @"^[0-9a-f\-/]+$", RegexOptions.IgnoreCase)) continue;
            var voice = VoiceRegex().Match(line);
            var speaker = voice.Success ? voice.Groups["who"].Value.Trim() : lines.LastOrDefault().Speaker ?? "";
            var text = TagRegex().Replace(voice.Success ? voice.Groups["text"].Value : line, "").Trim();
            if (text.Length == 0) continue;
            if (lines.Count > 0 && lines[^1].Speaker == speaker) lines[^1] = (speaker, lines[^1].Text + " " + text);
            else lines.Add((speaker, text));
        }
        var builder = new StringBuilder();
        foreach (var (speaker, text) in lines) builder.AppendLine(speaker.Length > 0 ? $"{speaker}: {text}" : text);
        return builder.ToString().Trim();
    }

    private async Task<T?> GetAsync<T>(string resource, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, resource);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        await GraphResponseDiagnostics.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    private sealed class GraphList<T> { public List<T> Value { get; init; } = []; }
    private sealed class GraphOnlineMeeting { public string? Id { get; init; } }
    private sealed class GraphTranscript { public string? Id { get; init; } public DateTimeOffset? CreatedDateTime { get; init; } }

    [GeneratedRegex(@"^<v\s+(?<who>[^>]+)>(?<text>.*?)(?:</v>)?$")] private static partial Regex VoiceRegex();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex TagRegex();
}
