using NetFence.App.Services;

namespace NetFence.App.Pages;

public partial class RuleProfilesPage : System.Windows.Controls.UserControl
{
    public RuleProfilesPage()
    {
        InitializeComponent();
        ApplyLocale();
        LocaleService.LanguageChanged += ApplyLocale;
    }

    private void ApplyLocale()
    {
        Dispatcher.Invoke(() =>
            PlaceholderText.Text = LocaleService.T("ruleProfilesComing"));
    }
}
