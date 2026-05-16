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
    private Forms.ToolStripMenuItem? _watcherMenuItem;
    private Forms.ToolStripMenuItem? _autoStartMenuItem;
    private bool _isExiting;
    private bool _ownsTrayIcon;
    private static string? _lastDispatcherError;

    private const string AutoStartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValue = "NetFence";

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _lastDispatcherError = args.Exception.ToString();
            try
            {
                OperationLog.Write(OperationLog.DefaultPath, "UnhandledUiException",
                    args.Exception.ToString(), []);
            }
            catch { }
            // Do NOT mark as handled — let the exception propagate so it can be caught by try/catch
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                try
                {
                    OperationLog.Write(OperationLog.DefaultPath, "UnhandledException", ex.ToString(), []);
                }
                catch { }
            }
        };

        try { ThemeService.Apply(SettingsService.Theme); }
        catch (Exception ex)
        {
            OperationLog.Write(OperationLog.DefaultPath, "StartupError_Theme", ex.ToString(), []);
        }

        try { CreateTrayIcon(); }
        catch (Exception ex)
        {
            OperationLog.Write(OperationLog.DefaultPath, "StartupError_TrayIcon", ex.ToString(), []);
        }

        // Start watcher on background thread to avoid blocking UI init
        Task.Run(() =>
        {
            try { StartWatcher(); }
            catch (Exception ex)
            {
                OperationLog.Write(OperationLog.DefaultPath, "StartupError_Watcher", ex.ToString(), []);
            }
        });

        try
        {
            _mainWindow = new MainWindow();
            MainWindow = _mainWindow;
            _mainWindow.Closing += (_, args) =>
            {
                if (!_isExiting)
                {
                    args.Cancel = true;
                    _mainWindow.Hide();
                }
            };
            _mainWindow.Show();
        }
        catch (Exception ex)
        {
            OperationLog.Write(OperationLog.DefaultPath, "StartupError_MainWindow",
                ex.ToString(), []);
            try { System.Windows.MessageBox.Show(ex.ToString(), "NetFence Startup Error",
                MessageBoxButton.OK, MessageBoxImage.Error); }
            catch { }
            Shutdown();
            return;
        }
    }

    private void CreateTrayIcon()
    {
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NetFence.exe");
        System.Drawing.Icon icon;
        try { icon = System.Drawing.Icon.ExtractAssociatedIcon(iconPath); _ownsTrayIcon = true; }
        catch { icon = System.Drawing.SystemIcons.Application; _ownsTrayIcon = false; }

        _watcherMenuItem = new Forms.ToolStripMenuItem(LocaleService.T("trayEnableWatcher"))
            { Checked = true };
        _watcherMenuItem.Click += (_, _) => SyncWatcherToggle(!_watcherMenuItem.Checked);

        _autoStartMenuItem = new Forms.ToolStripMenuItem(LocaleService.T("trayAutoStart"))
            { Checked = IsAutoStartEnabled() };
        _autoStartMenuItem.Click += (_, _) => SyncAutoStartToggle(!_autoStartMenuItem.Checked);

        var strip = new Forms.ContextMenuStrip();
        strip.Items.AddRange(new Forms.ToolStripItem[]
        {
            new Forms.ToolStripMenuItem(LocaleService.T("trayShow"), null, (_, _) => ShowMainWindow()),
            new Forms.ToolStripSeparator(),
            _watcherMenuItem,
            _autoStartMenuItem,
            new Forms.ToolStripSeparator(),
            new Forms.ToolStripMenuItem(LocaleService.T("trayUninstall"), null, (_, _) => Uninstall()),
            new Forms.ToolStripSeparator(),
            new Forms.ToolStripMenuItem(LocaleService.T("trayExit"), null, (_, _) => ExitApplication())
        });

        if (icon is null) icon = System.Drawing.SystemIcons.Application;

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "NetFence",
            ContextMenuStrip = strip
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void StartWatcher()
    {
        ProcessWatcher.ProcessStarted += OnProcessStarted;
        ProcessWatcher.Start();
    }

    private static DateTime _lastWatcherEvent = DateTime.MinValue;
    private static readonly TimeSpan WatcherThrottle = TimeSpan.FromSeconds(1);

    private void OnProcessStarted(object? sender, WatcherEventArgs e)
    {
        // Rate limit to prevent thread pool saturation
        var now = DateTime.UtcNow;
        if (now - _lastWatcherEvent < WatcherThrottle) return;
        _lastWatcherEvent = now;

        Task.Run(() =>
        {
            try
            {
                var blockedSet = NetworkMonitor.GetNetFenceBlockedPrograms();
                if (blockedSet.Count == 0) return;

                string? parentExe;
                try
                {
                    using var proc = Process.GetProcessById(e.ParentProcessId);
                    parentExe = proc.MainModule?.FileName;
                }
                catch { return; }

                if (parentExe is null || !blockedSet.Contains(parentExe)) return;

                string? childExe;
                try
                {
                    using var proc = Process.GetProcessById(e.ProcessId);
                    childExe = proc.MainModule?.FileName;
                }
                catch { return; }

                if (childExe is null || blockedSet.Contains(childExe)) return;

                FirewallService.Block(childExe, "auto", false, Array.Empty<string>());
                Dispatcher.Invoke(() =>
                    _trayIcon?.ShowBalloonTip(3000, "NetFence",
                        LocaleService.T("trayAutoBlocked", Path.GetFileName(childExe)),
                        Forms.ToolTipIcon.Info));
            }
            catch { }
        });
    }

    public void SyncWatcherToggle(bool enabled)
    {
        if (_watcherMenuItem is not null) _watcherMenuItem.Checked = enabled;
        try
        {
            if (enabled) StartWatcher();
            else ProcessWatcher.Stop();
        }
        catch { if (_watcherMenuItem is not null) _watcherMenuItem.Checked = !enabled; }
    }

    public void SyncAutoStartToggle(bool enabled)
    {
        if (_autoStartMenuItem is not null) _autoStartMenuItem.Checked = enabled;
        try { SetAutoStart(enabled); }
        catch { if (_autoStartMenuItem is not null) _autoStartMenuItem.Checked = !enabled; }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Show();
        _mainWindow.Activate();
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
            {
                var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NetFence.exe");
                key?.SetValue(AutoStartValue, $"\"{exePath}\"");
            }
            else
                key?.DeleteValue(AutoStartValue, false);
        }
        catch { }
    }

    public void Uninstall()
    {
        var result = System.Windows.MessageBox.Show(
            LocaleService.T("uninstallConfirm"), "NetFence",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        SetAutoStart(false);

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetFence");

        // Schedule deletion on next reboot (handles locked files)
        try { MoveFileEx(dataDir, null, 0x4); } catch { }
        try { MoveFileEx(appDir, null, 0x4); } catch { }

        var batchPath = Path.Combine(Path.GetTempPath(), "netfence-uninstall.bat");
        var batchContent = string.Join(Environment.NewLine,
            "@echo off",
            "timeout /t 2 /nobreak >nul",
            $"reg delete \"HKCU\\{AutoStartKey}\" /v \"{AutoStartValue}\" /f 2>nul",
            $"if exist \"{dataDir}\" rmdir /s /q \"{dataDir}\"",
            $"if exist \"{appDir}\" rmdir /s /q \"{appDir}\"",
            "del \"%~f0\"");
        File.WriteAllText(batchPath, batchContent,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batchPath}\"")
        {
            UseShellExecute = true,
            CreateNoWindow = false
        });

        ExitApplication();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, int dwFlags);

    private void ExitApplication()
    {
        _isExiting = true;
        ProcessWatcher.Stop();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            if (_ownsTrayIcon) _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
        }
        Shutdown();
    }
}
