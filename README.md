# Nekomata Personal

Nekomata Personal is a Windows planning assistant for everyday work and life. It combines tasks, projects, daily planning, mission focus, capacity guidance, assistant conversations, and optional Microsoft calendar, email, OpenAI, and Spotify connections.

## Support

If Nekomata Personal is useful to you, you can support its continued development at [Buy Me a Coffee](https://buymeacoffee.com/nekomataassistant).

## Designed for you

- First-run setup asks what the assistant should call you.
- Tasks, projects, mission history, and assistant memory stay in `%LocalAppData%\Nekomata Personal`.
- Your workspace is ready after installation, with no separate data service to configure.
- Your OpenAI key is optional and is stored in Windows Credential Manager.
- Portable encrypted backups let you move your workspace to another Windows computer.

## Microsoft account connection

The application supports delegated Microsoft Graph access for calendar and email. A central multi-tenant Entra application ID must be supplied in `MicrosoftGraph:ClientId` before distribution. Individual users then connect with the in-app Microsoft sign-in and do not register their own application.

The intended delegated scopes are `User.Read`, `Calendars.ReadWrite`, `Mail.ReadWrite`, and `Mail.Send`. Users see and consent to those permissions during sign-in.

## Development

Requirements are Windows 10 or later and the .NET 10 SDK.

```powershell
dotnet restore Nekomata/Nekomata.sln
dotnet build Nekomata/Nekomata.sln --configuration Release
dotnet test Nekomata/Nekomata.Tests/Nekomata.Tests.csproj --configuration Release
```

## Releases

Merging a versioned pull request into `master` runs the `Windows release` workflow. It creates a self-contained x64 portable archive, `Nekomata-Personal-Setup-<version>.exe`, and SHA-256 checksums. Packages are published to the public [Nekomata Personal Releases](https://github.com/theSaviour579/Nekomata-Personal-Releases) repository while the source remains private. Nekomata checks that public feed at startup, downloads the matching installer with confirmation, verifies its published SHA-256 digest, and hands off to the Windows installer without requiring a GitHub account.
