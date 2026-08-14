# Nekomata backup and laptop recovery

Nekomata source code is protected by the private GitHub repository. Workspace data is stored separately in PostgreSQL and must be backed up.

## Configure automatic backups

Install PostgreSQL client tools (`pg_dump` and `pg_restore`). Nekomata discovers them from `PATH`, the standard PostgreSQL installation folder, or `Backup:PostgreSqlBinPath`.

Set a strong encryption password outside Git. Either use the environment variable:

```powershell
[Environment]::SetEnvironmentVariable('NEKOMATA_BACKUP_PASSPHRASE', 'your-long-private-password', 'User')
```

Or use .NET user secrets from the UI project directory:

```powershell
dotnet user-secrets set "Backup:Passphrase" "your-long-private-password"
dotnet user-secrets set "Database:Password" "your-database-password"
```

Restart Nekomata after changing either setting. It creates at most one automatic backup every 24 hours under `%LOCALAPPDATA%\Nekomata\Backups`, retaining seven recent daily files plus four older weekly files.

Copy encrypted `.nkb` files to a separately protected cloud folder or external drive. The encryption password is never stored in the backup.

## Move to another laptop

1. Install Git, .NET 10, PostgreSQL server/client tools, and clone the private repository.
2. Create the `nekomata` PostgreSQL database and configure its password in user secrets.
3. Launch Nekomata once so the schema is initialised.
4. Open **Status → Backup & Recovery → Restore**.
5. Select the `.nkb` file and enter its encryption password.
6. Restart Nekomata after the restore completes.
7. Run Diagnostics and confirm PostgreSQL and Backups are healthy.

Restore deliberately replaces the current `assistant` schema. Nekomata verifies the encrypted archive, creates an encrypted pre-restore safety backup, and requires explicit confirmation before making changes.

The old placeholder database password previously present in `appsettings.json` has been removed. If that password was ever used outside an isolated local development database, rotate it before relying on these backups.
