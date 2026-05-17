using System.Collections.ObjectModel;
using System.Windows;
using NetFence.Core;
using NetFence.App.Services;

namespace NetFence.App.Pages;

public partial class ServicesTasksPage : System.Windows.Controls.UserControl
{
    private readonly ObservableCollection<ServiceInfo> _services = [];
    private readonly ObservableCollection<ScheduledTaskInfo> _tasks = [];

    public ServicesTasksPage()
    {
        InitializeComponent();
        ServicesGrid.ItemsSource = _services;
        TasksGrid.ItemsSource = _tasks;
        ApplyLocale();
        LocaleService.LanguageChanged += ApplyLocale;
    }

    private void ApplyLocale()
    {
        Dispatcher.Invoke(() =>
        {
            TargetPathLabel.Text = LocaleService.T("targetPath");
            ScanButton.Content = LocaleService.T("scanServices");
            ServicesTabHeader.Text = LocaleService.T("servicesTab");
            TasksTabHeader.Text = LocaleService.T("tasksTab");
            StopServiceButton.Content = LocaleService.T("stopService");
            DisableServiceButton.Content = LocaleService.T("disableService");
            DisableTaskButton.Content = LocaleService.T("disableTask");
            BlockTaskExeButton.Content = LocaleService.T("blockTaskExe");
            SvcNameCol.Header = LocaleService.T("columnServiceName");
            SvcDispCol.Header = LocaleService.T("columnDisplayName");
            SvcStatusCol.Header = LocaleService.T("columnServiceStatus");
            SvcStartCol.Header = LocaleService.T("columnStartMode");
            SvcPathCol.Header = LocaleService.T("columnServicePath");
            TaskNameCol.Header = LocaleService.T("columnTaskName");
            TaskPathCol.Header = LocaleService.T("columnTaskPath");
            TaskStateCol.Header = LocaleService.T("columnTaskState");
            TaskTriggersCol.Header = LocaleService.T("columnTaskTriggers");
            TaskActionsCol.Header = LocaleService.T("columnTaskActions");
        });
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = PathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                System.Windows.MessageBox.Show(LocaleService.T("selectTargetFirst"), "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var services = await Task.Run(() => ServiceScanner.ScanServices(path));
            _services.Clear();
            foreach (var s in services) _services.Add(s);

            var stasks = await Task.Run(() => ServiceScanner.ScanScheduledTasks(path));
            _tasks.Clear();
            foreach (var t in stasks) _tasks.Add(t);

            if (_services.Count == 0 && _tasks.Count == 0)
                System.Windows.MessageBox.Show(LocaleService.T("noServicesFound"), "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void StopServiceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ServicesGrid.SelectedItem is not ServiceInfo svc) return;
            if (svc.IsSystemService) { ShowSystemProtected(); return; }
            var confirm = System.Windows.MessageBox.Show(
                LocaleService.T("stopServiceConfirm", svc.Name), "NetFence",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            await Task.Run(() => ServiceScanner.StopService(svc.Name));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DisableServiceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ServicesGrid.SelectedItem is not ServiceInfo svc) return;
            if (svc.IsSystemService) { ShowSystemProtected(); return; }
            var confirm = System.Windows.MessageBox.Show(
                LocaleService.T("disableServiceConfirm", svc.Name), "NetFence",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            await Task.Run(() => ServiceScanner.DisableService(svc.Name));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DisableTaskButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TasksGrid.SelectedItem is not ScheduledTaskInfo task) return;
            var confirm = System.Windows.MessageBox.Show(
                LocaleService.T("disableTaskConfirm", task.Name), "NetFence",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            await Task.Run(() => ServiceScanner.DisableScheduledTask(task.Path, task.Name));
            System.Windows.MessageBox.Show(LocaleService.T("taskDisabled"), "NetFence",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BlockTaskExeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TasksGrid.SelectedItem is not ScheduledTaskInfo task) return;
            if (string.IsNullOrWhiteSpace(task.ExecutablePath))
            {
                System.Windows.MessageBox.Show(LocaleService.T("noExePathAvailable"), "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var exePath = task.ExecutablePath;
            await Task.Run(() => FirewallService.Block(exePath,
                System.IO.Path.GetFileNameWithoutExtension(exePath),
                false, Array.Empty<string>()));
            System.Windows.MessageBox.Show(LocaleService.T("blockedExe", exePath), "NetFence",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowSystemProtected()
    {
        System.Windows.MessageBox.Show(LocaleService.T("systemServiceProtected"), "NetFence",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
