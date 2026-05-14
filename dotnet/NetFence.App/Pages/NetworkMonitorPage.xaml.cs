using NetFence.App.Services;

namespace NetFence.App.Pages;

public partial class NetworkMonitorPage : System.Windows.Controls.UserControl
{
    public NetworkMonitorPage()
    {
        InitializeComponent();
        ApplyLocale();
        LocaleService.LanguageChanged += ApplyLocale;
    }

    private void ApplyLocale()
    {
        Dispatcher.Invoke(() =>
            PlaceholderText.Text = LocaleService.T("networkMonitorComing"));
    }
}
