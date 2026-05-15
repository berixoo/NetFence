# NetFence P1 UI Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite NetFence WPF app shell with sidebar navigation, 5 page modules, dark/light/system theme support, and extract scan/block logic into a dedicated UserControl.

**Architecture:** MainWindow becomes a thin shell (sidebar + ContentControl + status bar). Each feature page is an independent UserControl. A static `LocaleService` provides translations with a `LanguageChanged` event for page refresh. `ThemeService` manages 3-mode theme switching via ResourceDictionary replacement, persisted through `SettingsService`.

**Tech Stack:** WPF .NET 9, XAML ResourceDictionary themes, System.Windows.Forms (for FolderBrowserDialog)

---

### Task 1: SettingsService — persist user preferences

**Files:**
- Create: `dotnet/NetFence.App/Services/SettingsService.cs`

- [ ] **Step 1: Write SettingsService**

```csharp
using System.IO;
using System.Text.Json;

namespace NetFence.App.Services;

public static class SettingsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetFence",
        "settings.json");

    private static SettingsData? _current;

    public static string Theme
    {
        get => Load().Theme;
        set { Load().Theme = value; Save(); }
    }

    public static string Language
    {
        get => Load().Language;
        set { Load().Language = value; Save(); }
    }

    private static SettingsData Load()
    {
        if (_current is not null) return _current;
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                _current = JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
            }
            else
            {
                _current = new SettingsData();
            }
        }
        catch
        {
            _current = new SettingsData();
        }
        return _current;
    }

    private static void Save()
    {
        if (_current is null) return;
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_current));
    }

    private sealed class SettingsData
    {
        public string Theme { get; set; } = "system";
        public string Language { get; set; } = "";
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug`

- [ ] **Step 3: Commit**

```bash
git add dotnet/NetFence.App/Services/SettingsService.cs
git commit -m "feat: add SettingsService for persisting user preferences"
```

---

### Task 2: ThemeService — dark/light/system theme switching

**Files:**
- Create: `dotnet/NetFence.App/Services/ThemeService.cs`

- [ ] **Step 1: Write ThemeService**

```csharp
using System.Windows;
using Microsoft.Win32;

namespace NetFence.App.Services;

public static class ThemeService
{
    public const string SystemKey = "system";
    public const string DarkKey = "dark";
    public const string LightKey = "light";

    private const string DarkDictUri = "Themes/DarkTheme.xaml";
    private const string LightDictUri = "Themes/LightTheme.xaml";

    private static ResourceDictionary? _darkDict;
    private static ResourceDictionary? _lightDict;

    public static void Apply(string theme)
    {
        var resolved = theme == SystemKey ? ReadSystemTheme() : theme;
        var merged = Application.Current.Resources.MergedDictionaries;

        _darkDict ??= new ResourceDictionary { Source = new Uri(DarkDictUri, UriKind.Relative) };
        _lightDict ??= new ResourceDictionary { Source = new Uri(LightDictUri, UriKind.Relative) };

        merged.Remove(_darkDict);
        merged.Remove(_lightDict);
        merged.Add(resolved == DarkKey ? _darkDict : _lightDict);

        SettingsService.Theme = theme;
    }

    public static string GetEffectiveTheme(string stored)
    {
        return stored == SystemKey ? ReadSystemTheme() : stored;
    }

    private static string ReadSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
                return v == 0 ? DarkKey : LightKey;
        }
        catch { }
        return LightKey;
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug`

- [ ] **Step 3: Commit**

```bash
git add dotnet/NetFence.App/Services/ThemeService.cs
git commit -m "feat: add ThemeService for dark/light/system theme switching"
```

---

### Task 3: LocaleService — shared translation infrastructure

**Files:**
- Create: `dotnet/NetFence.App/Services/LocaleService.cs`

- [ ] **Step 1: Write LocaleService**

This contains the full translation dictionary extracted from MainWindow.xaml.cs plus keys for the new sidebar and settings pages, plus placeholder page messages. The file is large but self-contained — this is the single source of truth for all UI text.

```csharp
namespace NetFence.App.Services;

public static class LocaleService
{
    public static event Action? LanguageChanged;

    private static string _currentLanguage = "";

    public static string CurrentLanguage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_currentLanguage))
            {
                _currentLanguage = SettingsService.Language;
                if (string.IsNullOrWhiteSpace(_currentLanguage))
                {
                    _currentLanguage = Thread.CurrentThread.CurrentUICulture.Name
                        .StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                        ? "zh-CN" : "en-US";
                }
            }
            return _currentLanguage;
        }
        set
        {
            if (!string.Equals(_currentLanguage, value, StringComparison.OrdinalIgnoreCase))
            {
                _currentLanguage = value;
                SettingsService.Language = value;
                LanguageChanged?.Invoke();
            }
        }
    }

    public static string T(string key)
    {
        var map = Translations.TryGetValue(CurrentLanguage, out var selected)
            ? selected : Translations["en-US"];
        return map.TryGetValue(key, out var value) ? value
            : Translations["en-US"].GetValueOrDefault(key, key);
    }

    public static string T(string key, params object[] args)
    {
        try { return string.Format(T(key), args); }
        catch (FormatException) { return T(key) + " " + string.Join(" ", args); }
    }

    public static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["en-US"] = new()
        {
            ["navScanBlock"] = "Scan & Block",
            ["navNetworkMonitor"] = "Network Monitor",
            ["navServicesTasks"] = "Services & Tasks",
            ["navRuleProfiles"] = "Rule Profiles",
            ["navSettings"] = "Settings",
            ["ready"] = "Ready",
            ["adminEnabled"] = "Administrator: firewall changes enabled",
            ["targetPath"] = "Target path",
            ["selectExe"] = "Select EXE",
            ["selectFolder"] = "Select Folder",
            ["ruleName"] = "Rule name",
            ["includeChildProcesses"] = "Include executable files from currently running child processes",
            ["scanRelated"] = "Scan Related",
            ["blockNetwork"] = "Block Network",
            ["unblock"] = "Unblock",
            ["unblockSelected"] = "Unblock Selected",
            ["unblockAll"] = "Unblock All",
            ["exportSnapshot"] = "Export Snapshot",
            ["openLog"] = "Open Log",
            ["refresh"] = "Refresh",
            ["relatedCandidates"] = "Related candidates",
            ["firewallRules"] = "Firewall rules",
            ["columnBlock"] = "Block",
            ["columnReason"] = "Reason",
            ["columnPid"] = "PID",
            ["columnProcess"] = "Process",
            ["columnProgramPath"] = "Program path",
            ["columnRule"] = "Rule",
            ["columnDirection"] = "Direction",
            ["columnEnabled"] = "Enabled",
            ["columnAction"] = "Action",
            ["scanRunning"] = "Scanning related executable files...",
            ["blockRunning"] = "Applying persistent firewall block rules...",
            ["unblockRunning"] = "Removing NetFence firewall rules...",
            ["unblockAllRunning"] = "Removing all NetFence firewall rules...",
            ["exportRunning"] = "Exporting current rules and candidates...",
            ["refreshRunning"] = "Refreshing firewall rule status...",
            ["loadedRules"] = "Loaded {0} NetFence firewall rule(s).",
            ["readStatusFailed"] = "Failed to read firewall status: {0}",
            ["foundCandidates"] = "Found {0} related candidate executable(s). Review checkboxes before blocking.",
            ["scanFailed"] = "Scan failed: {0}",
            ["selectTargetFirst"] = "Select an .exe file or a folder first.",
            ["pathDoesNotExist"] = "Path does not exist: {0}",
            ["folderDialogDescription"] = "Select a folder containing executable files",
            ["blockedTargets"] = "Blocked ''{0}'' for {1} executable file(s).",
            ["blockFailed"] = "Block failed: {0}",
            ["unblockedRules"] = "Unblocked ''{0}'' and removed {1} rule(s).",
            ["unblockFailed"] = "Unblock failed: {0}",
            ["selectRuleFirst"] = "Select one or more firewall rules first.",
            ["unblockSelectedRunning"] = "Removing firewall rules for selected executable file(s)...",
            ["unblockedSelectedRules"] = "Removed {0} rule(s) for {1} selected executable file(s).",
            ["unblockSelectedFailed"] = "Unblock selected failed: {0}",
            ["unblockSelectedConfirmTitle"] = "Unblock selected executable files",
            ["unblockSelectedConfirmMessage"] = "This removes all NetFence rules for {0} selected executable file(s). Continue?",
            ["unblockedAllRules"] = "Removed all NetFence rules: {0} rule(s).",
            ["unblockAllFailed"] = "Unblock all failed: {0}",
            ["unblockAllConfirmTitle"] = "Remove all NetFence rules",
            ["unblockAllConfirmMessage"] = "This removes every firewall rule managed by NetFence. Continue?",
            ["exportComplete"] = "Exported {0} row(s) to {1}",
            ["exportFailed"] = "Export failed: {0}",
            ["openLogFailed"] = "Open log failed: {0}",
            ["languageSwitchFailed"] = "Language switch failed: {0}",
            ["firstRunTitle"] = "NetFence safety notice",
            ["firstRunMessage"] = "NetFence changes Windows Defender Firewall rules and requires administrator permission for block/unblock actions. Rules persist after closing this tool. Use Unblock or Unblock All to restore networking. Continue?",
            ["networkMonitorComing"] = "Network monitoring will be available in a future update.",
            ["servicesTasksComing"] = "Service and scheduled task scanning will be available in a future update.",
            ["ruleProfilesComing"] = "Rule profile management will be available in a future update.",
            ["languageLabel"] = "Language",
            ["languageEnglish"] = "English",
            ["languageChinese"] = "Chinese",
            ["themeLabel"] = "Theme",
            ["themeSystem"] = "Follow System",
            ["themeDark"] = "Dark",
            ["themeLight"] = "Light",
            ["aboutLabel"] = "About",
            ["aboutText"] = "NetFence — Manage Windows Firewall outbound and inbound block rules for selected applications.",
            ["openLogLabel"] = "Open Log",
        },
        ["zh-CN"] = new()
        {
            ["navScanBlock"] = "扫描封禁",
            ["navNetworkMonitor"] = "联网监控",
            ["navServicesTasks"] = "服务/计划任务",
            ["navRuleProfiles"] = "规则档案",
            ["navSettings"] = "设置",
            ["ready"] = "就绪",
            ["adminEnabled"] = "管理员权限：可以修改防火墙规则",
            ["targetPath"] = "目标路径",
            ["selectExe"] = "选择 EXE",
            ["selectFolder"] = "选择文件夹",
            ["ruleName"] = "规则名称",
            ["includeChildProcesses"] = "包含当前运行中的子进程可执行文件",
            ["scanRelated"] = "扫描关联程序",
            ["blockNetwork"] = "禁止联网",
            ["unblock"] = "解除禁止",
            ["unblockSelected"] = "解除选中",
            ["unblockAll"] = "解除全部",
            ["exportSnapshot"] = "导出快照",
            ["openLog"] = "打开日志",
            ["refresh"] = "刷新",
            ["relatedCandidates"] = "关联候选程序",
            ["firewallRules"] = "防火墙规则",
            ["columnBlock"] = "阻断",
            ["columnReason"] = "原因",
            ["columnPid"] = "PID",
            ["columnProcess"] = "进程",
            ["columnProgramPath"] = "程序路径",
            ["columnRule"] = "规则",
            ["columnDirection"] = "方向",
            ["columnEnabled"] = "启用",
            ["columnAction"] = "动作",
            ["scanRunning"] = "正在扫描关联可执行文件...",
            ["blockRunning"] = "正在写入持久化防火墙阻断规则...",
            ["unblockRunning"] = "正在删除 NetFence 防火墙规则...",
            ["unblockAllRunning"] = "正在删除全部 NetFence 防火墙规则...",
            ["exportRunning"] = "正在导出当前规则和候选列表...",
            ["refreshRunning"] = "正在刷新防火墙规则状态...",
            ["loadedRules"] = "已加载 {0} 条 NetFence 防火墙规则。",
            ["readStatusFailed"] = "读取防火墙状态失败：{0}",
            ["foundCandidates"] = "发现 {0} 个关联候选可执行文件。阻断前请检查勾选项。",
            ["scanFailed"] = "扫描失败：{0}",
            ["selectTargetFirst"] = "请先选择一个 .exe 文件或文件夹。",
            ["pathDoesNotExist"] = "路径不存在：{0}",
            ["folderDialogDescription"] = "选择包含可执行程序的文件夹",
            ["blockedTargets"] = "已禁止 ''{0}'' 联网，覆盖 {1} 个可执行文件。",
            ["blockFailed"] = "禁止联网失败：{0}",
            ["unblockedRules"] = "已解除 ''{0}''，删除 {1} 条规则。",
            ["unblockFailed"] = "解除失败：{0}",
            ["selectRuleFirst"] = "请先选择一条或多条防火墙规则。",
            ["unblockSelectedRunning"] = "正在删除选中可执行文件的防火墙规则...",
            ["unblockedSelectedRules"] = "已删除 {0} 条规则，涉及 {1} 个选中可执行文件。",
            ["unblockSelectedFailed"] = "解除选中失败：{0}",
            ["unblockSelectedConfirmTitle"] = "解除选中可执行文件",
            ["unblockSelectedConfirmMessage"] = "这会删除 {0} 个选中可执行文件的全部 NetFence 规则。是否继续？",
            ["unblockedAllRules"] = "已删除全部 NetFence 规则：{0} 条。",
            ["unblockAllFailed"] = "解除全部失败：{0}",
            ["unblockAllConfirmTitle"] = "删除全部 NetFence 规则",
            ["unblockAllConfirmMessage"] = "这会删除所有由 NetFence 管理的防火墙规则。是否继续？",
            ["exportComplete"] = "已导出 {0} 行到 {1}",
            ["exportFailed"] = "导出失败：{0}",
            ["openLogFailed"] = "打开日志失败：{0}",
            ["languageSwitchFailed"] = "语言切换失败：{0}",
            ["firstRunTitle"] = "NetFence 安全提示",
            ["firstRunMessage"] = "NetFence 会修改 Windows Defender 防火墙规则，禁止/解除操作需要管理员权限。规则在关闭工具后仍会保留。需要恢复联网时请使用“解除禁止”或“解除全部”。是否继续？",
            ["networkMonitorComing"] = "联网监控功能将在后续版本中推出。",
            ["servicesTasksComing"] = "服务与计划任务扫描将在后续版本中推出。",
            ["ruleProfilesComing"] = "规则档案管理将在后续版本中推出。",
            ["languageLabel"] = "语言",
            ["languageEnglish"] = "英文",
            ["languageChinese"] = "中文",
            ["themeLabel"] = "主题",
            ["themeSystem"] = "跟随系统",
            ["themeDark"] = "深色",
            ["themeLight"] = "浅色",
            ["aboutLabel"] = "关于",
            ["aboutText"] = "NetFence — 管理 Windows Defender 防火墙出入站规则，控制指定软件的网络访问。",
            ["openLogLabel"] = "打开日志",
        }
    };
}
```

Note: The zh-CN values use Unicode escapes above for plan readability. The actual file should contain the raw Chinese characters copied from the original MainWindow.xaml.cs.

- [ ] **Step 2: Verify build**

Run: `dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug`

- [ ] **Step 3: Commit**

```bash
git add dotnet/NetFence.App/Services/LocaleService.cs
git commit -m "feat: extract LocaleService with all translations and LanguageChanged event"
```

---

### Task 4: Theme ResourceDictionaries

**Files:**
- Create: `dotnet/NetFence.App/Themes/DarkTheme.xaml`
- Create: `dotnet/NetFence.App/Themes/LightTheme.xaml`

- [ ] **Step 1: Create DarkTheme.xaml**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="WindowBackground" Color="#1E1E2E"/>
    <SolidColorBrush x:Key="SidebarBackground" Color="#252536"/>
    <SolidColorBrush x:Key="SidebarItemHover" Color="#333350"/>
    <SolidColorBrush x:Key="SidebarItemSelected" Color="#1E5FA8"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#2A2A3C"/>
    <SolidColorBrush x:Key="PanelBackground" Color="#333350"/>
    <SolidColorBrush x:Key="InputBackground" Color="#1E1E2E"/>
    <SolidColorBrush x:Key="GridRowAlt" Color="#2D2D40"/>
    <SolidColorBrush x:Key="GridRowHover" Color="#3A3A50"/>
    <SolidColorBrush x:Key="PrimaryText" Color="#E0E0E0"/>
    <SolidColorBrush x:Key="SecondaryText" Color="#AAAAAA"/>
    <SolidColorBrush x:Key="MutedText" Color="#888888"/>
    <SolidColorBrush x:Key="AccentBlue" Color="#7EB4FF"/>
    <SolidColorBrush x:Key="AccentGreen" Color="#27AE60"/>
    <SolidColorBrush x:Key="AccentRed" Color="#E74C3C"/>
    <SolidColorBrush x:Key="WarningOrange" Color="#CC9933"/>
    <SolidColorBrush x:Key="BorderColor" Color="#4A4A60"/>
    <SolidColorBrush x:Key="SeparatorColor" Color="#3A3A50"/>
    <SolidColorBrush x:Key="StatusBarBackground" Color="#1A1A28"/>
    <SolidColorBrush x:Key="StatusSuccessText" Color="#27AE60"/>
    <SolidColorBrush x:Key="TabSelectedUnderline" Color="#1E5FA8"/>
    <SolidColorBrush x:Key="TabInactiveText" Color="#888888"/>
    <SolidColorBrush x:Key="ProgressBarForeground" Color="#1E5FA8"/>
    <SolidColorBrush x:Key="ButtonPrimaryBackground" Color="#1E5FA8"/>
    <SolidColorBrush x:Key="ButtonPrimaryForeground" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="ButtonSecondaryBackground" Color="#3A3A50"/>
    <SolidColorBrush x:Key="ButtonSecondaryBorder" Color="#555555"/>
    <SolidColorBrush x:Key="ButtonSecondaryForeground" Color="#CCCCCC"/>
    <SolidColorBrush x:Key="ButtonDangerBackground" Color="#C0392B"/>
    <SolidColorBrush x:Key="ButtonDangerForeground" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="ButtonSuccessBackground" Color="#27AE60"/>
    <SolidColorBrush x:Key="ButtonSuccessForeground" Color="#FFFFFF"/>
</ResourceDictionary>
```

- [ ] **Step 2: Create LightTheme.xaml**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="WindowBackground" Color="#F5F5F5"/>
    <SolidColorBrush x:Key="SidebarBackground" Color="#FAFAFA"/>
    <SolidColorBrush x:Key="SidebarItemHover" Color="#E8E8E8"/>
    <SolidColorBrush x:Key="SidebarItemSelected" Color="#1E5FA8"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="PanelBackground" Color="#F8F8F8"/>
    <SolidColorBrush x:Key="InputBackground" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="GridRowAlt" Color="#F5F5F5"/>
    <SolidColorBrush x:Key="GridRowHover" Color="#E8F0FE"/>
    <SolidColorBrush x:Key="PrimaryText" Color="#1A1A1A"/>
    <SolidColorBrush x:Key="SecondaryText" Color="#555555"/>
    <SolidColorBrush x:Key="MutedText" Color="#888888"/>
    <SolidColorBrush x:Key="AccentBlue" Color="#1E5FA8"/>
    <SolidColorBrush x:Key="AccentGreen" Color="#1E8449"/>
    <SolidColorBrush x:Key="AccentRed" Color="#C0392B"/>
    <SolidColorBrush x:Key="WarningOrange" Color="#B8860B"/>
    <SolidColorBrush x:Key="BorderColor" Color="#D0D0D0"/>
    <SolidColorBrush x:Key="SeparatorColor" Color="#E0E0E0"/>
    <SolidColorBrush x:Key="StatusBarBackground" Color="#ECECEC"/>
    <SolidColorBrush x:Key="StatusSuccessText" Color="#1E8449"/>
    <SolidColorBrush x:Key="TabSelectedUnderline" Color="#1E5FA8"/>
    <SolidColorBrush x:Key="TabInactiveText" Color="#888888"/>
    <SolidColorBrush x:Key="ProgressBarForeground" Color="#1E5FA8"/>
    <SolidColorBrush x:Key="ButtonPrimaryBackground" Color="#1E5FA8"/>
    <SolidColorBrush x:Key="ButtonPrimaryForeground" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="ButtonSecondaryBackground" Color="#E8E8E8"/>
    <SolidColorBrush x:Key="ButtonSecondaryBorder" Color="#C0C0C0"/>
    <SolidColorBrush x:Key="ButtonSecondaryForeground" Color="#333333"/>
    <SolidColorBrush x:Key="ButtonDangerBackground" Color="#C0392B"/>
    <SolidColorBrush x:Key="ButtonDangerForeground" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="ButtonSuccessBackground" Color="#27AE60"/>
    <SolidColorBrush x:Key="ButtonSuccessForeground" Color="#FFFFFF"/>
</ResourceDictionary>
```

- [ ] **Step 3: Verify build**

Run: `dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug`

- [ ] **Step 4: Commit**

```bash
git add dotnet/NetFence.App/Themes/
git commit -m "feat: add DarkTheme and LightTheme ResourceDictionaries"
```

---

### Task 5: Placeholder pages

**Files:**
- Create: `dotnet/NetFence.App/Pages/NetworkMonitorPage.xaml` + `.cs`
- Create: `dotnet/NetFence.App/Pages/ServicesTasksPage.xaml` + `.cs`
- Create: `dotnet/NetFence.App/Pages/RuleProfilesPage.xaml` + `.cs`

- [ ] **Step 1: Create NetworkMonitorPage**

`NetworkMonitorPage.xaml`:
```xml
<UserControl x:Class="NetFence.App.Pages.NetworkMonitorPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Background="{DynamicResource ContentBackground}">
        <TextBlock x:Name="PlaceholderText" FontSize="16"
                   Foreground="{DynamicResource MutedText}"
                   HorizontalAlignment="Center" VerticalAlignment="Center"/>
    </Grid>
</UserControl>
```

`NetworkMonitorPage.xaml.cs`:
```csharp
using System.Windows.Controls;
using NetFence.App.Services;

namespace NetFence.App.Pages;

public partial class NetworkMonitorPage : UserControl
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
```

- [ ] **Step 2: Create ServicesTasksPage**

`ServicesTasksPage.xaml`:
```xml
<UserControl x:Class="NetFence.App.Pages.ServicesTasksPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Background="{DynamicResource ContentBackground}">
        <TextBlock x:Name="PlaceholderText" FontSize="16"
                   Foreground="{DynamicResource MutedText}"
                   HorizontalAlignment="Center" VerticalAlignment="Center"/>
    </Grid>
</UserControl>
```

`ServicesTasksPage.xaml.cs`:
```csharp
using System.Windows.Controls;
using NetFence.App.Services;

namespace NetFence.App.Pages;

public partial class ServicesTasksPage : UserControl
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
```

- [ ] **Step 3: Create RuleProfilesPage**

`RuleProfilesPage.xaml`:
```xml
<UserControl x:Class="NetFence.App.Pages.RuleProfilesPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Background="{DynamicResource ContentBackground}">
        <TextBlock x:Name="PlaceholderText" FontSize="16"
                   Foreground="{DynamicResource MutedText}"
                   HorizontalAlignment="Center" VerticalAlignment="Center"/>
    </Grid>
</UserControl>
```

`RuleProfilesPage.xaml.cs`:
```csharp
using System.Windows.Controls;
using NetFence.App.Services;

namespace NetFence.App.Pages;

public partial class RuleProfilesPage : UserControl
{
    public RuleProfilesPage()
    {
        InitializeComponent();
        ApplyLocale();
        LocaleService.LanguageChanged += ApplyLocale();
    }

    private void ApplyLocale()
    {
        Dispatcher.Invoke(() =>
            PlaceholderText.Text = LocaleService.T("ruleProfilesComing"));
    }
}
```

Wait, there's a bug in the above: `ApplyLocale()` is a void method, you can't use it with `+=`. Fix in actual implementation:

```csharp
public RuleProfilesPage()
{
    InitializeComponent();
    ApplyLocale();
    LocaleService.LanguageChanged += ApplyLocale;
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug`

- [ ] **Step 5: Commit**

```bash
git add dotnet/NetFence.App/Pages/NetworkMonitorPage.xaml dotnet/NetFence.App/Pages/NetworkMonitorPage.xaml.cs dotnet/NetFence.App/Pages/ServicesTasksPage.xaml dotnet/NetFence.App/Pages/ServicesTasksPage.xaml.cs dotnet/NetFence.App/Pages/RuleProfilesPage.xaml dotnet/NetFence.App/Pages/RuleProfilesPage.xaml.cs
git commit -m "feat: add placeholder pages for NetworkMonitor, ServicesTasks, RuleProfiles"
```

---

### Task 6: ScanBlockPage — extract scan/block logic into UserControl

**Files:**
- Create: `dotnet/NetFence.App/Pages/ScanBlockPage.xaml` + `.cs`

- [ ] **Step 1: Create ScanBlockPage.xaml**

Move the existing scan/block UI from MainWindow.xaml into this UserControl. Use DynamicResource for all colors. The DataGridColumnHeaderStyle StaticResource will reference the style defined in App.xaml (Task 8).

```xml
<UserControl x:Class="NetFence.App.Pages.ScanBlockPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="16" Background="{DynamicResource ContentBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Target Path row -->
        <Grid Grid.Row="0" Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="90"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBlock x:Name="TargetPathLabel" VerticalAlignment="Center"
                       Foreground="{DynamicResource PrimaryText}"/>
            <TextBox x:Name="PathBox" Grid.Column="1" Height="32"
                     VerticalContentAlignment="Center"
                     Background="{DynamicResource InputBackground}"
                     Foreground="{DynamicResource PrimaryText}"
                     BorderBrush="{DynamicResource BorderColor}"/>
            <StackPanel Grid.Column="2" Orientation="Horizontal">
                <Button x:Name="SelectExeButton" MinWidth="100" Height="32"
                        Margin="4,0" Padding="12,0" Click="SelectExeButton_Click"
                        Background="{DynamicResource ButtonPrimaryBackground}"
                        Foreground="{DynamicResource ButtonPrimaryForeground}"
                        BorderThickness="0">
                    <Button.Resources>
                        <Style TargetType="Border">
                            <Setter Property="CornerRadius" Value="4"/>
                        </Style>
                    </Button.Resources>
                </Button>
                <Button x:Name="SelectFolderButton" MinWidth="110" Height="32"
                        Margin="4,0" Padding="12,0" Click="SelectFolderButton_Click"
                        Background="{DynamicResource ButtonSecondaryBackground}"
                        Foreground="{DynamicResource ButtonSecondaryForeground}"
                        BorderBrush="{DynamicResource ButtonSecondaryBorder}"/>
            </StackPanel>
        </Grid>

        <!-- Rule Name + Checkbox row -->
        <Grid Grid.Row="1" Margin="0,0,0,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="90"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock x:Name="RuleNameLabel" VerticalAlignment="Center"
                       Foreground="{DynamicResource PrimaryText}"/>
            <StackPanel Grid.Column="1">
                <TextBox x:Name="NameBox" Height="32" VerticalContentAlignment="Center"
                         Background="{DynamicResource InputBackground}"
                         Foreground="{DynamicResource PrimaryText}"
                         BorderBrush="{DynamicResource BorderColor}"/>
                <CheckBox x:Name="IncludeLinkedCheck" Margin="0,6,0,0"
                          Foreground="{DynamicResource SecondaryText}" IsChecked="True"/>
            </StackPanel>
        </Grid>

        <!-- Action Buttons row -->
        <WrapPanel Grid.Row="2" Margin="0,4,0,10">
            <Button x:Name="ScanButton" MinWidth="120" Height="32" Margin="2"
                    Padding="12,0" Click="ScanButton_Click"
                    Background="{DynamicResource ButtonPrimaryBackground}"
                    Foreground="{DynamicResource ButtonPrimaryForeground}" BorderThickness="0"/>
            <Button x:Name="BlockButton" MinWidth="120" Height="32" Margin="2"
                    Padding="12,0" Click="BlockButton_Click"
                    Background="{DynamicResource ButtonDangerBackground}"
                    Foreground="{DynamicResource ButtonDangerForeground}" BorderThickness="0"/>
            <Button x:Name="UnblockButton" MinWidth="110" Height="32" Margin="2"
                    Padding="12,0" Click="UnblockButton_Click"
                    Background="{DynamicResource ButtonSuccessBackground}"
                    Foreground="{DynamicResource ButtonSuccessForeground}" BorderThickness="0"/>
            <Separator Width="8" Visibility="Hidden"/>
            <Button x:Name="UnblockSelectedButton" MinWidth="110" Height="32" Margin="2"
                    Padding="12,0" Click="UnblockSelectedButton_Click"
                    Background="{DynamicResource ButtonSecondaryBackground}"
                    Foreground="{DynamicResource ButtonSecondaryForeground}"
                    BorderBrush="{DynamicResource ButtonSecondaryBorder}"/>
            <Button x:Name="UnblockAllButton" MinWidth="100" Height="32" Margin="2"
                    Padding="12,0" Click="UnblockAllButton_Click"
                    Background="{DynamicResource ButtonSecondaryBackground}"
                    Foreground="{DynamicResource ButtonSecondaryForeground}"
                    BorderBrush="{DynamicResource ButtonSecondaryBorder}"/>
            <Button x:Name="ExportButton" MinWidth="110" Height="32" Margin="2"
                    Padding="12,0" Click="ExportButton_Click"
                    Background="{DynamicResource ButtonSecondaryBackground}"
                    Foreground="{DynamicResource ButtonSecondaryForeground}"
                    BorderBrush="{DynamicResource ButtonSecondaryBorder}"/>
            <Button x:Name="RefreshButton" MinWidth="90" Height="32" Margin="2"
                    Padding="12,0" Click="RefreshButton_Click"
                    Background="{DynamicResource ButtonSecondaryBackground}"
                    Foreground="{DynamicResource ButtonSecondaryForeground}"
                    BorderBrush="{DynamicResource ButtonSecondaryBorder}"/>
        </WrapPanel>

        <!-- Results area: TabControl with two DataGrids -->
        <TabControl x:Name="Tabs" Grid.Row="3"
                    Background="{DynamicResource ContentBackground}"
                    BorderBrush="{DynamicResource BorderColor}">
            <TabItem x:Name="CandidatesTab">
                <TabItem.Header>
                    <TextBlock x:Name="CandidatesTabHeader"/>
                </TabItem.Header>
                <DataGrid x:Name="CandidatesGrid" AutoGenerateColumns="False"
                          CanUserAddRows="False"
                          Background="{DynamicResource ContentBackground}"
                          Foreground="{DynamicResource PrimaryText}"
                          RowBackground="{DynamicResource ContentBackground}"
                          AlternatingRowBackground="{DynamicResource GridRowAlt}"
                          BorderBrush="{DynamicResource BorderColor}"
                          ColumnHeaderStyle="{StaticResource DataGridColumnHeaderStyle}">
                    <DataGrid.Columns>
                        <DataGridCheckBoxColumn x:Name="CandidateBlockColumn"
                                                Binding="{Binding Selected}" Width="60"/>
                        <DataGridTextColumn x:Name="CandidateProgramColumn"
                                            Binding="{Binding Program}" IsReadOnly="True" Width="3*"/>
                        <DataGridTextColumn x:Name="CandidateReasonColumn"
                                            Binding="{Binding Reason}" IsReadOnly="True" Width="2*"/>
                        <DataGridTextColumn x:Name="CandidatePidColumn"
                                            Binding="{Binding ProcessId}" IsReadOnly="True" Width="60"/>
                        <DataGridTextColumn x:Name="CandidateProcessColumn"
                                            Binding="{Binding ProcessName}" IsReadOnly="True" Width="120"/>
                    </DataGrid.Columns>
                </DataGrid>
            </TabItem>
            <TabItem x:Name="RulesTab">
                <TabItem.Header>
                    <TextBlock x:Name="RulesTabHeader"/>
                </TabItem.Header>
                <DataGrid x:Name="RulesGrid" AutoGenerateColumns="False"
                          CanUserAddRows="False" IsReadOnly="True"
                          SelectionMode="Extended" SelectionUnit="FullRow"
                          Background="{DynamicResource ContentBackground}"
                          Foreground="{DynamicResource PrimaryText}"
                          RowBackground="{DynamicResource ContentBackground}"
                          AlternatingRowBackground="{DynamicResource GridRowAlt}"
                          BorderBrush="{DynamicResource BorderColor}"
                          ColumnHeaderStyle="{StaticResource DataGridColumnHeaderStyle}">
                    <DataGrid.Columns>
                        <DataGridTextColumn x:Name="RuleProfileColumn"
                                            Binding="{Binding ProfileName}" Width="140"/>
                        <DataGridTextColumn x:Name="RuleDirectionColumn"
                                            Binding="{Binding Direction}" Width="80"/>
                        <DataGridTextColumn x:Name="RuleEnabledColumn"
                                            Binding="{Binding Enabled}" Width="70"/>
                        <DataGridTextColumn x:Name="RuleActionColumn"
                                            Binding="{Binding Action}" Width="70"/>
                        <DataGridTextColumn x:Name="RuleProgramColumn"
                                            Binding="{Binding Program}" Width="*"/>
                    </DataGrid.Columns>
                </DataGrid>
            </TabItem>
        </TabControl>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create ScanBlockPage.xaml.cs**

Port the full scan/block logic from the current `MainWindow.xaml.cs`. The code-behind is nearly identical to the original, with three changes:
1. Use `LocaleService.T()` instead of the `T()` method
2. Use `Window.GetWindow(this)` for dialog owner instead of `this`
3. Remove `Reflection`-based RefreshRulesAsync call — expose it as `public` instead so MainWindow can call it

```csharp
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NetFence.Core;
using NetFence.App.Services;
using Forms = System.Windows.Forms;

namespace NetFence.App.Pages;

public partial class ScanBlockPage : UserControl
{
    private readonly ObservableCollection<CandidateRow> _candidates = [];
    private readonly ObservableCollection<FirewallRuleInfo> _rules = [];
    private bool _isUpdatingRuleName;
    private bool _ruleNameEditedByUser;
    private string? _lastSuggestedRuleName;

    public ScanBlockPage()
    {
        InitializeComponent();
        CandidatesGrid.ItemsSource = _candidates;
        RulesGrid.ItemsSource = _rules;
        NameBox.TextChanged += (_, _) =>
        {
            if (!_isUpdatingRuleName) _ruleNameEditedByUser = true;
        };
        ApplyLocale();
        LocaleService.LanguageChanged += ApplyLocale;
    }

    public async Task RefreshRulesAsync()
    {
        try
        {
            await RunBusyAsync(() => LocaleService.T("refreshRunning"), async () =>
            {
                var rules = await Task.Run(FirewallService.GetStatus);
                ReplaceRules(rules);
            });
        }
        catch { }
    }

    public void OpenLog()
    {
        try
        {
            if (!File.Exists(OperationLog.DefaultPath))
                OperationLog.Write(OperationLog.DefaultPath, "OpenLog", "Created log file.", []);
            Process.Start(new ProcessStartInfo(OperationLog.DefaultPath) { UseShellExecute = true });
        }
        catch { }
    }

    private void ApplyLocale()
    {
        Dispatcher.Invoke(() =>
        {
            TargetPathLabel.Text = LocaleService.T("targetPath");
            SelectExeButton.Content = LocaleService.T("selectExe");
            SelectFolderButton.Content = LocaleService.T("selectFolder");
            RuleNameLabel.Text = LocaleService.T("ruleName");
            IncludeLinkedCheck.Content = LocaleService.T("includeChildProcesses");
            ScanButton.Content = LocaleService.T("scanRelated");
            BlockButton.Content = LocaleService.T("blockNetwork");
            UnblockButton.Content = LocaleService.T("unblock");
            UnblockSelectedButton.Content = LocaleService.T("unblockSelected");
            UnblockAllButton.Content = LocaleService.T("unblockAll");
            ExportButton.Content = LocaleService.T("exportSnapshot");
            RefreshButton.Content = LocaleService.T("refresh");
            CandidatesTabHeader.Text = LocaleService.T("relatedCandidates");
            RulesTabHeader.Text = LocaleService.T("firewallRules");
            CandidateBlockColumn.Header = LocaleService.T("columnBlock");
            CandidateReasonColumn.Header = LocaleService.T("columnReason");
            CandidatePidColumn.Header = LocaleService.T("columnPid");
            CandidateProcessColumn.Header = LocaleService.T("columnProcess");
            CandidateProgramColumn.Header = LocaleService.T("columnProgramPath");
            RuleProfileColumn.Header = LocaleService.T("columnRule");
            RuleDirectionColumn.Header = LocaleService.T("columnDirection");
            RuleEnabledColumn.Header = LocaleService.T("columnEnabled");
            RuleActionColumn.Header = LocaleService.T("columnAction");
            RuleProgramColumn.Header = LocaleService.T("columnProgramPath");
        });
    }

    private async void SelectExeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            PathBox.Text = dialog.FileName;
            SuggestRuleName(dialog.FileName);
            await ScanRelatedAsync();
        }
    }

    private async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = LocaleService.T("folderDialogDescription")
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            PathBox.Text = dialog.SelectedPath;
            SuggestRuleName(dialog.SelectedPath);
            await ScanRelatedAsync();
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e) => await ScanRelatedAsync();

    private async void BlockButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitCandidateEdits();
            var path = GetSelectedPath();
            var name = NameBox.Text.Trim();
            var includeLinked = IncludeLinkedCheck.IsChecked == true;
            var selectedCandidates = _candidates.Where(item => item.Selected)
                .Select(item => item.Program).ToArray();

            await RunBusyAsync(() => LocaleService.T("blockRunning"), async () =>
            {
                var result = await Task.Run(() =>
                    FirewallService.Block(path, name, includeLinked, selectedCandidates));
                var rules = await Task.Run(FirewallService.GetStatus);
                ReplaceRules(rules);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocaleService.T("blockFailed", ex.Message),
                "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void UnblockButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = GetSelectedPath();
            var name = NameBox.Text.Trim();
            await RunBusyAsync(() => LocaleService.T("unblockRunning"), async () =>
            {
                var result = await Task.Run(() => FirewallService.Unblock(path, name));
                var rules = await Task.Run(FirewallService.GetStatus);
                ReplaceRules(rules);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocaleService.T("unblockFailed", ex.Message),
                "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void UnblockSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedRules = RulesGrid.SelectedItems.OfType<FirewallRuleInfo>().ToArray();
            var targets = FirewallService.GetSelectedProgramUnblockTargets(selectedRules);
            if (targets.Count == 0)
                throw new InvalidOperationException(LocaleService.T("selectRuleFirst"));

            var confirm = MessageBox.Show(
                LocaleService.T("unblockSelectedConfirmMessage", targets.Count),
                LocaleService.T("unblockSelectedConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            await RunBusyAsync(() => LocaleService.T("unblockSelectedRunning"), async () =>
            {
                var removed = await Task.Run(() =>
                    FirewallService.UnblockSelectedPrograms(selectedRules));
                var rules = await Task.Run(FirewallService.GetStatus);
                ReplaceRules(rules);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocaleService.T("unblockSelectedFailed", ex.Message),
                "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void UnblockAllButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            LocaleService.T("unblockAllConfirmMessage"),
            LocaleService.T("unblockAllConfirmTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        await RunBusyAsync(() => LocaleService.T("unblockAllRunning"), async () =>
        {
            var removed = await Task.Run(FirewallService.UnblockAll);
            var rules = await Task.Run(FirewallService.GetStatus);
            ReplaceRules(rules);
        });
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"NetFence-snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var rulesArr = _rules.ToArray();
        CommitCandidateEdits();
        var candidatesArr = _candidates.Select(item =>
            new RelatedCandidate(item.Selected, item.Program, item.Reason,
                item.ProcessId, item.ProcessName)).ToArray();

        await RunBusyAsync(() => LocaleService.T("exportRunning"), async () =>
        {
            var result = await Task.Run(() =>
                SnapshotExporter.Export(dialog.FileName, rulesArr, candidatesArr));
            OperationLog.Write(OperationLog.DefaultPath, "Export",
                $"Exported {result.RowCount} row(s) to '{result.Path}'.", [result.Path]);
        });
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshRulesAsync();

    private async Task ScanRelatedAsync()
    {
        try
        {
            var path = GetSelectedPath();
            await RunBusyAsync(() => LocaleService.T("scanRunning"), async () =>
            {
                var rows = await Task.Run(LiveSystemInfo.GetProcessRows);
                var networkIds = await Task.Run(LiveSystemInfo.GetNetworkProcessIds);
                var candidates = await Task.Run(() =>
                    RelatedProcessScanner.GetRelatedCandidates(path, rows, networkIds));
                _candidates.Clear();
                foreach (var c in candidates) _candidates.Add(new CandidateRow(c));
                Tabs.SelectedItem = CandidatesTab;
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocaleService.T("scanFailed", ex.Message),
                "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RunBusyAsync(Func<string> statusFactory, Func<Task> action)
    {
        SetBusy(true);
        try { await action(); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
        foreach (var control in new Control[] {
            SelectExeButton, SelectFolderButton, PathBox, NameBox, IncludeLinkedCheck,
            ScanButton, BlockButton, UnblockButton, UnblockSelectedButton,
            UnblockAllButton, ExportButton, RefreshButton })
        {
            control.IsEnabled = !busy;
        }
    }

    private void CommitCandidateEdits()
    {
        CandidatesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        CandidatesGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void SuggestRuleName(string path)
    {
        var suggestion = NetFenceRules.GetProfileName(path);
        if (_ruleNameEditedByUser &&
            !string.IsNullOrWhiteSpace(NameBox.Text) &&
            !string.Equals(NameBox.Text.Trim(), _lastSuggestedRuleName, StringComparison.Ordinal))
        {
            _lastSuggestedRuleName = suggestion;
            return;
        }
        try
        {
            _isUpdatingRuleName = true;
            NameBox.Text = suggestion;
            _lastSuggestedRuleName = suggestion;
            _ruleNameEditedByUser = false;
        }
        finally { _isUpdatingRuleName = false; }
    }

    private string GetSelectedPath()
    {
        var path = PathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(LocaleService.T("selectTargetFirst"));
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new InvalidOperationException(LocaleService.T("pathDoesNotExist", path));
        return path;
    }

    private void ReplaceRules(IEnumerable<FirewallRuleInfo> rules)
    {
        _rules.Clear();
        foreach (var rule in rules) _rules.Add(rule);
    }

    public sealed class CandidateRow(RelatedCandidate candidate)
    {
        public bool Selected { get; set; } = candidate.Selected;
        public string Program { get; } = candidate.Program;
        public string Reason { get; } = candidate.Reason;
        public int ProcessId { get; } = candidate.ProcessId;
        public string ProcessName { get; } = candidate.ProcessName;
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug`
Expected: Build fails — old MainWindow.xaml.cs still references removed x:Name elements. Accept this; MainWindow rewrite in Task 9 resolves it.

---

### Task 7: SettingsPage — language, theme, about

**Files:**
- Create: `dotnet/NetFence.App/Pages/SettingsPage.xaml` + `.cs`

- [ ] **Step 1: Create SettingsPage.xaml**

```xml
<UserControl x:Class="NetFence.App.Pages.SettingsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="24" MaxWidth="480" Background="{DynamicResource ContentBackground}">
        <StackPanel>
            <!-- Language -->
            <Grid Margin="0,0,0,14">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="80"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock x:Name="LanguageLabel" VerticalAlignment="Center"
                           Foreground="{DynamicResource PrimaryText}"/>
                <ComboBox x:Name="LanguageBox" Grid.Column="1" Width="160"
                          SelectionChanged="LanguageBox_SelectionChanged"
                          Background="{DynamicResource InputBackground}"
                          Foreground="{DynamicResource PrimaryText}"
                          BorderBrush="{DynamicResource BorderColor}">
                    <ComboBoxItem x:Name="LangEnItem" Tag="en-US"/>
                    <ComboBoxItem x:Name="LangZhItem" Tag="zh-CN"/>
                </ComboBox>
            </Grid>

            <!-- Theme -->
            <Grid Margin="0,0,0,14">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="80"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock x:Name="ThemeLabel" VerticalAlignment="Center"
                           Foreground="{DynamicResource PrimaryText}"/>
                <ComboBox x:Name="ThemeBox" Grid.Column="1" Width="160"
                          SelectionChanged="ThemeBox_SelectionChanged"
                          Background="{DynamicResource InputBackground}"
                          Foreground="{DynamicResource PrimaryText}"
                          BorderBrush="{DynamicResource BorderColor}">
                    <ComboBoxItem x:Name="ThemeSystemItem" Tag="system"/>
                    <ComboBoxItem x:Name="ThemeDarkItem" Tag="dark"/>
                    <ComboBoxItem x:Name="ThemeLightItem" Tag="light"/>
                </ComboBox>
            </Grid>

            <!-- Open Log -->
            <Button x:Name="OpenLogButton" Width="160" Height="32"
                    HorizontalAlignment="Left" Margin="0,0,0,20"
                    Click="OpenLogButton_Click"
                    Background="{DynamicResource ButtonSecondaryBackground}"
                    Foreground="{DynamicResource ButtonSecondaryForeground}"
                    BorderBrush="{DynamicResource ButtonSecondaryBorder}"/>

            <!-- Separator -->
            <Border Height="1" Margin="0,0,0,16"
                    Background="{DynamicResource SeparatorColor}"/>

            <!-- About -->
            <TextBlock x:Name="AboutLabel" FontWeight="SemiBold" Margin="0,0,0,6"
                       Foreground="{DynamicResource PrimaryText}"/>
            <TextBlock x:Name="AboutText" TextWrapping="Wrap"
                       Foreground="{DynamicResource SecondaryText}"/>
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create SettingsPage.xaml.cs**

```csharp
using System.Windows;
using System.Windows.Controls;
using NetFence.App.Services;

namespace NetFence.App.Pages;

public partial class SettingsPage : UserControl
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
```

- [ ] **Step 3: Build**

Run: `dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug`

---

### Task 8: App.xaml — global styles and theme init

**Files:**
- Modify: `dotnet/NetFence.App/App.xaml`
- Modify: `dotnet/NetFence.App/App.xaml.cs`

- [ ] **Step 1: Update App.xaml**

Replace entire file:

```xml
<Application x:Class="NetFence.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <Style x:Key="DataGridColumnHeaderStyle" TargetType="DataGridColumnHeader">
                <Setter Property="Background" Value="{DynamicResource PanelBackground}"/>
                <Setter Property="Foreground" Value="{DynamicResource PrimaryText}"/>
                <Setter Property="BorderBrush" Value="{DynamicResource BorderColor}"/>
                <Setter Property="BorderThickness" Value="0,0,0,1"/>
                <Setter Property="Padding" Value="8,4"/>
                <Setter Property="FontWeight" Value="SemiBold"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="DataGridColumnHeader">
                            <Border Background="{TemplateBinding Background}"
                                    BorderBrush="{TemplateBinding BorderBrush}"
                                    BorderThickness="{TemplateBinding BorderThickness}"
                                    Padding="{TemplateBinding Padding}">
                                <ContentPresenter VerticalAlignment="Center"/>
                            </Border>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: Update App.xaml.cs**

Replace entire file:

```csharp
using System.Windows;
using NetFence.Core;
using NetFence.App.Services;

namespace NetFence.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                OperationLog.Write(OperationLog.DefaultPath, "UnhandledUiException",
                    args.Exception.Message, []);
                MessageBox.Show(args.Exception.Message, "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                try
                {
                    OperationLog.Write(OperationLog.DefaultPath, "UnhandledException", ex.Message, []);
                }
                catch { }
            }
        };

        ThemeService.Apply(SettingsService.Theme);
        base.OnStartup(e);
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add dotnet/NetFence.App/App.xaml dotnet/NetFence.App/App.xaml.cs
git commit -m "feat: wire theme init and global DataGrid header style"
```

---

### Task 9: MainWindow — rewrite shell with sidebar

**Files:**
- Modify: `dotnet/NetFence.App/MainWindow.xaml` — complete rewrite
- Modify: `dotnet/NetFence.App/MainWindow.xaml.cs` — complete rewrite

- [ ] **Step 1: Rewrite MainWindow.xaml**

```xml
<Window x:Class="NetFence.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NetFence" Height="700" Width="1000"
        MinHeight="540" MinWidth="840"
        WindowStartupLocation="CenterScreen"
        Background="{DynamicResource WindowBackground}">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <Grid Grid.Row="0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="200"/>
                <ColumnDefinition Width="1"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <!-- Sidebar -->
            <Border Grid.Column="0" Background="{DynamicResource SidebarBackground}">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>

                    <TextBlock Grid.Row="0" Text="NetFence" FontSize="20"
                               FontWeight="SemiBold" Margin="16,16,16,12"
                               Foreground="{DynamicResource AccentBlue}"/>

                    <ListBox x:Name="NavList" Grid.Row="1"
                             Background="Transparent" BorderThickness="0"
                             SelectionChanged="NavList_SelectionChanged">
                        <ListBox.Resources>
                            <Style TargetType="ListBoxItem">
                                <Setter Property="Template">
                                    <Setter.Value>
                                        <ControlTemplate TargetType="ListBoxItem">
                                            <Border x:Name="Border" Background="Transparent"
                                                    Padding="16,10" Margin="4,2"
                                                    CornerRadius="6" Cursor="Hand">
                                                <TextBlock x:Name="Text"
                                                           Text="{Binding Label}"
                                                           FontSize="13"
                                                           Foreground="{DynamicResource SecondaryText}"
                                                           VerticalAlignment="Center"/>
                                            </Border>
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter TargetName="Border" Property="Background"
                                                            Value="{DynamicResource SidebarItemHover}"/>
                                                </Trigger>
                                                <Trigger Property="IsSelected" Value="True">
                                                    <Setter TargetName="Border" Property="Background"
                                                            Value="{DynamicResource SidebarItemSelected}"/>
                                                    <Setter TargetName="Text" Property="Foreground" Value="White"/>
                                                    <Setter TargetName="Text" Property="FontWeight" Value="SemiBold"/>
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Setter.Value>
                                </Setter>
                            </Style>
                        </ListBox.Resources>
                    </ListBox>
                </Grid>
            </Border>

            <!-- Sidebar divider -->
            <Border Grid.Column="1" Background="{DynamicResource SeparatorColor}"/>

            <!-- Content area -->
            <ContentControl x:Name="ContentHost" Grid.Column="2"/>
        </Grid>

        <!-- Status Bar -->
        <Border Grid.Row="1" Background="{DynamicResource StatusBarBackground}"
                BorderBrush="{DynamicResource SeparatorColor}"
                BorderThickness="0,1,0,0" Padding="16,6">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock x:Name="AdminText" VerticalAlignment="Center"
                           Foreground="{DynamicResource AccentGreen}" FontSize="12"/>
                <StackPanel Grid.Column="1" Orientation="Horizontal"
                            HorizontalAlignment="Right">
                    <ProgressBar x:Name="Progress" Width="120" Height="4"
                                 IsIndeterminate="True" Visibility="Collapsed"
                                 Foreground="{DynamicResource ProgressBarForeground}"
                                 Margin="0,0,10,0"/>
                    <TextBlock x:Name="StatusText" FontSize="12"
                               Foreground="{DynamicResource SecondaryText}"
                               VerticalAlignment="Center"/>
                </StackPanel>
            </Grid>
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 2: Rewrite MainWindow.xaml.cs**

```csharp
using System.Windows;
using System.Windows.Controls;
using NetFence.Core;
using NetFence.App.Pages;
using NetFence.App.Services;

namespace NetFence.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = [];
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
        settingsPage.OpenLogRequested += () => Dispatcher.Invoke(() => _scanBlockPage?.OpenLog());

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
        var result = MessageBox.Show(
            LocaleService.T("firstRunMessage"),
            LocaleService.T("firstRunTitle"),
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return false;
        FirstRunState.SetAcknowledged();
        return true;
    }
}
```

- [ ] **Step 3: Build and fix compilation errors**

Run: `dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug`

Expected issues to resolve during build:
- All old x:Name references in old MainWindow are gone — the build should now succeed.
- If the build fails due to missing directories (Pages/Themes/Services), ensure the .csproj picks them up (WPF projects auto-include all .xaml/.cs in the project tree).

- [ ] **Step 4: Commit**

```bash
git add dotnet/NetFence.App/MainWindow.xaml dotnet/NetFence.App/MainWindow.xaml.cs dotnet/NetFence.App/Pages/SettingsPage.xaml dotnet/NetFence.App/Pages/SettingsPage.xaml.cs dotnet/NetFence.App/Pages/ScanBlockPage.xaml dotnet/NetFence.App/Pages/ScanBlockPage.xaml.cs
git commit -m "feat: rewrite MainWindow shell with sidebar, extract ScanBlockPage and SettingsPage"
```

---

### Task 10: Integration test — build and run

**Files:** None (verification only)

- [ ] **Step 1: Build Release**

```bash
dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Release
```
Expected: Build succeeds with 0 errors, 0 warnings.

- [ ] **Step 2: Run core tests (regression)**

```bash
dotnet run --project dotnet/NetFence.Core.Tests/NetFence.Core.Tests.csproj -c Release
```
Expected: All 14 tests pass. The core library is unchanged, so this confirms no regressions.

- [ ] **Step 3: Manual verification checklist**

Run `dotnet/NetFence.App/bin/Release/net9.0-windows/NetFence.exe` (as Administrator) and verify:

1. Window opens centered, sidebar visible with 5 nav items, Scan & Block selected by default
2. "Administrator: firewall changes enabled" shown in green status bar
3. First-run safety warning appears (if first run) — clicking OK proceeds, Cancel closes
4. Clicking each sidebar item switches the content area to the correct page
5. Language: Settings → switch to Chinese → sidebar labels + all page text updates immediately
6. Theme: Settings → switch Dark/Light/System → window appearance changes correctly
7. Scan & Block: Select EXE → dialog opens → file populates path + auto-generates rule name
8. Scan & Block: Select Folder → folder browser opens → path populates
9. Scan & Block: Click "Scan Related" → progress bar shows → candidates appear in grid
10. Scan & Block: Click "Block Network" → rules created → appear in Firewall Rules tab
11. Scan & Block: Click "Unblock" → rules removed
12. Scan & Block: "Export Snapshot" → SaveFileDialog → CSV written
13. Settings: "Open Log" → log file opens in default text editor
14. Window resize: drag to minimum (840×540) → no overlapping, scrollbars appear as needed

- [ ] **Step 5: Commit any fixes**

If manual testing reveals issues, fix and commit:
```bash
git add -A
git commit -m "fix: resolve P1 UI integration issues found during manual testing"
```
