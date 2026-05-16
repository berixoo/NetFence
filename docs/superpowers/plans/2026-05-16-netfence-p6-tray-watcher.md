# NetFence P6 — System Tray + Process Watcher + Uninstall

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development

**Goal:** Add system tray with "minimize to tray", WMI process creation watcher with auto-block, startup-with-Windows, and full uninstall.

**Architecture:** NotifyIcon in App.xaml.cs, ProcessWatcher using ManagementEventWatcher, MainWindow hides to tray on close.

**Tech Stack:** .NET 9, WPF + System.Windows.Forms, System.Management (WMI)

---

### Task 1: ProcessWatcher.cs + NuGet package

**Files:**
- Create: `dotnet/NetFence.Core/ProcessWatcher.cs`
- Modify: `dotnet/NetFence.Core/NetFence.Core.csproj` (add System.Management)

- [ ] **Step 1: Add NuGet**

```bash
cd D:\Desktop\workspace\NetFence\dotnet\NetFence.Core && dotnet add package System.Management
```

- [ ] **Step 2: Create ProcessWatcher.cs**

```csharp
using System.Management;

namespace NetFence.Core;

public sealed class WatcherEventArgs : EventArgs
{
    public int ProcessId { get; init; }
    public int ParentProcessId { get; init; }
}

public static class ProcessWatcher
{
    private static ManagementEventWatcher? _watcher;
    public static bool IsRunning => _watcher is not null;

    public static event EventHandler<WatcherEventArgs>? ProcessStarted;

    public static void Start()
    {
        if (_watcher is not null) return;

        var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
        _watcher = new ManagementEventWatcher(query);
        _watcher.EventArrived += OnEventArrived;
        _watcher.Start();
    }

    public static void Stop()
    {
        if (_watcher is null) return;
        _watcher.Stop();
        _watcher.EventArrived -= OnEventArrived;
        _watcher.Dispose();
        _watcher = null;
    }

    private static void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
            var parentPid = Convert.ToInt32(e.NewEvent.Properties["ParentProcessID"].Value);
            ProcessStarted?.Invoke(null, new WatcherEventArgs
            {
                ProcessId = pid,
                ParentProcessId = parentPid
            });
        }
        catch { }
    }
}
```

- [ ] **Step 3: Build + Commit**

```bash
dotnet build dotnet/NetFence.Core/NetFence.Core.csproj -c Debug
git add dotnet/NetFence.Core/
git commit -m "feat: add ProcessWatcher with WMI Win32_ProcessStartTrace"
```

---

### Task 2: App.xaml.cs — tray icon + ProcessWatcher + uninstall + startup

**Files:**
- Modify: `dotnet/NetFence.App/App.xaml.cs`

Full rewrite of `App.xaml.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using NetFence.Core;
using NetFence.App.Services;
using Forms = System.Windows.Forms;

namespace NetFence.App;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private Forms.MenuItem? _watcherMenuItem;
    private Forms.MenuItem? _autoStartMenuItem;
    private bool _isExiting;

    private const string AutoStartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValue = "NetFence";

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                OperationLog.Write(OperationLog.DefaultPath, "UnhandledUiException", args.Exception.Message, []);
                System.Windows.MessageBox.Show(args.Exception.Message, "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                try { OperationLog.Write(OperationLog.DefaultPath, "UnhandledException", ex.Message, []); }
                catch { }
            }
        };

        ThemeService.Apply(SettingsService.Theme);
        CreateTrayIcon();
        StartWatcher();
        base.OnStartup(e);

        _mainWindow = (MainWindow)MainWindow;
        _mainWindow.Closing += (_, args) =>
        {
            if (!_isExiting)
            {
                args.Cancel = true;
                _mainWindow.Hide();
            }
        };
    }

    private void CreateTrayIcon()
    {
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NetFence.exe");
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(iconPath);

        _watcherMenuItem = new Forms.MenuItem(LocaleService.T("trayEnableWatcher"))
            { Checked = true };
        _watcherMenuItem.Click += (_, _) =>
        {
            _watcherMenuItem.Checked = !_watcherMenuItem.Checked;
            if (_watcherMenuItem.Checked) StartWatcher();
            else ProcessWatcher.Stop();
        };

        _autoStartMenuItem = new Forms.MenuItem(LocaleService.T("trayAutoStart"))
            { Checked = IsAutoStartEnabled() };
        _autoStartMenuItem.Click += (_, _) =>
        {
            _autoStartMenuItem.Checked = !_autoStartMenuItem.Checked;
            SetAutoStart(_autoStartMenuItem.Checked);
        };

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon!,
            Visible = true,
            Text = "NetFence",
            ContextMenu = new Forms.ContextMenu(new[]
            {
                new Forms.MenuItem(LocaleService.T("trayShow"), (_, _) => ShowMainWindow()),
                new Forms.MenuItem("-"),
                _watcherMenuItem,
                _autoStartMenuItem,
                new Forms.MenuItem("-"),
                new Forms.MenuItem(LocaleService.T("trayUninstall"), (_, _) => Uninstall()),
                new Forms.MenuItem("-"),
                new Forms.MenuItem(LocaleService.T("trayExit"), (_, _) => ExitApplication())
            })
        };
    }

    private void StartWatcher()
    {
        ProcessWatcher.ProcessStarted += OnProcessStarted;
        ProcessWatcher.Start();
    }

    private void OnProcessStarted(object? sender, WatcherEventArgs e)
    {
        try
        {
            // Get blocked programs
            var blockedPrograms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in FirewallService.GetStatus())
            {
                if (!string.IsNullOrWhiteSpace(rule.Program) && Path.IsPathFullyQualified(rule.Program))
                    blockedPrograms.Add(Path.GetFullPath(rule.Program));
            }

            // Get parent process exe path
            string? parentExe;
            try
            {
                using var proc = Process.GetProcessById(e.ParentProcessId);
                parentExe = proc.MainModule?.FileName;
            }
            catch { return; }

            if (parentExe is null || !blockedPrograms.Contains(parentExe)) return;

            // Get child process exe path
            string? childExe;
            try
            {
                using var proc = Process.GetProcessById(e.ProcessId);
                childExe = proc.MainModule?.FileName;
            }
            catch { return; }

            if (childExe is null || blockedPrograms.Contains(childExe)) return;

            // Auto-block the child
            FirewallService.Block(childExe, "auto", false, Array.Empty<string>());
            Dispatcher.Invoke(() =>
                _trayIcon?.ShowBalloonTip(3000, "NetFence",
                    LocaleService.T("trayAutoBlocked", Path.GetFileName(childExe)),
                    Forms.ToolTipIcon.Info));
        }
        catch { }
    }

    private void ShowMainWindow()
    {
        _mainWindow?.Show();
        _mainWindow?.Activate();
    }

    private bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKey);
            return key?.GetValue(AutoStartValue) is not null;
        }
        catch { return false; }
    }

    private void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKey, true);
            if (enable)
                key?.SetValue(AutoStartValue,
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NetFence.exe"));
            else
                key?.DeleteValue(AutoStartValue, false);
        }
        catch { }
    }

    private void Uninstall()
    {
        var result = System.Windows.MessageBox.Show(
            LocaleService.T("uninstallConfirm"), "NetFence",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        // Disable auto-start
        SetAutoStart(false);

        // Generate uninstall batch script
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetFence");
        var batchPath = Path.Combine(Path.GetTempPath(), "netfence-uninstall.bat");

        File.WriteAllText(batchPath, $"""
            @echo off
            timeout /t 2 /nobreak >nul
            reg delete "HKCU\{AutoStartKey}" /v "{AutoStartValue}" /f 2>nul
            if exist "{dataDir}" rmdir /s /q "{dataDir}"
            if exist "{appDir}" rmdir /s /q "{appDir}"
            del "%~f0"
            """,
            System.Text.Encoding.ASCII);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batchPath}\"")
        {
            UseShellExecute = true,
            CreateNoWindow = false
        });

        ExitApplication();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        ProcessWatcher.Stop();
        _trayIcon?.Dispose();
        Shutdown();
    }
}
```

- [ ] **Step 2: Also update MainWindow.xaml.cs closing behavior**

Read `MainWindow.xaml.cs`. The `Closing` event moved to App.xaml.cs. Remove any existing OnClosing override if present (the App handles it via `_mainWindow.Closing`).

- [ ] **Step 3: Minor — LocaleService keys**

Read LocaleService.cs, add to en-US:
```csharp
            ["trayShow"] = "Show NetFence",
            ["trayEnableWatcher"] = "Enable Guardian",
            ["trayAutoStart"] = "Start with Windows",
            ["trayUninstall"] = "Uninstall",
            ["trayExit"] = "Exit",
            ["trayAutoBlocked"] = "Auto-blocked: {0}",
            ["uninstallConfirm"] = "Uninstall NetFence? All files and settings will be deleted.",
```

Add to zh-CN:
```csharp
            ["trayShow"] = "显示 NetFence",
            ["trayEnableWatcher"] = "启用守护",
            ["trayAutoStart"] = "开机自启",
            ["trayUninstall"] = "卸载",
            ["trayExit"] = "退出",
            ["trayAutoBlocked"] = "已自动阻断：{0}",
            ["uninstallConfirm"] = "确定卸载 NetFence？将删除所有文件和配置。",
```

- [ ] **Step 4: Build + Commit**

```bash
dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug
git add dotnet/NetFence.App/
git commit -m "feat: add system tray, process watcher, auto-start, and uninstall"
```

---

### Task 3: Integration test

- [ ] **Step 1: Build Release + run core tests**

```bash
dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Release
dotnet run --project dotnet/NetFence.Core.Tests/NetFence.Core.Tests.csproj -c Release
```

- [ ] **Step 2: Manual verification**
1. Launch → tray icon appears, window opens
2. Close window → window hides (minimized to tray)
3. Right-click tray → "Show NetFence" → window reappears
4. "Enable Guardian" toggle → checkmark toggles
5. "Start with Windows" toggle → registry key appears/remains
6. Block a program → start it → child process → balloon tip "Auto-blocked"
7. "Uninstall" → confirm → bat runs → app exits → files deleted
8. "Exit" → app closes completely
