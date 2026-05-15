using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NetFence.Core;
using NetFence.App.Services;

namespace NetFence.App.Pages;

public partial class NetworkMonitorPage : System.Windows.Controls.UserControl
{
    private readonly ObservableCollection<ConnectionRow> _connections = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _isRefreshing;

    public NetworkMonitorPage()
    {
        InitializeComponent();
        ConnectionsGrid.ItemsSource = _connections;
        _timer.Tick += async (_, _) => await RefreshAsync();
        ApplyLocale();
        LocaleService.LanguageChanged += ApplyLocale;
        IsVisibleChanged += OnVisibilityChanged;

        Loaded += async (_, _) =>
        {
            AutoRefreshBox.SelectedIndex = 2; // default 2s
            await RefreshAsync();
        };
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) _timer.Start();
        else _timer.Stop();
    }

    private void ApplyLocale()
    {
        Dispatcher.Invoke(() =>
        {
            RefreshButton.Content = LocaleService.T("refresh");
            AutoRefreshLabel.Text = LocaleService.T("autoRefresh");
            RefreshOff.Content = LocaleService.T("autoRefreshOff");
            Refresh1s.Content = LocaleService.T("refreshInterval1s");
            Refresh2s.Content = LocaleService.T("refreshInterval2s");
            Refresh5s.Content = LocaleService.T("refreshInterval5s");
            Refresh10s.Content = LocaleService.T("refreshInterval10s");
            ColProcess.Header = LocaleService.T("columnProcess");
            ColPid.Header = LocaleService.T("columnPID");
            ColPath.Header = LocaleService.T("columnProgramPath");
            ColProtocol.Header = LocaleService.T("columnProtocol");
            ColLocal.Header = LocaleService.T("columnLocalAddress");
            ColRemote.Header = LocaleService.T("columnRemoteAddress");
            ColState.Header = LocaleService.T("columnConnectionState");
            ColBlocked.Header = LocaleService.T("columnBlocked");
            UpdateCountLabel();
        });
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            var connections = await Task.Run(NetworkMonitor.GetConnections);
            Dispatcher.Invoke(() =>
            {
                _connections.Clear();
                foreach (var c in connections)
                    _connections.Add(new ConnectionRow(c));
                UpdateCountLabel();
            });
        }
        catch { }
        finally { _isRefreshing = false; }
    }

    private void AutoRefreshBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutoRefreshBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (int.TryParse(tag, out var sec))
            {
                if (sec > 0)
                {
                    _timer.Interval = TimeSpan.FromSeconds(sec);
                    _timer.Start();
                }
                else _timer.Stop();
            }
        }
    }

    private void UpdateCountLabel()
    {
        ConnectionCountLabel.Text = LocaleService.T("connectionCount", _connections.Count);
    }

    public sealed class ConnectionRow(NetworkConnection c)
    {
        public string ProcessName => c.ProcessName;
        public int ProcessId => c.ProcessId;
        public string? ExecutablePath => c.ExecutablePath;
        public string Protocol => c.Protocol;
        public string LocalDisplay => $"{c.LocalAddress}:{c.LocalPort}";
        public string RemoteDisplay => c.Protocol == "UDP" ? "*" : $"{c.RemoteAddress}:{c.RemotePort}";
        public string State => c.State;
        public bool IsBlocked => c.IsBlockedByNetFence;
        public string BlockedDisplay => c.IsBlockedByNetFence
            ? LocaleService.T("blockedStatus")
            : LocaleService.T("allowedStatus");
    }
}
