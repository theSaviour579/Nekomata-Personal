# Nekomata Personal

Nekomata Personal is a Windows planning assistant for everyday work and life. It combines tasks, projects, daily planning, mission focus, capacity guidance, assistant conversations, and optional Microsoft calendar, email, OpenAI and music connections.

## Support

If Nekomata Personal is useful to you, you can support its continued development at [Buy Me a Coffee](https://buymeacoffee.com/nekomataassistant).

## Designed for you

- First-run setup asks what the assistant should call you.
- Tasks, projects, mission history, and assistant memory stay in `%LocalAppData%\Nekomata Personal`.
- Your workspace is ready after installation, with no separate data service to configure.
- Guardian can use an organisation's Azure OpenAI/Foundry deployment through each user's Microsoft Entra identity, with an optional OpenAI key stored in Windows Credential Manager as fallback.
- Licensed work/school users can select the preview Microsoft 365 Copilot Chat API for Guardian conversation. Its additional delegated permissions are requested separately, web grounding is opt-in, and it never silently falls back to a chargeable provider.
- Spotify uses OAuth PKCE. Configure the organisation's public Spotify app Client ID and register `http://127.0.0.1:43821/callback/` as its redirect URI.
- Personal settings let each user choose Spotify, a YouTube Music page, or a favourite radio station link. Browser-based sources open only when the user chooses to open them.
- Portable encrypted backups let you move your workspace to another Windows computer.
- The shared navigation rail provides quick access to planning, mail, meetings, team skills, work logging, review, learning, audit, What If and Value without organisation-specific tools.
- Team can sync Microsoft 365 direct reports and job titles, while keeping availability, responsibilities, exclusions and role skills editable in the local Personal workspace.
- What If compares the ranked plan, a protected objective and an earlier finish while keeping calendar commitments visible for review.
- Value reads a connected Microsoft 365 job title when available, or a manual title from Personal settings, then creates goals for that role and maps completed work against them. A local role template keeps it useful when AI is unavailable.

## Microsoft account connection

The application supports delegated Microsoft Graph access for calendar, email, Microsoft To Do and assigned Planner tasks. A central multi-tenant Entra application ID must be supplied in `MicrosoftGraph:ClientId` before distribution. Individual users then connect with the in-app Microsoft sign-in and do not register their own application.

The intended delegated scopes are `User.Read`, `User.ReadBasic.All`, `Calendars.ReadWrite`, `Mail.ReadWrite`, `Mail.Send`, and read-only `Tasks.Read`. Users see and consent to those permissions during sign-in. `User.ReadBasic.All` supplies the basic profiles and job titles of the signed-in user's Microsoft 365 direct reports for the editable Team capability. Planner is available only to supported work or school accounts; its basic assigned tasks are imported read-only. Duplicate items exposed by both Planner and To Do are collapsed in favour of the Planner record.

Microsoft 365 Copilot conversation is an optional preview connection for licensed work/school accounts. Add the following delegated Microsoft Graph permissions to the Entra app registration and grant administrator consent before using **Test Microsoft 365 Copilot access** in Personal settings: `Sites.Read.All`, `Mail.Read`, `People.Read.All`, `OnlineMeetingTranscript.Read.All`, `Chat.Read`, `ChannelMessage.Read.All`, and `ExternalItem.Read.All`. These permissions are requested only when the user tests or uses Copilot; ordinary calendar and email sign-in continues to use its existing scope set.

## Development

Requirements are Windows 10 or later and the .NET 10 SDK.

```powershell
dotnet restore Nekomata/Nekomata.sln
dotnet build Nekomata/Nekomata.sln --configuration Release
dotnet test Nekomata/Nekomata.Tests/Nekomata.Tests.csproj --configuration Release
```

## Releases

Merging a versioned pull request into `master` runs the `Windows release` workflow. It creates a self-contained x64 portable archive, `Nekomata-Personal-Setup-<version>.exe`, and SHA-256 checksums. Packages are published to the public [Nekomata Personal Releases](https://github.com/theSaviour579/Nekomata-Personal-Releases) repository while the source remains private. Nekomata checks that public feed at startup, downloads the matching installer with confirmation, verifies its published SHA-256 digest, and hands off to the Windows installer without requiring a GitHub account.

## Quick menu and task ownership

Press Ctrl+K while the main window is active, or click Quick Menu, to search navigation and actions. Use the arrow keys and Enter to select, or Escape to close.

Planner imports use Microsoft Graph's assigned-to-me endpoint. To Do imports include only owned, unshared lists and exclude the generated Flagged email list. Shared To Do lists are excluded, including their tasks assigned to you, because the Microsoft Graph v1.0 To Do API does not expose task assignees. The sync status reports excluded lists; personal list tasks do not require an explicit assignment.
