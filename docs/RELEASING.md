# Releasing Nekomata for Windows

## Create a release

1. Ensure `master` is green and clean.
2. Open **Actions → Windows release → Run workflow** in the private GitHub repository.
3. Enter the next semantic version, for example `0.2.0`.
4. Optionally mark the build as a prerelease.
5. Wait for tests, self-contained publish, installer compilation, and checksum generation.
6. Download `Nekomata-Setup-<version>.exe` from the resulting private GitHub Release.

Pushing a `v<version>` tag runs the same workflow.

## Produced artifacts

- `Nekomata-Setup-<version>.exe` — per-user Inno Setup installer with optional desktop and startup shortcuts.
- `Nekomata-<version>-win-x64.zip` — portable self-contained build.
- `SHA256SUMS.txt` — SHA-256 integrity hashes for both packages.

The installer upgrades the existing per-user installation in place. It does not remove or replace .NET user secrets, Microsoft token caches, PostgreSQL data, `%LOCALAPPDATA%\Nekomata\Backups`, or first-run preferences.

## Private update checks

Nekomata uses an authenticated `gh` installation to read the latest release from the private repository. Run `gh auth login` once on each laptop. The app never embeds a repository token in the installer.
