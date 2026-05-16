using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NetFence.Core;
using NetFence.App.Services;

namespace NetFence.App.Pages;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    private bool _isInitializing = true;

    public event Action? OpenLogRequested;
    public event Action? UninstallRequested;
    public event Action<bool>? WatcherToggled;
    public event Action<bool>? AutoStartToggled;

    public SettingsPage()
    {
        InitializeComponent();
        LoadCurrentSettings();
        ApplyLocale();
        LocaleService.LanguageChanged += ApplyLocale;
        _isInitializing = false;
    }

    private void LoadCurrentSettings()
    {
        var lang = LocaleService.CurrentLanguage;
        foreach (ComboBoxItem item in LanguageBox.Items)
        {
            if (item.Tag is string tag && tag == lang)
            { item.IsSelected = true; break; }
        }

        var theme = SettingsService.Theme;
        foreach (ComboBoxItem item in ThemeBox.Items)
        {
            if (item.Tag is string tag && tag == theme)
            { item.IsSelected = true; break; }
        }

        GuardianToggle.IsChecked = ProcessWatcher.IsRunning;
        AutoStartToggle.IsChecked = IsAutoStartEnabled();
    }

    private void ApplyLocale()
    {
        Dispatcher.Invoke(() =>
        {
            AppearanceSectionLabel.Text = LocaleService.T("sectionAppearance");
            LanguageLabel.Text = LocaleService.T("languageLabel");
            LanguageDesc.Text = LocaleService.T("languageDesc");
            LangEnItem.Content = LocaleService.T("languageEnglish");
            LangZhItem.Content = LocaleService.T("languageChinese");
            ThemeLabel.Text = LocaleService.T("themeLabel");
            ThemeDesc.Text = LocaleService.T("themeDesc");
            ThemeSystemItem.Content = LocaleService.T("themeSystem");
            ThemeDarkItem.Content = LocaleService.T("themeDark");
            ThemeLightItem.Content = LocaleService.T("themeLight");

            GuardianSectionLabel.Text = LocaleService.T("sectionGuardian");
            GuardianLabel.Text = LocaleService.T("trayEnableWatcher");
            GuardianDesc.Text = LocaleService.T("guardianDesc");
            AutoStartLabel.Text = LocaleService.T("trayAutoStart");
            AutoStartDesc.Text = LocaleService.T("autoStartDesc");

            SystemSectionLabel.Text = LocaleService.T("sectionSystem");
            OpenLogButton.Content = LocaleService.T("openLogLabel");
            UninstallButton.Content = LocaleService.T("trayUninstall");

            AboutSectionLabel.Text = LocaleService.T("aboutLabel");
            AboutVersion.Text = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";
            AboutText.Text = LocaleService.T("aboutText");
        });
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (LanguageBox.SelectedItem is ComboBoxItem item && item.Tag is string code)
            LocaleService.CurrentLanguage = code;
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ThemeBox.SelectedItem is ComboBoxItem item && item.Tag is string theme)
            ThemeService.Apply(theme);
    }

    private void GuardianToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        var enabled = GuardianToggle.IsChecked == true;
        try
        {
            if (enabled) { ProcessWatcher.Start(); }
            else ProcessWatcher.Stop();
            WatcherToggled?.Invoke(enabled);
        }
        catch { GuardianToggle.IsChecked = !enabled; }
    }

    private void AutoStartToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        var enabled = AutoStartToggle.IsChecked == true;
        try
        {
            SetAutoStart(enabled);
            AutoStartToggled?.Invoke(enabled);
        }
        catch { AutoStartToggle.IsChecked = !enabled; }
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLogRequested?.Invoke();
    }

    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        UninstallRequested?.Invoke();
    }

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("NetFence") is not null;
        }
        catch { return false; }
    }

    private static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (enable)
                key?.SetValue("NetFence",
                    $"\"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NetFence.exe")}\"");
            else
                key?.DeleteValue("NetFence", false);
        }
        catch { }
    }
}
