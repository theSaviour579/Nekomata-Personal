namespace Nekomata.Integrations.MicrosoftGraph.Authentication;

public class MicrosoftGraphOptions
{
    public string ClientId { get; init; } = "";

    public string TenantId { get; init; } = "";

    public string RedirectUri { get; init; } =
        "http://localhost";

    public string[] Scopes { get; init; } =
    [
        "User.Read",
        "Calendars.ReadWrite",
        "Mail.ReadWrite",
        "Mail.Send"
    ];
}