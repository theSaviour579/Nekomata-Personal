using System.IO;
using System.Text.Json;
using Nekomata.Models.Analytics;
using Nekomata.Models.Planning;

namespace Nekomata.UI.Services;

public sealed record PersonalProfile
{
    public string DisplayName { get; init; } = string.Empty;
    public bool StartWithWindows { get; init; } = true;
    public string SpotifyArrivalPlaylistUri { get; init; } = string.Empty;
    public string SpotifyClientId { get; init; } = string.Empty;
    public string PreferredMediaProvider { get; init; } = "Spotify";
    public string YouTubeMusicUrl { get; init; } = "https://music.youtube.com";
    public string AppleMusicUrl { get; init; } = "https://music.apple.com";
    public string RadioStationUrl { get; init; } = string.Empty;
    public string AzureOpenAIEndpoint { get; init; } = string.Empty;
    public string AzureOpenAIDeployment { get; init; } = string.Empty;
    public string ConversationProvider { get; init; } = "Automatic";
    public bool CopilotWebSearchEnabled { get; init; }
    public string JobTitle { get; init; } = string.Empty;
    public string JobTitleSource { get; init; } = "Manual";
    public PersonalRoleProfile? RoleProfile { get; init; }
    public bool WorkScheduleConfigured { get; init; }
    public TimeSpan WorkdayStart { get; init; }
    public TimeSpan WorkdayEnd { get; init; }
    public bool IncludeLunchBreak { get; init; } = true;
    public TimeSpan LunchStart { get; init; }
    public TimeSpan LunchEnd { get; init; }
    public TimeSpan WrapUpTime { get; init; }
    public bool EmailBriefingEnabled { get; init; }
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
            AppleMusicUrl = Current.AppleMusicUrl,
            RadioStationUrl = Current.RadioStationUrl,
            AzureOpenAIEndpoint = Current.AzureOpenAIEndpoint,
            AzureOpenAIDeployment = Current.AzureOpenAIDeployment,
            ConversationProvider = Current.ConversationProvider,
            CopilotWebSearchEnabled = Current.CopilotWebSearchEnabled,
            JobTitle = Current.JobTitle,
            JobTitleSource = Current.JobTitleSource,
            RoleProfile = Current.RoleProfile,
            WorkScheduleConfigured = Current.WorkScheduleConfigured,
            WorkdayStart = Current.WorkdayStart,
            WorkdayEnd = Current.WorkdayEnd,
            IncludeLunchBreak = Current.IncludeLunchBreak,
            LunchStart = Current.LunchStart,
            LunchEnd = Current.LunchEnd,
            WrapUpTime = Current.WrapUpTime,
            EmailBriefingEnabled = Current.EmailBriefingEnabled,
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

    public WorkingDaySettings CreateWorkingDaySettings()
    {
        var profile = Current;
        return profile.WorkScheduleConfigured
            ? new WorkingDaySettings
            {
                StartTime = profile.WorkdayStart,
                EndTime = profile.WorkdayEnd,
                IncludeLunchBreak = profile.IncludeLunchBreak,
                LunchStartTime = profile.LunchStart,
                LunchDurationMinutes = profile.IncludeLunchBreak
                    ? Math.Max(0, (int)(profile.LunchEnd - profile.LunchStart).TotalMinutes)
                    : 0
            }
            : new WorkingDaySettings();
    }

    public void SaveWorkSchedule(
        TimeSpan workdayStart,
        TimeSpan workdayEnd,
        bool includeLunchBreak,
        TimeSpan lunchStart,
        TimeSpan lunchEnd,
        TimeSpan wrapUpTime,
        bool emailBriefingEnabled)
    {
        if (workdayStart < TimeSpan.Zero || workdayStart >= TimeSpan.FromDays(1) ||
            workdayEnd <= workdayStart || workdayEnd > TimeSpan.FromDays(1))
            throw new ArgumentException("Working hours must have a valid start and a later finish time.");
        if (includeLunchBreak &&
            (lunchStart < workdayStart || lunchEnd <= lunchStart || lunchEnd > workdayEnd))
            throw new ArgumentException("Lunch must start and finish within your working hours.");
        if (wrapUpTime < workdayStart || wrapUpTime > workdayEnd)
            throw new ArgumentException("Choose a wrap-up time within your working hours.");

        Current = Current with
        {
            WorkScheduleConfigured = true,
            WorkdayStart = workdayStart,
            WorkdayEnd = workdayEnd,
            IncludeLunchBreak = includeLunchBreak,
            LunchStart = includeLunchBreak ? lunchStart : TimeSpan.Zero,
            LunchEnd = includeLunchBreak ? lunchEnd : TimeSpan.Zero,
            WrapUpTime = wrapUpTime,
            EmailBriefingEnabled = emailBriefingEnabled
        };
        Persist();
    }

    public void ApplyWorkSchedule(WorkingDaySettings settings)
    {
        var configured = CreateWorkingDaySettings();
        settings.StartTime = configured.StartTime;
        settings.EndTime = configured.EndTime;
        settings.IncludeLunchBreak = configured.IncludeLunchBreak;
        settings.LunchStartTime = configured.LunchStartTime;
        settings.LunchDurationMinutes = configured.LunchDurationMinutes;
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

    public void SaveMediaPreference(string provider, string youtubeMusicUrl, string appleMusicUrl, string radioStationUrl)
    {
        provider = provider.Trim();
        if (provider is not ("Spotify" or "Apple Music" or "YouTube Music" or "Radio"))
            throw new ArgumentException("Choose Spotify, Apple Music, YouTube Music or Radio.");
        youtubeMusicUrl = ValidateMediaUrl(youtubeMusicUrl, "YouTube Music", required: provider == "YouTube Music");
        appleMusicUrl = ValidateMediaUrl(appleMusicUrl, "Apple Music", required: provider == "Apple Music");
        radioStationUrl = ValidateMediaUrl(radioStationUrl, "radio station", required: provider == "Radio");
        if (youtubeMusicUrl.Length > 0 && !new Uri(youtubeMusicUrl).Host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The YouTube Music link must use music.youtube.com.");
        if (appleMusicUrl.Length > 0 && !new Uri(appleMusicUrl).Host.Equals("music.apple.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Apple Music link must use music.apple.com.");
        Current = Current with
        {
            PreferredMediaProvider = provider,
            YouTubeMusicUrl = youtubeMusicUrl,
            AppleMusicUrl = appleMusicUrl,
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
