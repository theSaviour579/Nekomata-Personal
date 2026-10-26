# Nekomata Personal backup and laptop recovery

Nekomata Personal keeps everything on the machine it runs on. There is no database server
and nothing to configure: your workspace, tasks, projects, objectives, duties, Guardian's
own records and its memory all live under `%LOCALAPPDATA%\Nekomata Personal`.

Because that folder is the only copy, take a backup before moving to another computer.

## Create a backup

1. Open **Settings → Backup & Recovery → Create backup**.
2. Choose where to save the `.nkp` file.
3. Enter an encryption password when prompted.

The archive is encrypted with that password, which is never stored inside it. Keep the file
somewhere separately protected — a cloud folder or an external drive — and remember the
password, because the backup cannot be opened without it.

## Move to another computer

1. Install Nekomata Personal on the new computer and launch it once, so it creates its
   workspace folder.
2. Open **Settings → Backup & Recovery → Restore**.
3. Select the `.nkp` file and enter its encryption password.
4. Restart Nekomata Personal after the restore completes.
5. Sign in to Microsoft again, and re-enter any OpenAI key: tokens and secrets are held in
   Windows Credential Manager on the old machine and are deliberately not carried in the backup.

Restoring replaces the current workspace. Nekomata Personal verifies the archive first and
asks for explicit confirmation before it changes anything.

## If the workspace will not load

Nekomata Personal keeps each of Guardian's records in its own small JSON file. If one is
damaged it is set aside as `<name>.json.unreadable-<timestamp>` and that feature starts
again from empty, rather than the whole app failing to start. Those files can be deleted
once you no longer need them, or inspected in a text editor to recover anything important.
