# NetFence P3 — Real-time Network Monitoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace NetworkMonitorPage placeholder with a real-time network connection monitor using P/Invoke GetExtendedTcpTable/GetExtendedUdpTable.

**Architecture:** `NetworkMonitor.cs` wraps native iphlpapi P/Invoke calls into a clean `IReadOnlyList<NetworkConnection>` result. `NetworkMonitorPage` uses a `DispatcherTimer` for auto-refresh and a bound `DataGrid` for display. NetFence blocked status is cross-referenced from `FirewallService.GetStatus()`.

**Tech Stack:** .NET 9, WPF, P/Invoke (iphlpapi.dll), DispatcherTimer

---

### Task 1: NetworkMonitor.cs — P/Invoke + connection enumeration

**Files:**
- Create: `dotnet/NetFence.Core/NetworkMonitor.cs`
- Modify: `dotnet/NetFence.Core/Models.cs` (add NetworkConnection record)

- [ ] **Step 1: Add NetworkConnection record to Models.cs**

Add after the existing records (before the closing `}` of namespace):

```csharp
public sealed record NetworkConnection(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State,
    bool IsBlockedByNetFence);
```

- [ ] **Step 2: Create NetworkMonitor.cs**

```csharp
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace NetFence.Core;

public static class NetworkMonitor
{
    // TCP states
    private static readonly Dictionary<int, string> TcpStates = new()
    {
        [1] = "Closed",      [2] = "Listen",       [3] = "SynSent",
        [4] = "SynReceived", [5] = "Established",   [6] = "FinWait1",
        [7] = "FinWait2",    [8] = "CloseWait",     [9] = "LastAck",
        [10] = "LastAck",    [11] = "TimeWait",     [12] = "DeleteTcb"
    };

    public static IReadOnlyList<NetworkConnection> GetConnections()
    {
        var result = new List<NetworkConnection>();
        var netFencePrograms = GetNetFenceBlockedPrograms();

        EnumerateTcp(result, netFencePrograms);
        EnumerateUdp(result, netFencePrograms);

        return result;
    }

    private static HashSet<string> GetNetFenceBlockedPrograms()
    {
        var programs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var rule in FirewallService.GetStatus())
            {
                if (!string.IsNullOrWhiteSpace(rule.Program) && Path.IsPathFullyQualified(rule.Program))
                    programs.Add(Path.GetFullPath(rule.Program));
            }
        }
        catch { }
        return programs;
    }

    private static void EnumerateTcp(List<NetworkConnection> results, HashSet<string> blockedPrograms)
    {
        var bufSize = 0u;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref bufSize, false, 2, 5, 0);
        var buf = Marshal.AllocHGlobal((int)bufSize);
        try
        {
            if (GetExtendedTcpTable(buf, ref bufSize, false, 2, 5, 0) != 0) return;

            var count = Marshal.ReadInt32(buf);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            for (var i = 0; i < count; i++)
            {
                var rowPtr = buf + 4 + i * rowSize;
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);

                var localAddr = new IPAddress((long)row.dwLocalAddr);
                var remoteAddr = new IPAddress((long)row.dwRemoteAddr);
                var localPort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort);
                var remotePort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwRemotePort);
                var state = TcpStates.GetValueOrDefault((int)row.dwState, $"State{row.dwState}");

                var (procName, exePath) = ResolveProcess(row.dwOwningPid);
                var blocked = exePath is not null && blockedPrograms.Contains(exePath);

                results.Add(new NetworkConnection(
                    (int)row.dwOwningPid, procName, exePath, "TCP",
                    localAddr.ToString(), localPort,
                    remoteAddr.ToString(), remotePort,
                    state, blocked));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static void EnumerateUdp(List<NetworkConnection> results, HashSet<string> blockedPrograms)
    {
        var bufSize = 0u;
        _ = GetExtendedUdpTable(IntPtr.Zero, ref bufSize, false, 2, 1, 0);
        var buf = Marshal.AllocHGlobal((int)bufSize);
        try
        {
            if (GetExtendedUdpTable(buf, ref bufSize, false, 2, 1, 0) != 0) return;

            var count = Marshal.ReadInt32(buf);
            var rowSize = Marshal.SizeOf<MibUdpRowOwnerPid>();
            for (var i = 0; i < count; i++)
            {
                var rowPtr = buf + 4 + i * rowSize;
                var row = Marshal.PtrToStructure<MibUdpRowOwnerPid>(rowPtr);

                var localAddr = new IPAddress((long)row.dwLocalAddr);
                var localPort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort);

                var (procName, exePath) = ResolveProcess(row.dwOwningPid);
                var blocked = exePath is not null && blockedPrograms.Contains(exePath);

                results.Add(new NetworkConnection(
                    (int)row.dwOwningPid, procName, exePath, "UDP",
                    localAddr.ToString(), localPort,
                    "*", 0,
                    "-", blocked));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static (string ProcessName, string? ExePath) ResolveProcess(uint pid)
    {
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return (proc.ProcessName, proc.MainModule?.FileName);
        }
        catch
        {
            return ($"PID:{pid}", null);
        }
    }

    #region P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref uint dwOutBufLen, bool bOrder,
        uint ulAf, uint dwTableClass, uint dwReserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref uint dwOutBufLen, bool bOrder,
        uint ulAf, uint dwTableClass, uint dwReserved);

    #endregion
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build dotnet/NetFence.Core/NetFence.Core.csproj -c Debug
```
Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add dotnet/NetFence.Core/NetworkMonitor.cs dotnet/NetFence.Core/Models.cs
git commit -m "feat: add NetworkMonitor with P/Invoke GetExtendedTcpTable/GetExtendedUdpTable"
```

---

### Task 2: Rewrite NetworkMonitorPage — full UI with auto-refresh

**Files:**
- Modify: `dotnet/NetFence.App/Pages/NetworkMonitorPage.xaml`
- Modify: `dotnet/NetFence.App/Pages/NetworkMonitorPage.xaml.cs`

- [ ] **Step 1: Rewrite NetworkMonitorPage.xaml**

```xml
<UserControl x:Class="NetFence.App.Pages.NetworkMonitorPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="16" Background="{DynamicResource ContentBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Toolbar -->
        <Grid Grid.Row="0" Margin="0,0,0,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <Button x:Name="RefreshButton" MinWidth="80" Height="28"
                    Click="RefreshButton_Click"
                    Background="{DynamicResource ButtonPrimaryBackground}"
                    Foreground="{DynamicResource ButtonPrimaryForeground}"
                    BorderThickness="0"/>
            <StackPanel Grid.Column="1" Orientation="Horizontal" Margin="12,0,0,0">
                <TextBlock x:Name="AutoRefreshLabel" VerticalAlignment="Center"
                           Foreground="{DynamicResource SecondaryText}" Margin="0,0,6,0"/>
                <ComboBox x:Name="AutoRefreshBox" Width="90" Height="28"
                          SelectionChanged="AutoRefreshBox_SelectionChanged">
                    <ComboBoxItem x:Name="RefreshOff" Tag="0"/>
                    <ComboBoxItem x:Name="Refresh1s" Tag="1"/>
                    <ComboBoxItem x:Name="Refresh2s" Tag="2"/>
                    <ComboBoxItem x:Name="Refresh5s" Tag="5"/>
                    <ComboBoxItem x:Name="Refresh10s" Tag="10"/>
                </ComboBox>
            </StackPanel>
            <TextBlock x:Name="ConnectionCountLabel" Grid.Column="3"
                       VerticalAlignment="Center"
                       Foreground="{DynamicResource SecondaryText}"/>
        </Grid>

        <!-- Connections DataGrid -->
        <DataGrid x:Name="ConnectionsGrid" Grid.Row="1" AutoGenerateColumns="False"
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
                <DataGridTextColumn x:Name="ColProcess" Binding="{Binding ProcessName}" Width="130"/>
                <DataGridTextColumn x:Name="ColPid" Binding="{Binding ProcessId}" Width="60"/>
                <DataGridTextColumn x:Name="ColPath" Binding="{Binding ExecutablePath}" Width="280"/>
                <DataGridTextColumn x:Name="ColProtocol" Binding="{Binding Protocol}" Width="60"/>
                <DataGridTextColumn x:Name="ColLocal" Binding="{Binding LocalDisplay}" Width="170"/>
                <DataGridTextColumn x:Name="ColRemote" Binding="{Binding RemoteDisplay}" Width="170"/>
                <DataGridTextColumn x:Name="ColState" Binding="{Binding State}" Width="100"/>
                <DataGridTextColumn x:Name="ColBlocked" Binding="{Binding BlockedDisplay}" Width="80"/>
            </DataGrid.Columns>
        </DataGrid>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Rewrite NetworkMonitorPage.xaml.cs**

```csharp
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
            ? App.Services.LocaleService.T("blockedStatus")
            : App.Services.LocaleService.T("allowedStatus");
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug
```

---

### Task 3: Add translation keys

**Files:**
- Modify: `dotnet/NetFence.App/Services/LocaleService.cs`

- [ ] **Step 1: Add keys to en-US dictionary**

Before the closing `};` of en-US section, add:
```csharp
            ["autoRefresh"] = "Auto-refresh",
            ["autoRefreshOff"] = "Off",
            ["refreshInterval1s"] = "1 second",
            ["refreshInterval2s"] = "2 seconds",
            ["refreshInterval5s"] = "5 seconds",
            ["refreshInterval10s"] = "10 seconds",
            ["columnProcess"] = "Process",
            ["columnPID"] = "PID",
            ["columnProtocol"] = "Protocol",
            ["columnLocalAddress"] = "Local Address",
            ["columnRemoteAddress"] = "Remote Address",
            ["columnConnectionState"] = "State",
            ["columnBlocked"] = "Blocked",
            ["connectionCount"] = "{0} connection(s)",
            ["blockedStatus"] = "Blocked",
            ["allowedStatus"] = "Allowed",
```

- [ ] **Step 2: Add keys to zh-CN dictionary**

```csharp
            ["autoRefresh"] = "自动刷新",
            ["autoRefreshOff"] = "关闭",
            ["refreshInterval1s"] = "1 秒",
            ["refreshInterval2s"] = "2 秒",
            ["refreshInterval5s"] = "5 秒",
            ["refreshInterval10s"] = "10 秒",
            ["columnProcess"] = "进程",
            ["columnPID"] = "PID",
            ["columnProtocol"] = "协议",
            ["columnLocalAddress"] = "本地地址",
            ["columnRemoteAddress"] = "远程地址",
            ["columnConnectionState"] = "状态",
            ["columnBlocked"] = "拦截",
            ["connectionCount"] = "{0} 条连接",
            ["blockedStatus"] = "已阻断",
            ["allowedStatus"] = "未阻断",
```

- [ ] **Step 3: Remove old placeholder key**

Remove `"networkMonitorComing"` from both en-US and zh-CN dictionaries (no longer needed).

- [ ] **Step 4: Commit**

```bash
git add dotnet/NetFence.App/Pages/NetworkMonitorPage.xaml dotnet/NetFence.App/Pages/NetworkMonitorPage.xaml.cs dotnet/NetFence.App/Services/LocaleService.cs
git commit -m "feat: rewrite NetworkMonitorPage with real-time connections, auto-refresh, and translations"
```

---

### Task 4: Integration test

- [ ] **Step 1: Build Release**

```bash
dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Release
```
Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Run core tests**

```bash
dotnet run --project dotnet/NetFence.Core.Tests/NetFence.Core.Tests.csproj -c Release
```
Expected: All tests pass.

- [ ] **Step 3: Manual verification**

1. Navigate to "联网监控" — should see a list of current connections
2. Click Refresh — connections update
3. Select different auto-refresh intervals — timer adjusts
4. Select "关闭" — auto-refresh stops
5. Click column headers — sorting works
6. Switch to another page → timer pauses; switch back → resumes
7. Block a program from Scan & Block page → it appears as "已阻断" in monitor page
8. Language switch → all column headers and labels update
