using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using NetFence.Core;
using NetFence.App.Services;

namespace NetFence.App.Pages;

public partial class RuleProfilesPage : System.Windows.Controls.UserControl
{
    private readonly ObservableCollection<ProfileRow> _profiles = [];
    private readonly ObservableCollection<SnapshotRow> _snapshots = [];

    public RuleProfilesPage()
    {
        InitializeComponent();
        ProfilesGrid.ItemsSource = _profiles;
        SnapshotsGrid.ItemsSource = _snapshots;
        ApplyLocale();
        LocaleService.LanguageChanged += ApplyLocale;
        Loaded += (_, _) => Refresh();
    }

    private void ApplyLocale()
    {
        Dispatcher.Invoke(() =>
        {
            ProfilesLabel.Text = LocaleService.T("ruleProfilesSection");
            SaveProfileButton.Content = LocaleService.T("saveProfile");
            LoadProfileButton.Content = LocaleService.T("loadProfile");
            DeleteProfileButton.Content = LocaleService.T("deleteProfile");
            ExportProfileButton.Content = LocaleService.T("exportProfile");
            ImportProfileButton.Content = LocaleService.T("importProfile");
            SnapshotsLabel.Text = LocaleService.T("snapshotsSection");
            RollbackButton.Content = LocaleService.T("rollback");
            ProfileNameColumn.Header = LocaleService.T("columnProfileName");
            ProfilePathsColumn.Header = LocaleService.T("columnPaths");
            ProfileProgramsColumn.Header = LocaleService.T("columnPrograms");
            ProfileUpdatedColumn.Header = LocaleService.T("columnUpdated");
            SnapProfileColumn.Header = LocaleService.T("columnRule");
            SnapActionColumn.Header = LocaleService.T("columnAction");
            SnapTimestampColumn.Header = LocaleService.T("columnUpdated");
            SnapRollbackColumn.Header = LocaleService.T("columnStatus");
            ModeLabel.Text = LocaleService.T("networkMode");
            ModeBlockAll.Content = LocaleService.T("modeBlockAll");
            ModeAllowAll.Content = LocaleService.T("modeAllowAll");
            ModeLanOnly.Content = LocaleService.T("modeLanOnly");
            ModeCustom.Content = LocaleService.T("modeCustom");
            ApplyModeButton.Content = LocaleService.T("applyMode");
            AllowedIpsLabel.Text = LocaleService.T("allowedIpsLabel");
        });
    }

    private void Refresh()
    {
        _profiles.Clear();
        foreach (var p in RuleProfileStore.ListAll())
            _profiles.Add(new ProfileRow(p));

        _snapshots.Clear();
        foreach (var s in RuleSnapshotStore.ListRecent(30))
            _snapshots.Add(new SnapshotRow(s));
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var rules = FirewallService.GetStatus();
            var paths = rules.Select(r => r.Program)
                .Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => Path.GetDirectoryName(p)!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var programs = rules.Select(r => r.Program)
                .Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count == 0)
            {
                System.Windows.MessageBox.Show(LocaleService.T("noRulesToSave"), "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var name = $"Profile_{DateTime.Now:yyyyMMdd-HHmmss}";
            var mode = GetSelectedMode();
            if (mode == "custom") CommitAllowedIps();
            var allowedIps = GetAllowedIpsFromEditor();
            RuleProfileStore.Save(name, paths, programs, mode, allowedIps,
                new List<string>());
            Refresh();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoadProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ProfilesGrid.SelectedItem is not ProfileRow row)
            {
                System.Windows.MessageBox.Show(LocaleService.T("selectProfileFirst"), "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = System.Windows.MessageBox.Show(
                LocaleService.T("loadProfileConfirm", row.Name),
                "NetFence", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var profile = RuleProfileStore.GetById(row.Id);
            if (profile is null) return;

            // Select the mode in dropdown
            foreach (System.Windows.Controls.ComboBoxItem item in ModeBox.Items)
            {
                if (item.Tag is string tag && tag == profile.Mode)
                {
                    item.IsSelected = true;
                    break;
                }
            }
            AllowedIpsBox.Text = string.Join(Environment.NewLine, profile.AllowedIps);

            var firstPath = profile.Paths.FirstOrDefault();
            if (firstPath is null || (!File.Exists(firstPath) && !Directory.Exists(firstPath)))
            {
                System.Windows.MessageBox.Show(LocaleService.T("pathDoesNotExist", firstPath ?? ""), "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var targets = new List<string>();
            foreach (var p in profile.Paths)
            { try { targets.AddRange(NetFenceTargets.GetExecutableTargets(p)); } catch { } }
            targets.AddRange(profile.Programs.Where(File.Exists));

            var mode = FirewallModeService.KeyToMode(profile.Mode);
            await Task.Run(() =>
                FirewallModeService.ApplyMode(profile.Name, targets.Distinct(StringComparer.OrdinalIgnoreCase),
                    mode, profile.AllowedIps, profile.AllowedDomains));
            System.Windows.MessageBox.Show(
                LocaleService.T("profileLoaded"), "NetFence",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ProfilesGrid.SelectedItem is not ProfileRow row) return;
            RuleProfileStore.Delete(row.Id);
            Refresh();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ProfilesGrid.SelectedItem is not ProfileRow row) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"NetFence-{row.Name}-{DateTime.Now:yyyyMMdd}.json"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            var profile = RuleProfileStore.GetById(row.Id);
            if (profile is null) return;

            var exportObj = new
            {
                version = "1.0",
                exportedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                name = profile.Name,
                paths = profile.Paths,
                programs = profile.Programs,
                mode = profile.Mode,
                allowedIps = profile.AllowedIps
            };
            File.WriteAllText(dialog.FileName,
                JsonSerializer.Serialize(exportObj, new JsonSerializerOptions { WriteIndented = true }));

            OperationHistoryStore.Record("Export", profile.Name, profile.Paths.Count + profile.Programs.Count);
            System.Windows.MessageBox.Show(
                LocaleService.T("exportComplete", 1, dialog.FileName),
                "NetFence", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            var json = File.ReadAllText(dialog.FileName);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "Imported" : "Imported";
            var paths = new List<string>();
            if (root.TryGetProperty("paths", out var pa))
            {
                foreach (var p in pa.EnumerateArray())
                    paths.Add(p.GetString() ?? "");
            }
            var programs = new List<string>();
            if (root.TryGetProperty("programs", out var pr))
            {
                foreach (var p in pr.EnumerateArray())
                    programs.Add(p.GetString() ?? "");
            }
            var mode = root.TryGetProperty("mode", out var m) ? m.GetString() ?? "block_all" : "block_all";
            var allowedIps = new List<string>();
            if (root.TryGetProperty("allowedIps", out var aip))
            {
                foreach (var ip in aip.EnumerateArray())
                    allowedIps.Add(ip.GetString() ?? "");
            }

            RuleProfileStore.Save(name, paths, programs, mode, allowedIps, new List<string>());
            OperationHistoryStore.Record("Import", name, paths.Count + programs.Count);
            Refresh();
            System.Windows.MessageBox.Show(
                LocaleService.T("profileImported", name),
                "NetFence", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RollbackButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SnapshotsGrid.SelectedItem is not SnapshotRow row)
            {
                System.Windows.MessageBox.Show(LocaleService.T("selectSnapshotFirst"), "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = System.Windows.MessageBox.Show(
                LocaleService.T("rollbackConfirm"),
                LocaleService.T("rollbackConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            var snapshot = RuleSnapshotStore.GetById(row.Id);
            if (snapshot is null) return;

            await Task.Run(FirewallService.UnblockAll);

            var rules = JsonSerializer.Deserialize<List<FirewallRuleInfo>>(snapshot.RulesJson) ?? [];
            var failedPaths = new List<string>();
            foreach (var rule in rules)
            {
                if (!string.IsNullOrWhiteSpace(rule.Program) && Path.IsPathFullyQualified(rule.Program))
                {
                    try
                    {
                        await Task.Run(() =>
                            FirewallService.Block(rule.Program, rule.ProfileName, false, Array.Empty<string>()));
                    }
                    catch { failedPaths.Add(rule.Program); }
                }
            }

            RuleSnapshotStore.MarkRolledBack(row.Id);
            OperationHistoryStore.Record("Rollback", snapshot.ProfileName, rules.Count);
            Refresh();

            if (failedPaths.Count > 0)
            {
                System.Windows.MessageBox.Show(
                    LocaleService.T("rollbackPartial", rules.Count, failedPaths.Count),
                    "NetFence", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    LocaleService.T("rollbackComplete"), "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ApplyModeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ProfilesGrid.SelectedItem is not ProfileRow row) return;
            var profile = RuleProfileStore.GetById(row.Id);
            if (profile is null) return;

            var modeKey = GetSelectedMode();
            var mode = FirewallModeService.KeyToMode(modeKey);
            if (modeKey == "custom") CommitAllowedIps();

            var ips = GetAllowedIpsFromEditor();
            var targets = new List<string>();
            foreach (var path in profile.Paths)
            {
                try { targets.AddRange(NetFenceTargets.GetExecutableTargets(path)); }
                catch { }
            }
            targets.AddRange(profile.Programs.Where(File.Exists));

            if (targets.Count == 0)
            {
                System.Windows.MessageBox.Show("No valid targets found.", "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await Task.Run(() =>
                FirewallModeService.ApplyMode(profile.Name, targets.Distinct(StringComparer.OrdinalIgnoreCase),
                    mode, ips, Array.Empty<string>()));
            System.Windows.MessageBox.Show(
                LocaleService.T("modeApplied", modeKey), "NetFence",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Refresh();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ModeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var mode = GetSelectedMode();
        CustomIpPanel.Visibility = mode == "custom"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private string GetSelectedMode() =>
        (ModeBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "block_all";

    private List<string> GetAllowedIpsFromEditor() =>
        AllowedIpsBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

    private void CommitAllowedIps()
    {
        if (ProfilesGrid.SelectedItem is not ProfileRow row) return;
        var profile = RuleProfileStore.GetById(row.Id);
        if (profile is null) return;
        var ips = GetAllowedIpsFromEditor();
        RuleProfileStore.Save(profile.Name, profile.Paths, profile.Programs,
            "custom", ips, profile.AllowedDomains);
        Refresh();
    }

    public sealed class ProfileRow(RuleProfile p)
    {
        public long Id => p.Id;
        public string Name => p.Name;
        public string PathsDisplay => string.Join("; ", p.Paths);
        public string ProgramsDisplay => string.Join("; ", p.Programs);
        public string UpdatedAt => p.UpdatedAt;
    }

    public sealed class SnapshotRow(RuleSnapshot s)
    {
        public long Id => s.Id;
        public string ProfileName => s.ProfileName;
        public string Action => s.Action;
        public string Timestamp => s.Timestamp;
        public string RollbackDisplay => s.IsRollback ? "Rolled back" : "Active";
    }
}
