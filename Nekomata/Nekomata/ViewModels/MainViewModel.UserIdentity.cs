namespace Nekomata.UI.ViewModels;

// Who Guardian is working for, taken from the personal profile, so prompts and messages never name a fixed person.
public partial class MainViewModel
{
    /// <summary>The user's name as they gave it during setup, or "the user" before setup is finished.</summary>
    private string UserName => string.IsNullOrWhiteSpace(_personalProfile.Current.DisplayName) ? "the user" : _personalProfile.Current.DisplayName.Trim();

    /// <summary>The user's first name, for spoken and written replies.</summary>
    private string UserFirstName => string.IsNullOrWhiteSpace(_personalProfile.Current.DisplayName)
        ? "" : _personalProfile.Current.DisplayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    /// <summary>"Name, Job title" when a job title is known; used to give the AI context about who it is helping.</summary>
    private string UserDescription => string.IsNullOrWhiteSpace(_personalProfile.Current.JobTitle)
        ? UserName : $"{UserName}, {_personalProfile.Current.JobTitle.Trim()}";
}
