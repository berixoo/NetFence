using NetFence.App.Services;

namespace NetFence.App.Pages;

public partial class ServicesTasksPage : System.Windows.Controls.UserControl
{
    public ServicesTasksPage()
    {
        InitializeComponent();
        ApplyLocale();
        LocaleService.LanguageChanged += ApplyLocale;
    }

    private void ApplyLocale()
    {
        Dispatcher.Invoke(() =>
            PlaceholderText.Text = LocaleService.T("servicesTasksComing"));
    }
}
