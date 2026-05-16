# NetFence P4 — Service + Scheduled Task Scanning

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace ServicesTasksPage placeholder with functional dual-tab page for scanning Windows services and scheduled tasks related to a target path.

**Architecture:** `ServiceScanner.cs` uses `ServiceController` for enumeration + `Microsoft.Win32.Registry` for exe paths + PowerShell for stop/disable operations. Scheduled tasks enumerated via PowerShell `Get-ScheduledTask`.

**Tech Stack:** .NET 9, System.ServiceProcess.ServiceController, PowerShell, WMI

---

### Task 1: ServiceScanner.cs — service enumeration + operations

**Files:**
- Create: `dotnet/NetFence.Core/ServiceScanner.cs`
- Modify: `dotnet/NetFence.Core/Models.cs` (add ServiceInfo + ScheduledTaskInfo records)
- Modify: `dotnet/NetFence.Core/NetFence.Core.csproj` (add System.ServiceProcess package)

- [ ] **Step 1: Add NuGet package**

```bash
cd D:\Desktop\workspace\NetFence\dotnet\NetFence.Core && dotnet add package System.ServiceProcess.ServiceController
```

- [ ] **Step 2: Add model records to Models.cs**

```csharp
public sealed record ServiceInfo(
    string Name,
    string DisplayName,
    string Status,
    string StartMode,
    string? ExecutablePath,
    bool IsSystemService);

public sealed record ScheduledTaskInfo(
    string Name,
    string Path,
    string State,
    string Triggers,
    string Actions,
    string? ExecutablePath);
```

- [ ] **Step 3: Create ServiceScanner.cs**

```csharp
using System.ServiceProcess;
using Microsoft.Win32;

namespace NetFence.Core;

public static class ServiceScanner
{
    private static readonly HashSet<string> SystemServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost.exe", "lsass.exe", "wininit.exe", "services.exe",
        "csrss.exe", "smss.exe", "winlogon.exe", "explorer.exe",
        "RuntimeBroker.exe", "SearchHost.exe", "Registry", "spoolsv.exe",
        "WSearch", "MsMpEng.exe", "SecurityHealthService.exe", "SgrmBroker.exe"
    };

    public static IReadOnlyList<ServiceInfo> ScanServices(string targetPath)
    {
        if (!Directory.Exists(targetPath) && !File.Exists(targetPath))
            throw new InvalidOperationException($"Path not found: {targetPath}");

        var installDir = Directory.Exists(targetPath) ? targetPath : Path.GetDirectoryName(targetPath)!;
        var results = new List<ServiceInfo>();

        foreach (var svc in ServiceController.GetServices())
        {
            try
            {
                var exePath = GetServiceImagePath(svc.ServiceName);
                var isRelated = false;

                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    if (NetFenceRules.IsPathUnderDirectory(exePath, installDir))
                        isRelated = true;
                }

                if (!isRelated &&
                    (svc.ServiceName.Contains(targetPath, StringComparison.OrdinalIgnoreCase) ||
                     svc.DisplayName.Contains(targetPath, StringComparison.OrdinalIgnoreCase) ||
                     svc.DisplayName.Contains(Path.GetFileName(installDir), StringComparison.OrdinalIgnoreCase)))
                    isRelated = true;

                if (!isRelated) continue;

                var exeName = exePath is not null ? Path.GetFileName(exePath) : null;
                var isSystem = exeName is not null && SystemServices.Contains(exeName);

                results.Add(new ServiceInfo(
                    svc.ServiceName,
                    svc.DisplayName,
                    svc.Status.ToString(),
                    svc.StartType.ToString(),
                    exePath,
                    isSystem));
            }
            catch { }
        }

        return results.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void StopService(string serviceName)
    {
        if (IsSystemServiceName(serviceName))
            throw new InvalidOperationException("Cannot stop a system service.");

        using var svc = new ServiceController(serviceName);
        svc.Stop();
        svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
    }

    public static void DisableService(string serviceName)
    {
        if (IsSystemServiceName(serviceName))
            throw new InvalidOperationException("Cannot disable a system service.");

        PowerShellRunner.RunRequired(
            $"Set-Service -Name {PowerShellRunner.Quote(serviceName)} -StartupType Disabled");
    }

    private static bool IsSystemServiceName(string name)
    {
        try
        {
            var exePath = GetServiceImagePath(name);
            var exeName = exePath is not null ? Path.GetFileName(exePath) : null;
            return exeName is not null && SystemServices.Contains(exeName);
        }
        catch { return true; }
    }

    private static string? GetServiceImagePath(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @$"SYSTEM\CurrentControlSet\Services\{serviceName}");
            var imagePath = key?.GetValue("ImagePath") as string;
            if (string.IsNullOrWhiteSpace(imagePath)) return null;

            // Handle "\??\C:\..." or "C:\..."
            var path = imagePath.Trim('"').Trim();
            if (path.StartsWith(@"\??\")) path = path[4..];
            if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path);
            if (path.Contains("svchost.exe", StringComparison.OrdinalIgnoreCase))
            {
                var argsIdx = path.IndexOf(" -k ", StringComparison.OrdinalIgnoreCase);
                if (argsIdx > 0) path = path[..argsIdx].Trim();
            }
            return File.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch { return null; }
    }

    public static IReadOnlyList<ScheduledTaskInfo> ScanScheduledTasks(string targetPath)
    {
        if (!Directory.Exists(targetPath) && !File.Exists(targetPath))
            throw new InvalidOperationException($"Path not found: {targetPath}");

        var installDir = Directory.Exists(targetPath) ? targetPath : Path.GetDirectoryName(targetPath)!;
        var quotedDir = PowerShellRunner.Quote(installDir);

        var script = $"""
            $ErrorActionPreference = 'Stop'
            Get-ScheduledTask -ErrorAction SilentlyContinue |
                ForEach-Object {{
                    $task = $_
                    $actions = @($task.Actions | ForEach-Object {{ $_.Execute + ' ' + ($_.Arguments ?? '') }})
                    $actionText = $actions -join '; '
                    $triggers = @($task.Triggers | ForEach-Object {{ $_.CimClass.CimClassName }} | Select-Object -Unique)
                    $triggerText = $triggers -join ', '
                    [PSCustomObject]@{{
                        Name = $task.TaskName
                        Path = $task.TaskPath
                        State = [string]$task.State
                        Triggers = $triggerText
                        Actions = $actionText
                        ExecutablePath = [string]$task.Actions[0].Execute
                    }}
                }} |
                Where-Object {{
                    $_.ExecutablePath -like '*{EscapeForPowerShell(installDir)}*' -or
                    $_.Actions -like '*updater*' -or
                    $_.Actions -like '*update*' -or
                    $_.Actions -like '*helper*' -or
                    $_.Actions -like '*launcher*'
                }} |
                ConvertTo-Csv -NoTypeInformation
            """;

        return LiveSystemInfo.ParseCsv(PowerShellRunner.RunRequired(script))
            .Select(row => new ScheduledTaskInfo(
                row.GetValueOrDefault("Name") ?? "",
                row.GetValueOrDefault("Path") ?? "",
                row.GetValueOrDefault("State") ?? "",
                row.GetValueOrDefault("Triggers") ?? "",
                row.GetValueOrDefault("Actions") ?? "",
                EmptyToNull(row.GetValueOrDefault("ExecutablePath"))))
            .ToArray();
    }

    private static string EscapeForPowerShell(string path) => path.Replace("'", "''");

    private static string? EmptyToNull(string? v) => string.IsNullOrWhiteSpace(v) ? null : v;

    public static void DisableScheduledTask(string taskPath)
    {
        PowerShellRunner.RunRequired(
            $"Disable-ScheduledTask -TaskPath {PowerShellRunner.Quote(taskPath)}");
    }

    public static string? GetTaskExecutablePath(string taskPath)
    {
        var script = $"""
            (Get-ScheduledTask -TaskPath {PowerShellRunner.Quote(taskPath)} |
                Select-Object -ExpandProperty Actions)[0].Execute
            """;
        var result = PowerShellRunner.RunRequired(script).Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
```

- [ ] **Step 4: Verify build**

```bash
dotnet build dotnet/NetFence.Core/NetFence.Core.csproj -c Debug
```
Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add dotnet/NetFence.Core/ServiceScanner.cs dotnet/NetFence.Core/Models.cs dotnet/NetFence.Core/NetFence.Core.csproj
git commit -m "feat: add ServiceScanner with ServiceController enumeration and scheduled task PowerShell scanning"
```

---

### Task 2: Rewrite ServicesTasksPage — dual-tab UI

**Files:**
- Modify: `dotnet/NetFence.App/Pages/ServicesTasksPage.xaml`
- Modify: `dotnet/NetFence.App/Pages/ServicesTasksPage.xaml.cs`

- [ ] **Step 1: Rewrite ServicesTasksPage.xaml**

```xml
<UserControl x:Class="NetFence.App.Pages.ServicesTasksPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="16" Background="{DynamicResource ContentBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Path input + Scan -->
        <Grid Grid.Row="0" Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="80"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBlock x:Name="TargetPathLabel" VerticalAlignment="Center"
                       Foreground="{DynamicResource PrimaryText}"/>
            <TextBox x:Name="PathBox" Grid.Column="1" Height="30" Margin="0,0,8,0"
                     VerticalContentAlignment="Center"
                     Background="{DynamicResource InputBackground}"
                     Foreground="{DynamicResource PrimaryText}"
                     BorderBrush="{DynamicResource BorderColor}"/>
            <Button x:Name="ScanButton" Grid.Column="2" MinWidth="100" Height="30"
                    Click="ScanButton_Click"
                    Background="{DynamicResource ButtonPrimaryBackground}"
                    Foreground="{DynamicResource ButtonPrimaryForeground}" BorderThickness="0"/>
        </Grid>

        <!-- Dual Tab -->
        <TabControl Grid.Row="1" Background="{DynamicResource ContentBackground}"
                    BorderBrush="{DynamicResource BorderColor}">
            <TabControl.Resources>
                <Style TargetType="TabPanel">
                    <Setter Property="Background" Value="{DynamicResource PanelBackground}"/>
                </Style>
                <Style TargetType="TabItem">
                    <Setter Property="Background" Value="Transparent"/>
                    <Setter Property="Foreground" Value="{DynamicResource TabInactiveText}"/>
                    <Setter Property="Padding" Value="14,7"/>
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="TabItem">
                                <Border Name="TabBorder" Background="Transparent"
                                        BorderThickness="0,0,0,2" BorderBrush="Transparent"
                                        Padding="{TemplateBinding Padding}" Cursor="Hand">
                                    <ContentPresenter ContentSource="Header"
                                                      HorizontalAlignment="Center"
                                                      TextElement.Foreground="{TemplateBinding Foreground}"/>
                                </Border>
                                <ControlTemplate.Triggers>
                                    <Trigger Property="IsSelected" Value="True">
                                        <Setter TargetName="TabBorder" Property="BorderBrush"
                                                Value="{DynamicResource TabSelectedUnderline}"/>
                                        <Setter Property="Foreground" Value="{DynamicResource PrimaryText}"/>
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </TabControl.Resources>

            <!-- Services tab -->
            <TabItem>
                <TabItem.Header>
                    <TextBlock x:Name="ServicesTabHeader"/>
                </TabItem.Header>
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    <DataGrid x:Name="ServicesGrid" AutoGenerateColumns="False"
                              CanUserAddRows="False" IsReadOnly="True"
                              Background="{DynamicResource ContentBackground}"
                              Foreground="{DynamicResource PrimaryText}"
                              AlternatingRowBackground="{DynamicResource GridRowAlt}"
                              BorderBrush="{DynamicResource BorderColor}"
                              ColumnHeaderStyle="{StaticResource DataGridColumnHeaderStyle}">
                        <DataGrid.Resources>
                            <Style TargetType="TextBlock">
                                <Setter Property="Foreground" Value="{DynamicResource PrimaryText}"/>
                            </Style>
                        </DataGrid.Resources>
                        <DataGrid.Columns>
                            <DataGridTextColumn x:Name="SvcNameCol" Binding="{Binding Name}" Width="140"/>
                            <DataGridTextColumn x:Name="SvcDispCol" Binding="{Binding DisplayName}" Width="200"/>
                            <DataGridTextColumn x:Name="SvcStatusCol" Binding="{Binding Status}" Width="80"/>
                            <DataGridTextColumn x:Name="SvcStartCol" Binding="{Binding StartMode}" Width="80"/>
                            <DataGridTextColumn x:Name="SvcPathCol" Binding="{Binding ExecutablePath}" Width="300"/>
                        </DataGrid.Columns>
                    </DataGrid>
                    <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,8,0,0">
                        <Button x:Name="StopServiceButton" MinWidth="100" Height="28" Margin="0,0,8,0"
                                Click="StopServiceButton_Click"
                                Background="{DynamicResource ButtonDangerBackground}"
                                Foreground="{DynamicResource ButtonDangerForeground}" BorderThickness="0"/>
                        <Button x:Name="DisableServiceButton" MinWidth="110" Height="28"
                                Click="DisableServiceButton_Click"
                                Background="{DynamicResource ButtonSecondaryBackground}"
                                Foreground="{DynamicResource ButtonSecondaryForeground}"
                                BorderBrush="{DynamicResource ButtonSecondaryBorder}"/>
                    </StackPanel>
                </Grid>
            </TabItem>

            <!-- Tasks tab -->
            <TabItem>
                <TabItem.Header>
                    <TextBlock x:Name="TasksTabHeader"/>
                </TabItem.Header>
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    <DataGrid x:Name="TasksGrid" AutoGenerateColumns="False"
                              CanUserAddRows="False" IsReadOnly="True"
                              Background="{DynamicResource ContentBackground}"
                              Foreground="{DynamicResource PrimaryText}"
                              AlternatingRowBackground="{DynamicResource GridRowAlt}"
                              BorderBrush="{DynamicResource BorderColor}"
                              ColumnHeaderStyle="{StaticResource DataGridColumnHeaderStyle}">
                        <DataGrid.Resources>
                            <Style TargetType="TextBlock">
                                <Setter Property="Foreground" Value="{DynamicResource PrimaryText}"/>
                            </Style>
                        </DataGrid.Resources>
                        <DataGrid.Columns>
                            <DataGridTextColumn x:Name="TaskNameCol" Binding="{Binding Name}" Width="160"/>
                            <DataGridTextColumn x:Name="TaskPathCol" Binding="{Binding Path}" Width="140"/>
                            <DataGridTextColumn x:Name="TaskStateCol" Binding="{Binding State}" Width="80"/>
                            <DataGridTextColumn x:Name="TaskTriggersCol" Binding="{Binding Triggers}" Width="130"/>
                            <DataGridTextColumn x:Name="TaskActionsCol" Binding="{Binding Actions}" Width="300"/>
                        </DataGrid.Columns>
                    </DataGrid>
                    <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,8,0,0">
                        <Button x:Name="DisableTaskButton" MinWidth="100" Height="28" Margin="0,0,8,0"
                                Click="DisableTaskButton_Click"
                                Background="{DynamicResource ButtonDangerBackground}"
                                Foreground="{DynamicResource ButtonDangerForeground}" BorderThickness="0"/>
                        <Button x:Name="BlockTaskExeButton" MinWidth="110" Height="28"
                                Click="BlockTaskExeButton_Click"
                                Background="{DynamicResource ButtonSecondaryBackground}"
                                Foreground="{DynamicResource ButtonSecondaryForeground}"
                                BorderBrush="{DynamicResource ButtonSecondaryBorder}"/>
                    </StackPanel>
                </Grid>
            </TabItem>
        </TabControl>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Rewrite ServicesTasksPage.xaml.cs**

```csharp
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

            var tasks = await Task.Run(() => ServiceScanner.ScanScheduledTasks(path));
            _tasks.Clear();
            foreach (var t in tasks) _tasks.Add(t);

            if (_services.Count == 0 && _tasks.Count == 0)
                System.Windows.MessageBox.Show(LocaleService.T("noServicesFound"), "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopServiceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ServicesGrid.SelectedItem is not ServiceInfo svc) return;
            if (svc.IsSystemService) { ShowSystemProtected(); return; }

            var confirm = System.Windows.MessageBox.Show(
                LocaleService.T("stopServiceConfirm", svc.Name), "NetFence",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            Task.Run(() => ServiceScanner.StopService(svc.Name));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DisableServiceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ServicesGrid.SelectedItem is not ServiceInfo svc) return;
            if (svc.IsSystemService) { ShowSystemProtected(); return; }

            var confirm = System.Windows.MessageBox.Show(
                LocaleService.T("disableServiceConfirm", svc.Name), "NetFence",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            Task.Run(() => ServiceScanner.DisableService(svc.Name));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DisableTaskButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TasksGrid.SelectedItem is not ScheduledTaskInfo task) return;
            var fullPath = task.Path.TrimEnd('\\') + "\\" + task.Name;
            Task.Run(() => ServiceScanner.DisableScheduledTask(fullPath));
            System.Windows.MessageBox.Show("Task disabled.", "NetFence",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BlockTaskExeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TasksGrid.SelectedItem is not ScheduledTaskInfo task) return;
            if (string.IsNullOrWhiteSpace(task.ExecutablePath))
            {
                System.Windows.MessageBox.Show("No executable path available.", "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var exePath = task.ExecutablePath;
            Task.Run(() => FirewallService.Block(exePath, System.IO.Path.GetFileNameWithoutExtension(exePath),
                false, Array.Empty<string>()));
            System.Windows.MessageBox.Show($"Blocked: {exePath}", "NetFence",
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
```

- [ ] **Step 3: Verify build**

```bash
dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug
```

- [ ] **Step 4: Commit**

```bash
git add dotnet/NetFence.App/Pages/ServicesTasksPage.xaml dotnet/NetFence.App/Pages/ServicesTasksPage.xaml.cs
git commit -m "feat: rewrite ServicesTasksPage with dual-tab service and scheduled task scanning"
```

---

### Task 3: Translation keys

**Files:**
- Modify: `dotnet/NetFence.App/Services/LocaleService.cs`

- [ ] **Step 1: Add en-US keys + remove placeholder**

```csharp
            ["servicesTab"] = "Services",
            ["tasksTab"] = "Scheduled Tasks",
            ["scanServices"] = "Scan",
            ["stopService"] = "Stop Service",
            ["disableService"] = "Disable Startup",
            ["disableTask"] = "Disable Task",
            ["blockTaskExe"] = "Block Network",
            ["columnServiceName"] = "Name",
            ["columnDisplayName"] = "Display Name",
            ["columnServiceStatus"] = "Status",
            ["columnStartMode"] = "Startup",
            ["columnServicePath"] = "Path",
            ["columnTaskName"] = "Task Name",
            ["columnTaskPath"] = "Path",
            ["columnTaskState"] = "State",
            ["columnTaskTriggers"] = "Triggers",
            ["columnTaskActions"] = "Actions",
            ["stopServiceConfirm"] = "Stop service '{0}'?",
            ["disableServiceConfirm"] = "Disable auto-start for service '{0}'?",
            ["systemServiceProtected"] = "System service — cannot modify.",
            ["noServicesFound"] = "No related services or tasks found.",
```

- [ ] **Step 2: Add zh-CN keys**

```csharp
            ["servicesTab"] = "服务",
            ["tasksTab"] = "计划任务",
            ["scanServices"] = "扫描",
            ["stopService"] = "停止服务",
            ["disableService"] = "禁用自启动",
            ["disableTask"] = "禁用任务",
            ["blockTaskExe"] = "阻止联网",
            ["columnServiceName"] = "名称",
            ["columnDisplayName"] = "显示名称",
            ["columnServiceStatus"] = "状态",
            ["columnStartMode"] = "启动类型",
            ["columnServicePath"] = "路径",
            ["columnTaskName"] = "任务名",
            ["columnTaskPath"] = "路径",
            ["columnTaskState"] = "状态",
            ["columnTaskTriggers"] = "触发器",
            ["columnTaskActions"] = "动作",
            ["stopServiceConfirm"] = "停止服务 '{0}'？",
            ["disableServiceConfirm"] = "禁用服务 '{0}' 的自启动？",
            ["systemServiceProtected"] = "系统服务，不可修改。",
            ["noServicesFound"] = "未发现关联服务或计划任务。",
```

- [ ] **Step 3: Remove old placeholder**

Remove `"servicesTasksComing"` from both dictionaries.

- [ ] **Step 4: Commit**

```bash
git add dotnet/NetFence.App/Services/LocaleService.cs
git commit -m "feat: add service and task scanning translation keys"
```

---

### Task 4: Integration test

- [ ] **Step 1: Build Release**

```bash
dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Release
```

- [ ] **Step 2: Run core tests**

```bash
dotnet run --project dotnet/NetFence.Core.Tests/NetFence.Core.Tests.csproj -c Release
```

- [ ] **Step 3: Manual verification**
1. Navigate to "服务/计划任务" — two tabs visible
2. Enter a path, click Scan — services and tasks populate
3. System services marked protected — stop/disable buttons should validate
4. Stop a non-system service → confirmation → service stops
5. Disable startup → service startup type changes
6. Disable a scheduled task → task state changes
7. Block task exe → firewall rules created
