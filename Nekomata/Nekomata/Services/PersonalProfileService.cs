using System.IO;
using System.Text.Json;
using Nekomata.Models.Analytics;

namespace Nekomata.UI.Services;

public sealed record PersonalProfile
{
    public string DisplayName { get; init; } = string.Empty;
    public bool StartWithWindows { get; init; } = true;
    public string SpotifyArrivalPlaylistUri { get; init; } = string.Empty;
    public string SpotifyClientId { get; init; } = string.Empty;
    public string PreferredMediaProvider { get; init; } = "Spotify";
    public string YouTubeMusicUrl { get; init; } = "https://music.youtube.com";
    public string RadioStationUrl { get; init; } = string.Empty;
    public string AzureOpenAIEndpoint { get; init; } = string.Empty;
    public string AzureOpenAIDeployment { get; init; } = string.Empty;
    public string ConversationProvider { get; init; } = "Automatic";
    public bool CopilotWebSearchEnabled { get; init; }
    public string JobTitle { get; init; } = string.Empty;
    public string JobTitleSource { get; init; } = "Manual";
    public PersonalRoleProfile? RoleProfile { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class PersonalProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _profilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nekomata Personal",
        "profile.json");

    public PersonalProfile Current { get; private set; }
    public string FilePath => _profilePath;

    public PersonalProfileService()
    {
        Current = Load();
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Current.DisplayName);

    public void Reload() => Current = Load();

    public void Save(string displayName, bool startWithWindows)
    {
        var cleanName = displayName.Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
            throw new ArgumentException("Please enter your name.", nameof(displayName));

        Current = new PersonalProfile
        {
            DisplayName = cleanName,
            StartWithWindows = startWithWindows,
            SpotifyArrivalPlaylistUri = Current.SpotifyArrivalPlaylistUri,
            SpotifyClientId = Current.SpotifyClientId,
            PreferredMediaProvider = Current.PreferredMediaProvider,
            YouTubeMusicUrl = Current.YouTubeMusicUrl,
            RadioStationUrl = Current.RadioStationUrl,
            AzureOpenAIEndpoint = Current.AzureOpenAIEndpoint,
            AzureOpenAIDeployment = Current.AzureOpenAIDeployment,
            ConversationProvider = Current.ConversationProvider,
            CopilotWebSearchEnabled = Current.CopilotWebSearchEnabled,
            JobTitle = Current.JobTitle,
            JobTitleSource = Current.JobTitleSource,
            RoleProfile = Current.RoleProfile,
            CreatedAt = Current.CreatedAt
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(Current, JsonOptions));
    }

    public void SaveJobTitle(string jobTitle)
    {
        jobTitle = jobTitle.Trim();
        var keepGoals = string.Equals(jobTitle, Current.JobTitle, StringComparison.OrdinalIgnoreCase);
        Current = Current with { JobTitle = jobTitle, JobTitleSource = "Manual", RoleProfile = keepGoals ? Current.RoleProfile : null };
        Persist();
    }

    public void SaveRoleProfile(PersonalRoleProfile roleProfile)
    {
        Current = Current with { JobTitle = roleProfile.JobTitle, JobTitleSource = roleProfile.Source, RoleProfile = roleProfile };
        Persist();
    }

    public void SaveSpotifyArrivalPlaylist(string playlistUri)
    {
        Current = Current with { SpotifyArrivalPlaylistUri = playlistUri.Trim() };
        Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(Current, JsonOptions));
    }

    public void SaveSpotifyClientId(string clientId)
    {
        Current = Current with { SpotifyClientId = clientId.Trim() };
        Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(Current, JsonOptions));
    }

    public void SaveMediaPreference(string provider, string youtubeMusicUrl, string radioStationUrl)
    {
        provider = provider.Trim();
        if (provider is not ("Spotify" or "YouTube Music" or "Radio"))
            throw new ArgumentException("Choose Spotify, YouTube Music or Radio.");
        youtubeMusicUrl = ValidateMediaUrl(youtubeMusicUrl, "YouTube Music", required: provider == "YouTube Music");
        radioStationUrl = ValidateMediaUrl(radioStationUrl, "radio station", required: provider == "Radio");
        if (youtubeMusicUrl.Length > 0 && !new Uri(youtubeMusicUrl).Host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The YouTube Music link must use music.youtube.com.");
        Current = Current with
        {
            PreferredMediaProvider = provider,
            YouTubeMusicUrl = youtubeMusicUrl,
            RadioStationUrl = radioStationUrl
        };
        Persist();
    }

    private static string ValidateMediaUrl(string value, string label, bool required)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            if (required) throw new ArgumentException($"Enter a link for the selected {label} source.");
            return string.Empty;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new ArgumentException($"The {label} link must be a valid HTTP or HTTPS address.");
        return uri.AbsoluteUri;
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(Current, JsonOptions));
    }

    public void SaveAzureOpenAI(string endpoint, string deployment)
    {
        endpoint = endpoint.Trim().TrimEnd('/');
        deployment = deployment.Trim();
        if ((endpoint.Length == 0) != (deployment.Length == 0))
            throw new ArgumentException("Enter both the Microsoft AI endpoint and deployment name, or leave both blank.");
        if (endpoint.Length > 0 && (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("The Microsoft AI endpoint must be a valid HTTPS address.");
        Current = Current with { AzureOpenAIEndpoint = endpoint, AzureOpenAIDeployment = deployment };
        Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(Current, JsonOptions));
    }

    public void SaveConversationProvider(string provider, bool copilotWebSearchEnabled)
    {
        provider = provider.Trim();
        if (provider is not ("Automatic" or "Microsoft 365 Copilot (Preview)" or "Microsoft AI (Entra)" or "OpenAI" or "No AI"))
            throw new ArgumentException("Choose a supported conversation provider.");
        Current = Current with { ConversationProvider = provider, CopilotWebSearchEnabled = copilotWebSearchEnabled };
        Persist();
    }

    private PersonalProfile Load()
    {
        try
        {
            return File.Exists(_profilePath)
                ? JsonSerializer.Deserialize<PersonalProfile>(File.ReadAllText(_profilePath)) ?? new PersonalProfile()
                : new PersonalProfile();
        }
        catch
        {
            return new PersonalProfile();
        }
    }
}
