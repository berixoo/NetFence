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

public partial class ScanBlockPage : System.Windows.Controls.UserControl
{
    private readonly ObservableCollection<CandidateRow> _candidates = [];
    private readonly ObservableCollection<FirewallRuleInfo> _rules = [];
    private string _lastStatusKey = "ready";
    private object[] _lastStatusArgs = [];
    private bool _lastStatusIsError;
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
            if (!_isUpdatingRuleName)
            {
                _ruleNameEditedByUser = true;
            }
        };
        ApplyLocale();
        LocaleService.LanguageChanged += () => Dispatcher.Invoke(ApplyLocale);
    }

    public async void SelectExeButton_Click(object sender, RoutedEventArgs e)
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

    public async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
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

    public async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        try { await ScanRelatedAsync(); }
        catch { }
    }

    public async void BlockButton_Click(object sender, RoutedEventArgs e)
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

    public async void UnblockButton_Click(object sender, RoutedEventArgs e)
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

    public async void UnblockSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedRules = RulesGrid.SelectedItems
                .OfType<FirewallRuleInfo>()
                .ToArray();
            var targets = FirewallService.GetSelectedProgramUnblockTargets(selectedRules);
            if (targets.Count == 0)
            {
                throw new InvalidOperationException(LocaleService.T("selectRuleFirst"));
            }

            var confirm = System.Windows.MessageBox.Show(
                string.Format(LocaleService.T("unblockSelectedConfirmMessage"), targets.Count),
                LocaleService.T("unblockSelectedConfirmTitle"),
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

    public async void UnblockAllButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = System.Windows.MessageBox.Show(
                LocaleService.T("unblockAllConfirmMessage"),
                LocaleService.T("unblockAllConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
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
        catch (Exception ex)
        {
            SetStatus("unblockAllFailed", true, ex.Message);
        }
    }

    public async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"NetFence-snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
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

    public void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLog();
    }

    public static void OpenLog()
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
            OperationLog.Write(OperationLog.DefaultPath, "OpenLogFailed", ex.Message, []);
        }
    }

    public async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        try { await RefreshRulesAsync(); }
        catch { }
    }

    public async Task RefreshRulesAsync()
    {
        await RunBusyAsync("refreshRunning", "readStatusFailed", async () =>
        {
            var rules = await Task.Run(FirewallService.GetStatus);
            ReplaceRules(rules);
            SetStatus("loadedRules", false, _rules.Count);
        });
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
        finally
        {
            _isUpdatingRuleName = false;
        }
    }

    private string GetSelectedPath()
    {
        var path = PathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(LocaleService.T("selectTargetFirst"));
        }
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new InvalidOperationException(LocaleService.T("pathDoesNotExist", path));
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
        StatusText.Text = args.Length == 0
            ? LocaleService.T(key)
            : LocaleService.T(key, args);
        StatusText.Foreground = isError
            ? TryFindResource("AccentRed") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DarkRed
            : TryFindResource("PrimaryText") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Black;
    }

    public void ApplyLocale()
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
        OpenLogButton.Content = LocaleService.T("openLog");
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

        SetStatus(_lastStatusKey, _lastStatusIsError, _lastStatusArgs);
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
