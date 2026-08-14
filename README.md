# Nekomata

Nekomata is a Windows desktop workspace assistant built with WPF and .NET 10. It combines task and project planning, mission prioritisation, capacity analysis, Guardian recommendations, and optional Microsoft Graph, Halo, KnowBe4, OpenAI, and Spotify integrations.

## Prerequisites

- Windows 10 or later
- .NET 10 SDK
- PostgreSQL reachable from the desktop

## First-time setup

1. Create a PostgreSQL database named `nekomata` (or change the database settings).
2. From `Nekomata`, run `./configure-secrets.ps1` and enter local credentials. Secrets are saved through .NET user-secrets and are not written to the repository.
3. Build with `dotnet build Nekomata.sln --configuration Release`.
4. Run `Nekomata/bin/Release/net10.0-windows/Nekomata.UI.exe`.

The application records and applies database migrations automatically. If PostgreSQL is unavailable, startup shows a warning and opens in a degraded state rather than terminating.

## Verification

Run `dotnet test Nekomata.sln --configuration Release`. Tests include planning regressions, calendar and email behavior, mission continuation, Guardian action handling, configuration validation, and database migration coverage.

## Windows releases

The private `Windows release` GitHub Actions workflow publishes a self-contained x64 application, a conventional per-user installer, a portable ZIP, and SHA-256 checksums. Run it manually with a semantic version such as `0.2.0`, or push a matching tag such as `v0.2.0`.

Installed releases do not require a separate .NET runtime. User secrets, Microsoft authentication tokens, PostgreSQL data, and encrypted backups live outside the installation directory and survive upgrades. Update checks use the authenticated GitHub CLI because the repository and its releases are private.

## Configuration

Non-secret defaults live in `Nekomata/appsettings.json`. Keep database passwords, API keys, client secrets, and integration passwords in .NET user-secrets. Integrations without valid credentials fall back to their unconfigured or fake implementation where supported.

## Project structure

- `Nekomata` — WPF application and view models
- `Nekomata.Core` — planning, mission, Guardian, and workspace logic
- `Nekomata.Data` — PostgreSQL access, repositories, and schema migrations
- `Nekomata.Integrations` — Microsoft Graph and integration abstractions
- `Nekomata.Services` — Halo and KnowBe4 clients
- `Nekomata.AI` — AI provider and structured response support
- `Nekomata.Models` — shared domain models
- `Nekomata.Tests` — automated regression tests
