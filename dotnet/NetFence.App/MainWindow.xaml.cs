using System.Windows;
using System.Windows.Controls;
using NetFence.Core;
using NetFence.App.Pages;
using NetFence.App.Services;

namespace NetFence.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, System.Windows.Controls.UserControl> _pages = [];
    private ScanBlockPage? _scanBlockPage;

    private sealed record NavItem(string Key, string Label);

    public MainWindow()
    {
        InitializeComponent();

        LoadPages();
        PopulateSidebar();
        LocaleService.LanguageChanged += OnLanguageChanged;

        Loaded += async (_, _) =>
        {
            if (!ShowFirstRunWarning())
            {
                Close();
                return;
            }
            await (_scanBlockPage?.RefreshRulesAsync() ?? Task.CompletedTask);
        };
    }

    private void LoadPages()
    {
        _scanBlockPage = new ScanBlockPage();
        var settingsPage = new SettingsPage();
        settingsPage.OpenLogRequested += () => Dispatcher.Invoke(() => ScanBlockPage.OpenLog());

        _pages["ScanBlock"] = _scanBlockPage;
        _pages["NetworkMonitor"] = new NetworkMonitorPage();
        _pages["ServicesTasks"] = new ServicesTasksPage();
        _pages["RuleProfiles"] = new RuleProfilesPage();
        _pages["Settings"] = settingsPage;

        NavList.SelectedIndex = 0;
        ContentHost.Content = _scanBlockPage;
    }

    private void PopulateSidebar()
    {
        NavList.Items.Clear();
        NavList.Items.Add(new NavItem("ScanBlock", LocaleService.T("navScanBlock")));
        NavList.Items.Add(new NavItem("NetworkMonitor", LocaleService.T("navNetworkMonitor")));
        NavList.Items.Add(new NavItem("ServicesTasks", LocaleService.T("navServicesTasks")));
        NavList.Items.Add(new NavItem("RuleProfiles", LocaleService.T("navRuleProfiles")));
        NavList.Items.Add(new NavItem("Settings", LocaleService.T("navSettings")));
        AdminText.Text = LocaleService.T("adminEnabled");
    }

    private void OnLanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            for (int i = 0; i < NavList.Items.Count; i++)
            {
                if (NavList.Items[i] is NavItem item)
                {
                    var newLabel = item.Key switch
                    {
                        "ScanBlock" => LocaleService.T("navScanBlock"),
                        "NetworkMonitor" => LocaleService.T("navNetworkMonitor"),
                        "ServicesTasks" => LocaleService.T("navServicesTasks"),
                        "RuleProfiles" => LocaleService.T("navRuleProfiles"),
                        "Settings" => LocaleService.T("navSettings"),
                        _ => item.Label
                    };
                    NavList.Items[i] = new NavItem(item.Key, newLabel);
                }
            }
            AdminText.Text = LocaleService.T("adminEnabled");
        });
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is NavItem item && _pages.TryGetValue(item.Key, out var page))
            ContentHost.Content = page;
    }

    private bool ShowFirstRunWarning()
    {
        if (FirstRunState.IsAcknowledged()) return true;
        var result = System.Windows.MessageBox.Show(
            LocaleService.T("firstRunMessage"),
            LocaleService.T("firstRunTitle"),
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return false;
        FirstRunState.SetAcknowledged();
        return true;
    }
}
