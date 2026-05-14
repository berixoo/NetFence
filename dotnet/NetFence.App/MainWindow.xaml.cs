using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using NetFence.Core;
using Forms = System.Windows.Forms;

namespace NetFence.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<CandidateRow> _candidates = [];
    private readonly ObservableCollection<FirewallRuleInfo> _rules = [];
    private string _language;
    private string _lastStatusKey = "ready";
    private object[] _lastStatusArgs = [];
    private bool _lastStatusIsError;
    private bool _isConfiguringLanguageBox;

    public MainWindow()
    {
        InitializeComponent();
        _language = Thread.CurrentThread.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : "en-US";

        CandidatesGrid.ItemsSource = _candidates;
        RulesGrid.ItemsSource = _rules;
        ConfigureLanguageBox();
        ApplyLanguage();

        Loaded += async (_, _) =>
        {
            if (!ShowFirstRunWarning())
            {
                Close();
                return;
            }

            await RefreshRulesAsync();
        };
    }

    private void ConfigureLanguageBox()
    {
        try
        {
            _isConfiguringLanguageBox = true;
            LanguageBox.Items.Add(new LanguageOption("en-US", "English"));
            LanguageBox.Items.Add(new LanguageOption("zh-CN", "中文"));
            LanguageBox.SelectedValuePath = nameof(LanguageOption.Code);
            LanguageBox.DisplayMemberPath = nameof(LanguageOption.Label);
            LanguageBox.SelectedValue = _language;
        }
        finally
        {
            _isConfiguringLanguageBox = false;
        }
    }

    private void ApplyLanguage()
    {
        LanguageLabel.Text = T("languageLabel");
        AdminText.Text = T("adminEnabled");
        TargetPathLabel.Text = T("targetPath");
        SelectExeButton.Content = T("selectExe");
        SelectFolderButton.Content = T("selectFolder");
        RuleNameLabel.Text = T("ruleName");
        IncludeLinkedCheck.Content = T("includeChildProcesses");
        ScanButton.Content = T("scanRelated");
        BlockButton.Content = T("blockNetwork");
        UnblockButton.Content = T("unblock");
        UnblockSelectedButton.Content = T("unblockSelected");
        UnblockAllButton.Content = T("unblockAll");
        ExportButton.Content = T("exportSnapshot");
        OpenLogButton.Content = T("openLog");
        RefreshButton.Content = T("refresh");

        CandidatesTab.Header = T("relatedCandidates");
        RulesTab.Header = T("firewallRules");
        CandidateBlockColumn.Header = T("columnBlock");
        CandidateReasonColumn.Header = T("columnReason");
        CandidatePidColumn.Header = T("columnPid");
        CandidateProcessColumn.Header = T("columnProcess");
        CandidateProgramColumn.Header = T("columnProgramPath");
        RuleProfileColumn.Header = T("columnRule");
        RuleDirectionColumn.Header = T("columnDirection");
        RuleEnabledColumn.Header = T("columnEnabled");
        RuleActionColumn.Header = T("columnAction");
        RuleProgramColumn.Header = T("columnProgramPath");

        SetStatus(_lastStatusKey, _lastStatusIsError, _lastStatusArgs);
    }

    private bool ShowFirstRunWarning()
    {
        if (FirstRunState.IsAcknowledged())
        {
            return true;
        }

        var result = System.Windows.MessageBox.Show(T("firstRunMessage"), T("firstRunTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK)
        {
            return false;
        }

        FirstRunState.SetAcknowledged();
        return true;
    }

    private async void SelectExeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                NameBox.Text = NetFenceRules.GetProfileName(dialog.FileName);
            }
            await ScanRelatedAsync();
        }
    }

    private async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = T("folderDialogDescription")
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            PathBox.Text = dialog.SelectedPath;
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                NameBox.Text = NetFenceRules.GetProfileName(dialog.SelectedPath);
            }
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
            var selectedCandidates = _candidates.Where(item => item.Selected).Select(item => item.Program).ToArray();

            await RunBusyAsync("blockRunning", "blockFailed", async () =>
            {
                var result = await Task.Run(() => FirewallService.Block(path, name, includeLinked, selectedCandidates));
                var rules = await Task.Run(FirewallService.GetStatus);
                ReplaceRules(rules);
                SetStatus("blockedTargets", false, result.ProfileName, result.Targets.Count);
            });
        }
        catch (Exception ex)
        {
            SetStatus("blockFailed", true, ex.Message);
        }
    }

    private async void UnblockButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = GetSelectedPath();
            var name = NameBox.Text.Trim();
            await RunBusyAsync("unblockRunning", "unblockFailed", async () =>
            {
                var result = await Task.Run(() => FirewallService.Unblock(path, name));
                var rules = await Task.Run(FirewallService.GetStatus);
                ReplaceRules(rules);
                SetStatus("unblockedRules", false, result.ProfileName, result.RemovedRuleCount);
            });
        }
        catch (Exception ex)
        {
            SetStatus("unblockFailed", true, ex.Message);
        }
    }

    private async void UnblockSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedRules = RulesGrid.SelectedItems
                .OfType<FirewallRuleInfo>()
                .ToArray();
            var targets = FirewallService.GetSelectedProgramUnblockTargets(selectedRules);
            if (targets.Count == 0)
            {
                throw new InvalidOperationException(T("selectRuleFirst"));
            }

            var confirm = System.Windows.MessageBox.Show(
                string.Format(T("unblockSelectedConfirmMessage"), targets.Count),
                T("unblockSelectedConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            await RunBusyAsync("unblockSelectedRunning", "unblockSelectedFailed", async () =>
            {
                var removed = await Task.Run(() => FirewallService.UnblockSelectedPrograms(selectedRules));
                var rules = await Task.Run(FirewallService.GetStatus);
                ReplaceRules(rules);
                SetStatus("unblockedSelectedRules", false, removed, targets.Count);
            });
        }
        catch (Exception ex)
        {
            SetStatus("unblockSelectedFailed", true, ex.Message);
        }
    }

    private async void UnblockAllButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(T("unblockAllConfirmMessage"), T("unblockAllConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync("unblockAllRunning", "unblockAllFailed", async () =>
        {
            var removed = await Task.Run(FirewallService.UnblockAll);
            var rules = await Task.Run(FirewallService.GetStatus);
            ReplaceRules(rules);
            SetStatus("unblockedAllRules", false, removed);
        });
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"NetFence-snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var rules = _rules.ToArray();
        CommitCandidateEdits();
        var candidates = _candidates
            .Select(item => new RelatedCandidate(item.Selected, item.Program, item.Reason, item.ProcessId, item.ProcessName))
            .ToArray();

        await RunBusyAsync("exportRunning", "exportFailed", async () =>
        {
            var result = await Task.Run(() => SnapshotExporter.Export(dialog.FileName, rules, candidates));
            OperationLog.Write(OperationLog.DefaultPath, "Export", $"Exported {result.RowCount} row(s) to '{result.Path}'.", [result.Path]);
            SetStatus("exportComplete", false, result.RowCount, result.Path);
        });
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(OperationLog.DefaultPath))
            {
                OperationLog.Write(OperationLog.DefaultPath, "OpenLog", "Created log file.", []);
            }

            Process.Start(new ProcessStartInfo(OperationLog.DefaultPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("openLogFailed", true, ex.Message);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshRulesAsync();

    private void LanguageBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isConfiguringLanguageBox)
        {
            return;
        }

        try
        {
            if (LanguageBox.SelectedValue is string code && !string.Equals(_language, code, StringComparison.OrdinalIgnoreCase))
            {
                _language = code;
                ApplyLanguage();
            }
        }
        catch (Exception ex)
        {
            OperationLog.Write(OperationLog.DefaultPath, "LanguageSwitchFailed", ex.Message, []);
            SetStatus("languageSwitchFailed", true, ex.Message);
        }
    }

    private async Task ScanRelatedAsync()
    {
        try
        {
            var path = GetSelectedPath();
            await RunBusyAsync("scanRunning", "scanFailed", async () =>
            {
                var rows = await Task.Run(LiveSystemInfo.GetProcessRows);
                var networkIds = await Task.Run(LiveSystemInfo.GetNetworkProcessIds);
                var candidates = await Task.Run(() => RelatedProcessScanner.GetRelatedCandidates(path, rows, networkIds));
                _candidates.Clear();
                foreach (var candidate in candidates)
                {
                    _candidates.Add(new CandidateRow(candidate));
                }
                Tabs.SelectedItem = CandidatesTab;
                SetStatus("foundCandidates", false, _candidates.Count);
            });
        }
        catch (Exception ex)
        {
            SetStatus("scanFailed", true, ex.Message);
        }
    }

    private async Task RefreshRulesAsync()
    {
        await RunBusyAsync("refreshRunning", "readStatusFailed", async () =>
        {
            var rules = await Task.Run(FirewallService.GetStatus);
            ReplaceRules(rules);
            SetStatus("loadedRules", false, _rules.Count);
        });
    }

    private async Task RunBusyAsync(string runningKey, string failureKey, Func<Task> action)
    {
        SetBusy(true);
        SetStatus(runningKey, false);
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetStatus(failureKey, true, ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
        foreach (var control in new System.Windows.Controls.Control[]
                 {
                     SelectExeButton, SelectFolderButton, PathBox, NameBox, IncludeLinkedCheck,
                     ScanButton, BlockButton, UnblockButton, UnblockSelectedButton, UnblockAllButton, ExportButton, RefreshButton
                 })
        {
            control.IsEnabled = !busy;
        }
    }

    private void CommitCandidateEdits()
    {
        CandidatesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        CandidatesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
    }

    private string GetSelectedPath()
    {
        var path = PathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(T("selectTargetFirst"));
        }
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new InvalidOperationException(string.Format(T("pathDoesNotExist"), path));
        }
        return path;
    }

    private void ReplaceRules(IEnumerable<FirewallRuleInfo> rules)
    {
        _rules.Clear();
        foreach (var rule in rules)
        {
            _rules.Add(rule);
        }
    }

    private void SetStatus(string key, bool isError, params object[] args)
    {
        _lastStatusKey = key;
        _lastStatusArgs = args;
        _lastStatusIsError = isError;
        StatusText.Text = SafeFormat(T(key), args);
        StatusText.Foreground = isError ? System.Windows.Media.Brushes.DarkRed : System.Windows.Media.Brushes.Black;
    }

    private static string SafeFormat(string template, object[] args)
    {
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return args.Length == 0 ? template : template + " " + string.Join(" ", args);
        }
    }

    private string T(string key)
    {
        var map = Translations.TryGetValue(_language, out var selected) ? selected : Translations["en-US"];
        if (map.TryGetValue(key, out var value))
        {
            return value;
        }
        return Translations["en-US"].GetValueOrDefault(key, key);
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["en-US"] = new()
        {
            ["languageLabel"] = "Language",
            ["languageEnglish"] = "English",
            ["languageChinese"] = "Chinese",
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
            ["ready"] = "Ready",
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
            ["blockedTargets"] = "Blocked '{0}' for {1} executable file(s).",
            ["blockFailed"] = "Block failed: {0}",
            ["unblockedRules"] = "Unblocked '{0}' and removed {1} rule(s).",
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
            ["firstRunMessage"] = "NetFence changes Windows Defender Firewall rules and requires administrator permission for block/unblock actions. Rules persist after closing this tool. Use Unblock or Unblock All to restore networking. Continue?"
        },
        ["zh-CN"] = new()
        {
            ["languageLabel"] = "语言",
            ["languageEnglish"] = "英文",
            ["languageChinese"] = "中文",
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
            ["ready"] = "就绪",
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
            ["blockedTargets"] = "已禁止 '{0}' 联网，覆盖 {1} 个可执行文件。",
            ["blockFailed"] = "禁止联网失败：{0}",
            ["unblockedRules"] = "已解除 '{0}'，删除 {1} 条规则。",
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
            ["firstRunMessage"] = "NetFence 会修改 Windows Defender 防火墙规则，禁止/解除操作需要管理员权限。规则在关闭工具后仍会保留。需要恢复联网时请使用“解除禁止”或“解除全部”。是否继续？"
        }
    };

    private sealed record LanguageOption(string Code, string Label)
    {
        public string Label { get; set; } = Label;
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
