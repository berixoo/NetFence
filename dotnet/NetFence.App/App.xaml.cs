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

    private const string AutoStartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValue = "NetFence";

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                OperationLog.Write(OperationLog.DefaultPath, "UnhandledUiException",
                    args.Exception.Message, []);
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
                try
                {
                    OperationLog.Write(OperationLog.DefaultPath, "UnhandledException", ex.Message, []);
                }
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

        _watcherMenuItem = new Forms.ToolStripMenuItem(LocaleService.T("trayEnableWatcher"))
            { Checked = true };
        _watcherMenuItem.Click += (_, _) =>
        {
            _watcherMenuItem.Checked = !_watcherMenuItem.Checked;
            if (_watcherMenuItem.Checked) StartWatcher();
            else ProcessWatcher.Stop();
        };

        _autoStartMenuItem = new Forms.ToolStripMenuItem(LocaleService.T("trayAutoStart"))
            { Checked = IsAutoStartEnabled() };
        _autoStartMenuItem.Click += (_, _) =>
        {
            _autoStartMenuItem.Checked = !_autoStartMenuItem.Checked;
            SetAutoStart(_autoStartMenuItem.Checked);
        };

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

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon!,
            Visible = true,
            Text = "NetFence",
            ContextMenuStrip = strip
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
            var blockedPrograms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in FirewallService.GetStatus())
            {
                if (!string.IsNullOrWhiteSpace(rule.Program) && Path.IsPathFullyQualified(rule.Program))
                    blockedPrograms.Add(Path.GetFullPath(rule.Program));
            }

            string? parentExe;
            try
            {
                using var proc = Process.GetProcessById(e.ParentProcessId);
                parentExe = proc.MainModule?.FileName;
            }
            catch { return; }

            if (parentExe is null || !blockedPrograms.Contains(parentExe)) return;

            string? childExe;
            try
            {
                using var proc = Process.GetProcessById(e.ProcessId);
                childExe = proc.MainModule?.FileName;
            }
            catch { return; }

            if (childExe is null || blockedPrograms.Contains(childExe)) return;

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

        SetAutoStart(false);

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
