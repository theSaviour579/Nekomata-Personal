namespace Nekomata.AI.Models;

public class GuardianChatTurn : System.ComponentModel.INotifyPropertyChanged
{
    private int _rating;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>the user's rating of an answer: 1 thumbs up, -1 thumbs down, 0 none.</summary>
    public int Rating
    {
        get => _rating;
        set
        {
            if (_rating == value) return;
            _rating = value;
            PropertyChanged?.Invoke(this, new(nameof(Rating)));
        }
    }

    public string Role { get; set; } = "user";

    public string Content { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public IReadOnlyList<string> AttachmentNames { get; set; } = [];

    public bool HasAttachments => AttachmentNames.Count > 0;

    public string AttachmentLabel => string.Join("  ·  ", AttachmentNames.Select(name => $"📎 {name}"));

    public bool IsUser =>
        Role.Equals("user", StringComparison.OrdinalIgnoreCase);

    public string DisplayRole => IsUser ? "YOU" : "GUARDIAN";

    public bool IsAssistant => !IsUser;
}
