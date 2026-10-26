namespace Nekomata.Integrations.MicrosoftGraph.Authentication;

public class MicrosoftGraphOptions
{
    public string AuthenticationMode { get; init; } = "Delegated";
    public bool AllowInteractiveAuthentication { get; set; } = true;
    public string ClientSecret { get; init; } = "";
    public string UserId { get; init; } = "";
    public bool UsesClientCredentials => AuthenticationMode.Equals("ClientCredentials",StringComparison.OrdinalIgnoreCase);
    public string UserResource => UsesClientCredentials
        ? $"users/{Uri.EscapeDataString(UserId)}" : "me";
    public string ClientId { get; init; } = "";

    public string TenantId { get; init; } = "";

    public string RedirectUri { get; init; } =
        "http://localhost";

    public string[] Scopes { get; init; } =
    [
        "User.Read",
        "Calendars.ReadWrite",
        "Mail.ReadWrite",
        "Mail.Send",
        "Chat.Read",
        "ChatMessage.Send",
        "Tasks.Read",
        "Presence.Read"
    ];
}
