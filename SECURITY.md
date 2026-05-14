# Security Policy

NetFence modifies Windows Defender Firewall rules and requires administrator
permission for block and unblock operations.

## What NetFence Does

- Creates and removes local Windows firewall rules.
- Stores a local operation log under `%LOCALAPPDATA%\NetFence\NetFence.log`.
- Stores a local first-run acknowledgement marker under `%LOCALAPPDATA%\NetFence`.

## What NetFence Does Not Do

- It does not upload telemetry.
- It does not contact a remote server.
- It does not install a background service.
- It does not change network adapter state.

## Reporting Issues

Please open a GitHub issue with:

- NetFence version.
- Windows version.
- Steps to reproduce.
- Exported snapshot if relevant, after removing sensitive paths if needed.
