<p align="center">
  <img src="dotnet/NetFence.App/Assets/NetFence.ico" width="80" alt="NetFence" />
</p>

<h1 align="center">NetFence</h1>

<p align="center">
  <strong>Block any program from accessing the network — without disconnecting your PC.</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet" />
  <img src="https://img.shields.io/badge/Windows-10%2B-0078D6?logo=windows" />
  <img src="https://img.shields.io/badge/license-MIT-green" />
  <img src="https://img.shields.io/badge/WPF-UI-blueviolet" />
</p>

---

NetFence is a **Windows desktop tool** that uses the built-in Windows Defender Firewall to block specific applications from reaching the internet. Rules are **persistent** — they survive reboots and work even after you close NetFence.

## Features

| Module | What it does |
|---|---|
| **Scan & Block** | Select an `.exe` or a folder, scan for executables, and block them with one click |
| **Related Processes** | Automatically find child processes, same-directory helpers, and command-line launchers |
| **Network Monitor** | Real-time view of all TCP/UDP connections — remote IP, port, protocol, state, blocked status |
| **Services & Tasks** | Scan and manage Windows services and scheduled tasks related to blocked software |
| **Rule Profiles** | Save/load rule configurations as JSON, import/export for sharing, rollback to any snapshot |
| **Network Modes** | Block All / Allow All / LAN Only / Custom IPs — choose how strict you want the firewall |
| **System Tray** | Minimize to tray, auto-block child processes, launch on startup, balloon notifications |
| **Uninstall** | One-click cleanup — removes all rules, settings, database, and the application itself |

## Screenshots

<p align="center">
  <em>Dark theme — Scan & Block page</em>
</p>

> *Screenshots coming soon — submit yours in a PR!*

## Installation

### Option 1: Download Release

Download the latest `NetFence-win-x64.zip` from [Releases](https://github.com/berixoo/NetFence/releases), extract, and run `NetFence.exe`.

**No .NET runtime required** — the release is self-contained.

### Option 2: Build from Source

```bash
git clone https://github.com/berixoo/NetFence.git
cd NetFence
dotnet build NetFence.sln -c Release
```

Output: `dotnet\NetFence.App\bin\Release\net9.0-windows\NetFence.exe`

**Requirements:** .NET 9 SDK, Windows 10+

## Usage Guide

### 1. Block a Program

1. Launch **NetFence.exe as Administrator** (required for firewall changes)
2. On the **Scan & Block** page, click **Select EXE** and choose a `.exe` file, or **Select Folder** to block an entire directory
3. The tool auto-generates a **rule name** — you can change it
4. Click **Scan Related** to find child processes, same-directory helpers, and network-connected programs
5. Review the candidates — uncheck any you don't want blocked
6. Click **Block Network** — inbound + outbound firewall rules are created instantly

### 2. Unblock a Program

- **Unblock** — removes rules for the current target path + rule name
- **Unblock Selected** — select specific rules from the Firewall Rules list and remove just those
- **Unblock All** — remove every NetFence-managed rule (emergency recovery)

### 3. Monitor Network Activity

Navigate to **Network Monitor** to see all active TCP and UDP connections in real time:

- Process name, PID, program path
- Remote IP address and port
- Protocol (TCP/UDP) and connection state
- **Blocked status** — shows "Blocked" in red if NetFence is filtering that program

Auto-refresh at 1s / 2s / 5s / 10s intervals.

### 4. Scan Services & Scheduled Tasks

Navigate to **Services & Tasks**, enter a target path, and click **Scan**:

- **Services tab** — lists Windows services whose executables live under the target directory. Stop services or disable their auto-start.
- **Tasks tab** — lists scheduled tasks that reference the target path. Disable tasks or block their executables.

System-critical services (svchost, lsass, etc.) are **protected** and cannot be stopped.

### 5. Save and Manage Rule Profiles

Navigate to **Rule Profiles** to manage your blocking configurations:

- **Save Profile** — save current firewall rules as a named profile
- **Load & Block** — load a saved profile and re-apply its rules
- **Export JSON** — export a profile to a `.json` file for backup or sharing
- **Import JSON** — import a profile from a `.json` file

Profiles support four **network modes**:

| Mode | Behavior |
|---|---|
| Block All | Block all inbound + outbound traffic |
| Allow All | Remove all NetFence rules (restore networking) |
| LAN Only | Block internet but allow local network (127.0.0.0/8, 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16) |
| Custom IPs | Block all except specific IP addresses you define |

### 6. Rollback to a Snapshot

Every Block/Unblock operation automatically creates a **snapshot** of your firewall rules before the change. If something goes wrong:

1. Go to **Rule Profiles** → **Snapshots** section
2. Select a snapshot from before the bad change
3. Click **Rollback** → confirm → your rules are restored

### 7. System Tray & Background Guardian

- **Minimize to tray** — closing the window hides NetFence to the system tray
- **Background Guardian** — when enabled, NetFence watches for new process creation. If a blocked program spawns a child process, it's automatically blocked. A balloon notification tells you what was blocked.
- **Start with Windows** — enable in Settings or from the tray menu

### 8. Uninstall

**From Settings page** or **tray menu → Uninstall**:

1. Confirmation dialog appears
2. All NetFence firewall rules are removed
3. All data (`%LOCALAPPDATA%\NetFence\`) is deleted
4. The application directory is scheduled for removal on next reboot

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+1` | Scan & Block page |
| `Ctrl+2` | Network Monitor |
| `Ctrl+3` | Services & Tasks |
| `Ctrl+4` | Rule Profiles |
| `Ctrl+5` | Settings |

## Data Storage

All data is stored locally on your machine:

| What | Where |
|---|---|
| Firewall rules | Windows Defender Firewall |
| Rule profiles & snapshots | `%LOCALAPPDATA%\NetFence\NetFence.db` (SQLite) |
| User settings | `%LOCALAPPDATA%\NetFence\settings.json` |
| Operation log | `%LOCALAPPDATA%\NetFence\NetFence.log` |

## Tech Stack

- **.NET 9** — WPF desktop application
- **SQLite** — local database for profiles and history
- **Windows Firewall** — `New-NetFirewallRule` via PowerShell
- **P/Invoke** — `GetExtendedTcpTable` / `GetExtendedUdpTable` for real-time network monitoring
- **WMI** — `Win32_ProcessStartTrace` for process creation watching

## Building

```bash
# Debug build
dotnet build NetFence.sln -c Debug

# Release build
dotnet build NetFence.sln -c Release

# Run tests
dotnet run --project dotnet\NetFence.Core.Tests\NetFence.Core.Tests.csproj -c Release

# Publish self-contained
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

## Important Notes

- **Rules are persistent.** Closing NetFence does not unblock programs. Use Unblock to restore networking.
- NetFence will **never block** executables under `C:\Windows\` (system directory protection)
- Related process scanning is a **best-effort heuristic** — review candidates before blocking
- **Administrator privileges** are required for blocking, unblocking, and monitoring

## License

MIT
