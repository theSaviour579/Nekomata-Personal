namespace Nekomata.Core.Personalization;

/// <summary>Who Guardian is working for. The desktop supplies this from the personal profile.</summary>
public interface IUserIdentity
{
    string DisplayName { get; }

    /// <summary>The user's first name, or "" before setup is finished.</summary>
    string FirstName { get; }

    /// <summary>"Name, Job title" when a job title is known, otherwise just the name; for AI prompts.</summary>
    string Description { get; }

    /// <summary>True when the given name or address is the user themselves.</summary>
    bool Is(string? nameOrAddress);
}
