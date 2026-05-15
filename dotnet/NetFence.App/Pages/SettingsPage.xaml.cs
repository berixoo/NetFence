using System.Windows;
using System.Windows.Controls;
using NetFence.App.Services;

namespace NetFence.App.Pages;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    private bool _isInitializing = true;

    public event Action? OpenLogRequested;

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
            {
                item.IsSelected = true;
                break;
            }
        }

        var theme = SettingsService.Theme;
        foreach (ComboBoxItem item in ThemeBox.Items)
        {
            if (item.Tag is string tag && tag == theme)
            {
                item.IsSelected = true;
                break;
            }
        }
    }

    private void ApplyLocale()
    {
        Dispatcher.Invoke(() =>
        {
            LanguageLabel.Text = LocaleService.T("languageLabel");
            LangEnItem.Content = LocaleService.T("languageEnglish");
            LangZhItem.Content = LocaleService.T("languageChinese");
            ThemeLabel.Text = LocaleService.T("themeLabel");
            ThemeSystemItem.Content = LocaleService.T("themeSystem");
            ThemeDarkItem.Content = LocaleService.T("themeDark");
            ThemeLightItem.Content = LocaleService.T("themeLight");
            OpenLogButton.Content = LocaleService.T("openLogLabel");
            AboutLabel.Text = LocaleService.T("aboutLabel");
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

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLogRequested?.Invoke();
    }
}
