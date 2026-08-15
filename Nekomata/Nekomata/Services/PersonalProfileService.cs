using System.IO;
using System.Text.Json;

namespace Nekomata.UI.Services;

public sealed record PersonalProfile
{
    public string DisplayName { get; init; } = string.Empty;
    public bool StartWithWindows { get; init; } = true;
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
            CreatedAt = Current.CreatedAt
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(Current, JsonOptions));
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
