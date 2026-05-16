# NetFence P2 — SQLite Data Layer + Rule Management

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add SQLite local database for rule profiles, pre-operation snapshots, and operation history; implement JSON import/export and snapshot rollback.

**Architecture:** Four new static classes in NetFence.Core follow the existing pattern (FirewallService, etc.). `Database.cs` manages the SQLite connection and schema. Three Store classes each handle one table. `FirewallService` is modified to call snapshot creation before destructive operations. `RuleProfilesPage` is rewritten from placeholder to full UI.

**Tech Stack:** .NET 9, Microsoft.Data.Sqlite, WPF, System.Text.Json

---

### Task 1: Add NuGet package + Database.cs

**Files:**
- Modify: `dotnet/NetFence.Core/NetFence.Core.csproj`
- Create: `dotnet/NetFence.Core/Database.cs`

- [ ] **Step 1: Add Microsoft.Data.Sqlite package**

```bash
cd dotnet/NetFence.Core && dotnet add package Microsoft.Data.Sqlite
```

- [ ] **Step 2: Create Database.cs**

```csharp
using Microsoft.Data.Sqlite;

namespace NetFence.Core;

public static class Database
{
    private static readonly string ConnectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetFence",
            "NetFence.db")
    }.ToString();

    private static bool _initialized;

    public static SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnsureCreated(conn);
        return conn;
    }

    public static string DataDirectory => Path.GetDirectoryName(
        new SqliteConnectionStringBuilder(ConnectionString).DataSource)!;

    private static void EnsureCreated(SqliteConnection conn)
    {
        if (_initialized) return;
        _initialized = true;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS RuleProfiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                PathsJson TEXT NOT NULL DEFAULT '[]',
                ProgramsJson TEXT NOT NULL DEFAULT '[]',
                Mode TEXT NOT NULL DEFAULT 'block_all',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RuleSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileName TEXT NOT NULL,
                Action TEXT NOT NULL,
                RulesJson TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                IsRollback INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS OperationHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Action TEXT NOT NULL,
                ProfileName TEXT NOT NULL DEFAULT '',
                TargetCount INTEGER NOT NULL DEFAULT 0,
                Timestamp TEXT NOT NULL,
                DetailsJson TEXT NOT NULL DEFAULT '{}'
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build dotnet/NetFence.Core/NetFence.Core.csproj -c Debug
```
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add dotnet/NetFence.Core/NetFence.Core.csproj dotnet/NetFence.Core/Database.cs
git commit -m "feat: add Microsoft.Data.Sqlite and Database connection/schema setup"
```

---

### Task 2: RuleProfileStore.cs — config profile CRUD

**Files:**
- Create: `dotnet/NetFence.Core/RuleProfileStore.cs`

- [ ] **Step 1: Write RuleProfileStore.cs**

```csharp
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NetFence.Core;

public sealed record RuleProfile(
    long Id,
    string Name,
    List<string> Paths,
    List<string> Programs,
    string Mode,
    string CreatedAt,
    string UpdatedAt);

public static class RuleProfileStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
        { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IReadOnlyList<RuleProfile> ListAll()
    {
        var results = new List<RuleProfile>();
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, PathsJson, ProgramsJson, Mode, CreatedAt, UpdatedAt FROM RuleProfiles ORDER BY UpdatedAt DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RuleProfile(
                reader.GetInt64(0),
                reader.GetString(1),
                JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [],
                JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [],
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6)));
        }
        return results;
    }

    public static RuleProfile? GetById(long id)
    {
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, PathsJson, ProgramsJson, Mode, CreatedAt, UpdatedAt FROM RuleProfiles WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new RuleProfile(
            reader.GetInt64(0),
            reader.GetString(1),
            JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [],
            JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [],
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    public static long Save(string name, List<string> paths, List<string> programs, string mode = "block_all")
    {
        var now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        var pathsJson = JsonSerializer.Serialize(paths, JsonOpts);
        var programsJson = JsonSerializer.Serialize(programs, JsonOpts);

        using var conn = Database.OpenConnection();
        // Upsert: update if name exists, else insert
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT Id FROM RuleProfiles WHERE Name = @name";
        checkCmd.Parameters.AddWithValue("@name", name);
        var existing = checkCmd.ExecuteScalar();

        if (existing is long id)
        {
            using var update = conn.CreateCommand();
            update.CommandText = "UPDATE RuleProfiles SET PathsJson=@p, ProgramsJson=@pr, Mode=@m, UpdatedAt=@u WHERE Id=@id";
            update.Parameters.AddWithValue("@p", pathsJson);
            update.Parameters.AddWithValue("@pr", programsJson);
            update.Parameters.AddWithValue("@m", mode);
            update.Parameters.AddWithValue("@u", now);
            update.Parameters.AddWithValue("@id", id);
            update.ExecuteNonQuery();
            return id;
        }
        else
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO RuleProfiles(Name, PathsJson, ProgramsJson, Mode, CreatedAt, UpdatedAt) VALUES(@n,@p,@pr,@m,@c,@u)";
            insert.Parameters.AddWithValue("@n", name);
            insert.Parameters.AddWithValue("@p", pathsJson);
            insert.Parameters.AddWithValue("@pr", programsJson);
            insert.Parameters.AddWithValue("@m", mode);
            insert.Parameters.AddWithValue("@c", now);
            insert.Parameters.AddWithValue("@u", now);
            insert.ExecuteNonQuery();

            using var lastId = conn.CreateCommand();
            lastId.CommandText = "SELECT last_insert_rowid()";
            return (long)lastId.ExecuteScalar()!;
        }
    }

    public static bool Delete(long id)
    {
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RuleProfiles WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build dotnet/NetFence.Core/NetFence.Core.csproj -c Debug
```

- [ ] **Step 3: Commit**

```bash
git add dotnet/NetFence.Core/RuleProfileStore.cs
git commit -m "feat: add RuleProfileStore with CRUD operations"
```

---

### Task 3: RuleSnapshotStore.cs + OperationHistoryStore.cs

**Files:**
- Create: `dotnet/NetFence.Core/RuleSnapshotStore.cs`
- Create: `dotnet/NetFence.Core/OperationHistoryStore.cs`

- [ ] **Step 1: Create RuleSnapshotStore.cs**

```csharp
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NetFence.Core;

public sealed record RuleSnapshot(
    long Id,
    string ProfileName,
    string Action,
    string RulesJson,
    string Timestamp,
    bool IsRollback);

public static class RuleSnapshotStore
{
    private const int MaxSnapshots = 50;

    public static long Create(string profileName, string action, IReadOnlyList<FirewallRuleInfo> rules)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        var rulesJson = JsonSerializer.Serialize(rules);

        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO RuleSnapshots(ProfileName, Action, RulesJson, Timestamp) VALUES(@pn,@a,@rj,@t)";
        cmd.Parameters.AddWithValue("@pn", profileName);
        cmd.Parameters.AddWithValue("@a", action);
        cmd.Parameters.AddWithValue("@rj", rulesJson);
        cmd.Parameters.AddWithValue("@t", timestamp);
        cmd.ExecuteNonQuery();

        // Get last inserted id
        using var lastId = conn.CreateCommand();
        lastId.CommandText = "SELECT last_insert_rowid()";
        var id = (long)lastId.ExecuteScalar()!;

        CleanupOld(conn);
        return id;
    }

    public static IReadOnlyList<RuleSnapshot> ListRecent(int limit = 20)
    {
        var results = new List<RuleSnapshot>();
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, ProfileName, Action, RulesJson, Timestamp, IsRollback FROM RuleSnapshots ORDER BY Id DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RuleSnapshot(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5) != 0));
        }
        return results;
    }

    public static RuleSnapshot? GetById(long id)
    {
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, ProfileName, Action, RulesJson, Timestamp, IsRollback FROM RuleSnapshots WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new RuleSnapshot(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5) != 0);
    }

    public static void MarkRolledBack(long id)
    {
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE RuleSnapshots SET IsRollback = 1 WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static void CleanupOld(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RuleSnapshots WHERE Id NOT IN (SELECT Id FROM RuleSnapshots ORDER BY Id DESC LIMIT @max)";
        cmd.Parameters.AddWithValue("@max", MaxSnapshots);
        cmd.ExecuteNonQuery();
    }
}
```

- [ ] **Step 2: Create OperationHistoryStore.cs**

```csharp
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NetFence.Core;

public sealed record OperationRecord(
    long Id,
    string Action,
    string ProfileName,
    int TargetCount,
    string Timestamp,
    string DetailsJson);

public static class OperationHistoryStore
{
    public static void Record(string action, string profileName, int targetCount, object? details = null)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        var detailsJson = details is null ? "{}" : JsonSerializer.Serialize(details);

        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO OperationHistory(Action, ProfileName, TargetCount, Timestamp, DetailsJson) VALUES(@a,@pn,@tc,@t,@dj)";
        cmd.Parameters.AddWithValue("@a", action);
        cmd.Parameters.AddWithValue("@pn", profileName);
        cmd.Parameters.AddWithValue("@tc", targetCount);
        cmd.Parameters.AddWithValue("@t", timestamp);
        cmd.Parameters.AddWithValue("@dj", detailsJson);
        cmd.ExecuteNonQuery();
    }

    public static IReadOnlyList<OperationRecord> ListRecent(int limit = 30)
    {
        var results = new List<OperationRecord>();
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Action, ProfileName, TargetCount, Timestamp, DetailsJson FROM OperationHistory ORDER BY Id DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new OperationRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5)));
        }
        return results;
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build dotnet/NetFence.Core/NetFence.Core.csproj -c Debug
```

- [ ] **Step 4: Commit**

```bash
git add dotnet/NetFence.Core/RuleSnapshotStore.cs dotnet/NetFence.Core/OperationHistoryStore.cs
git commit -m "feat: add RuleSnapshotStore and OperationHistoryStore"
```

---

### Task 4: Modify FirewallService.cs — add pre-operation snapshots + history

**Files:**
- Modify: `dotnet/NetFence.Core/FirewallService.cs`

- [ ] **Step 1: Add pre-operation snapshot in Block method**

Wrap the Block operation with snapshot + history. The Block method in FirewallService.cs (lines 37-88) already works. Inject snapshot creation before the PowerShell script executes, and history recording after.

Replace the `Block` method content (after the targets/group setup, before `PowerShellRunner.RunRequired`) by adding these lines BEFORE the PowerShell execution block:

```csharp
// Take pre-operation snapshot
var preRules = GetStatus();
RuleSnapshotStore.Create(profileName, "Block", preRules);
```

And after the `OperationLog.Write` call, add:

```csharp
OperationHistoryStore.Record("Block", profileName, targets.Count);
```

The actual edit: insert snapshot creation between the script building and `PowerShellRunner.RunRequired`, and history recording after `OperationLog.Write`.

Read the current file first, then make targeted edits:

Edit 1 — after `var scriptLines = new List<string>` block (around line 63-68, before calling `PowerShellRunner.RunRequired`), insert:

```csharp
        var preRules = GetStatus();
        RuleSnapshotStore.Create(profileName, "Block", preRules);
```

Edit 2 — after `OperationLog.Write(...)` call in Block (around line 87), add:

```csharp
        OperationHistoryStore.Record("Block", profileName, targets.Count);
```

- [ ] **Step 2: Add pre-operation snapshot in Unblock method**

In the `Unblock` method (lines 90-103), before `PowerShellRunner.RunRequired`:

```csharp
        var preRules = GetStatus();
        RuleSnapshotStore.Create(profileName, "Unblock", preRules);
```

After `OperationLog.Write`:

```csharp
        OperationHistoryStore.Record("Unblock", profileName, 0);
```

- [ ] **Step 3: Add pre-operation snapshot in UnblockSelectedPrograms method**

In `UnblockSelectedPrograms` (lines 115-149), before building script:

```csharp
        var preRules = GetStatus();
        RuleSnapshotStore.Create("selected", "UnblockSelected", preRules);
```

After `OperationLog.Write`:

```csharp
        OperationHistoryStore.Record("UnblockSelected", "selected", targets.Count);
```

- [ ] **Step 4: Add pre-operation snapshot in UnblockAll method**

In `UnblockAll` (lines 151-163), before `PowerShellRunner.RunRequired`:

```csharp
        var preRules = GetStatus();
        RuleSnapshotStore.Create("all", "UnblockAll", preRules);
```

After `OperationLog.Write`:

```csharp
        OperationHistoryStore.Record("UnblockAll", "all", removed);
```

- [ ] **Step 5: Verify build + run core tests**

```bash
dotnet build dotnet/NetFence.Core/NetFence.Core.csproj -c Debug
dotnet run --project dotnet/NetFence.Core.Tests/NetFence.Core.Tests.csproj -c Release
```
Expected: Build succeeds, all existing tests pass.

- [ ] **Step 6: Commit**

```bash
git add dotnet/NetFence.Core/FirewallService.cs
git commit -m "feat: add pre-operation snapshots and history recording to FirewallService"
```

---

### Task 5: Rewrite RuleProfilesPage — functional UI with profiles, snapshots, rollback

**Files:**
- Modify: `dotnet/NetFence.App/Pages/RuleProfilesPage.xaml`
- Modify: `dotnet/NetFence.App/Pages/RuleProfilesPage.xaml.cs`

- [ ] **Step 1: Rewrite RuleProfilesPage.xaml**

Replace the placeholder with full layout: profile list + action buttons (save/load/delete/export/import) + snapshot list + rollback button.

```xml
<UserControl x:Class="NetFence.App.Pages.RuleProfilesPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="16" Background="{DynamicResource ContentBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Profile section header -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,6">
            <TextBlock x:Name="ProfilesLabel" FontSize="14" FontWeight="SemiBold"
                       Foreground="{DynamicResource PrimaryText}" VerticalAlignment="Center"/>
            <Button x:Name="SaveProfileButton" MinWidth="80" Height="28" Margin="8,0,4,0"
                    Click="SaveProfileButton_Click"
                    Background="{DynamicResource ButtonPrimaryBackground}"
                    Foreground="{DynamicResource ButtonPrimaryForeground}" BorderThickness="0"/>
            <Button x:Name="LoadProfileButton" MinWidth="80" Height="28" Margin="4,0"
                    Click="LoadProfileButton_Click"
                    Background="{DynamicResource ButtonSecondaryBackground}"
                    Foreground="{DynamicResource ButtonSecondaryForeground}"
                    BorderBrush="{DynamicResource ButtonSecondaryBorder}"/>
            <Button x:Name="DeleteProfileButton" MinWidth="80" Height="28" Margin="4,0"
                    Click="DeleteProfileButton_Click"
                    Background="{DynamicResource ButtonSecondaryBackground}"
                    Foreground="{DynamicResource ButtonSecondaryForeground}"
                    BorderBrush="{DynamicResource ButtonSecondaryBorder}"/>
            <Button x:Name="ExportProfileButton" MinWidth="80" Height="28" Margin="4,0"
                    Click="ExportProfileButton_Click"
                    Background="{DynamicResource ButtonSecondaryBackground}"
                    Foreground="{DynamicResource ButtonSecondaryForeground}"
                    BorderBrush="{DynamicResource ButtonSecondaryBorder}"/>
            <Button x:Name="ImportProfileButton" MinWidth="80" Height="28" Margin="4,0"
                    Click="ImportProfileButton_Click"
                    Background="{DynamicResource ButtonSecondaryBackground}"
                    Foreground="{DynamicResource ButtonSecondaryForeground}"
                    BorderBrush="{DynamicResource ButtonSecondaryBorder}"/>
        </StackPanel>

        <!-- Profiles DataGrid -->
        <DataGrid x:Name="ProfilesGrid" Grid.Row="1" AutoGenerateColumns="False"
                  CanUserAddRows="False" IsReadOnly="True" Height="140"
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
                <DataGridTextColumn x:Name="ProfileNameColumn" Binding="{Binding Name}" Width="2*"/>
                <DataGridTextColumn x:Name="ProfilePathsColumn" Binding="{Binding PathsDisplay}" Width="3*"/>
                <DataGridTextColumn x:Name="ProfileProgramsColumn" Binding="{Binding ProgramsDisplay}" Width="2*"/>
                <DataGridTextColumn x:Name="ProfileUpdatedColumn" Binding="{Binding UpdatedAt}" Width="150"/>
            </DataGrid.Columns>
        </DataGrid>

        <!-- Separator -->
        <Border Grid.Row="2" Height="1" Margin="0,12,0,8"
                Background="{DynamicResource SeparatorColor}"/>

        <!-- Snapshot section header -->
        <StackPanel Grid.Row="3" Orientation="Horizontal" Margin="0,0,0,6">
            <TextBlock x:Name="SnapshotsLabel" FontSize="14" FontWeight="SemiBold"
                       Foreground="{DynamicResource PrimaryText}" VerticalAlignment="Center"/>
            <Button x:Name="RollbackButton" MinWidth="80" Height="28" Margin="8,0,4,0"
                    Click="RollbackButton_Click"
                    Background="{DynamicResource ButtonDangerBackground}"
                    Foreground="{DynamicResource ButtonDangerForeground}" BorderThickness="0"/>
        </StackPanel>

        <!-- Snapshots DataGrid -->
        <DataGrid x:Name="SnapshotsGrid" Grid.Row="4" AutoGenerateColumns="False"
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
                <DataGridTextColumn x:Name="SnapProfileColumn" Binding="{Binding ProfileName}" Width="120"/>
                <DataGridTextColumn x:Name="SnapActionColumn" Binding="{Binding Action}" Width="100"/>
                <DataGridTextColumn x:Name="SnapTimestampColumn" Binding="{Binding Timestamp}" Width="180"/>
                <DataGridTextColumn x:Name="SnapRollbackColumn" Binding="{Binding RollbackDisplay}" Width="80"/>
            </DataGrid.Columns>
        </DataGrid>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Rewrite RuleProfilesPage.xaml.cs**

```csharp
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

    public void OnNavigatedTo() => Refresh();

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
            // Save current scan/block state as a profile
            // The user should have a path entered on ScanBlockPage
            var scanPage = FindScanBlockPage();
            if (scanPage is null)
            {
                System.Windows.MessageBox.Show(LocaleService.T("selectTargetFirst"), "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = System.Windows.MessageBox.Show(
                LocaleService.T("saveProfileConfirm"),
                "NetFence", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            // Use current firewall rules to build profile
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
            RuleProfileStore.Save(name, paths, programs);
            Refresh();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadProfileButton_Click(object sender, RoutedEventArgs e)
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

            // Pass targets to FirewallService.Block
            foreach (var path in profile.Paths)
            {
                try { var targets = NetFenceTargets.GetExecutableTargets(path); }
                catch { }
            }

            FirewallService.Block(profile.Paths.FirstOrDefault() ?? "", profile.Name, false, profile.Programs);
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

            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"NetFence-{row.Name}-{DateTime.Now:yyyyMMdd}.json"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            var profile = RuleProfileStore.GetById(row.Id);
            if (profile is null) return;

            var export = new
            {
                version = "1.0",
                exportedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                name = profile.Name,
                paths = profile.Paths,
                programs = profile.Programs,
                mode = profile.Mode
            };
            File.WriteAllText(dialog.FileName,
                JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));

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
            var dialog = new OpenFileDialog
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

            var id = RuleProfileStore.Save(name, paths, programs, mode);
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

            // Remove all current NetFence rules
            await Task.Run(FirewallService.UnblockAll);

            // Restore rules from snapshot
            var rules = JsonSerializer.Deserialize<List<FirewallRuleInfo>>(snapshot.RulesJson) ?? [];
            foreach (var rule in rules)
            {
                if (!string.IsNullOrWhiteSpace(rule.Program) && Path.IsPathFullyQualified(rule.Program))
                {
                    try
                    {
                        await Task.Run(() =>
                            FirewallService.Block(rule.Program, rule.ProfileName, false, Array.Empty<string>()));
                    }
                    catch { }
                }
            }

            RuleSnapshotStore.MarkRolledBack(row.Id);
            OperationHistoryStore.Record("Rollback", snapshot.ProfileName, rules.Count);
            Refresh();
            System.Windows.MessageBox.Show(
                LocaleService.T("rollbackComplete"), "NetFence",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "NetFence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private ScanBlockPage? FindScanBlockPage()
    {
        var window = Window.GetWindow(this);
        if (window is MainWindow main)
        {
            return main.FindName("ContentHost") is System.Windows.Controls.ContentControl cc
                && cc.Content is ScanBlockPage sbp ? sbp : null;
        }
        return null;
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
```

- [ ] **Step 3: Add translation keys to LocaleService**

In `dotnet/NetFence.App/Services/LocaleService.cs`, add these keys to both en-US and zh-CN dictionaries:

en-US additions:
```csharp
["ruleProfilesSection"] = "Rule Profiles",
["saveProfile"] = "Save Profile",
["loadProfile"] = "Load & Block",
["deleteProfile"] = "Delete",
["exportProfile"] = "Export JSON",
["importProfile"] = "Import JSON",
["snapshotsSection"] = "Snapshots",
["rollback"] = "Rollback",
["columnProfileName"] = "Name",
["columnPaths"] = "Paths",
["columnPrograms"] = "Programs",
["columnUpdated"] = "Updated",
["columnStatus"] = "Status",
["saveProfileConfirm"] = "Save current NetFence firewall rules as a new profile?",
["selectProfileFirst"] = "Select a profile first.",
["loadProfileConfirm"] = "Load profile '{0}' and apply its block rules?",
["profileLoaded"] = "Profile loaded and rules applied.",
["noRulesToSave"] = "No NetFence rules exist to save as a profile.",
["profileImported"] = "Profile '{0}' imported successfully.",
["selectSnapshotFirst"] = "Select a snapshot first.",
["rollbackConfirm"] = "This will remove ALL current NetFence rules and restore the snapshot state. Continue?",
["rollbackConfirmTitle"] = "Rollback to snapshot",
["rollbackComplete"] = "Rollback complete. Rules restored from snapshot.",
["ruleProfilesColumnName"] = "Name",
```

zh-CN additions:
```csharp
["ruleProfilesSection"] = "规则档案",
["saveProfile"] = "保存档案",
["loadProfile"] = "加载并阻断",
["deleteProfile"] = "删除",
["exportProfile"] = "导出 JSON",
["importProfile"] = "导入 JSON",
["snapshotsSection"] = "操作快照",
["rollback"] = "回滚",
["columnProfileName"] = "名称",
["columnPaths"] = "路径",
["columnPrograms"] = "程序",
["columnUpdated"] = "更新时间",
["columnStatus"] = "状态",
["saveProfileConfirm"] = "将当前 NetFence 防火墙规则保存为新档案？",
["selectProfileFirst"] = "请先选择一个档案。",
["loadProfileConfirm"] = "加载档案 '{0}' 并应用阻断规则？",
["profileLoaded"] = "档案已加载，规则已应用。",
["noRulesToSave"] = "没有可保存的 NetFence 规则。",
["profileImported"] = "档案 '{0}' 已导入。",
["selectSnapshotFirst"] = "请先选择一个快照。",
["rollbackConfirm"] = "此操作将删除当前所有 NetFence 规则并恢复到快照状态。是否继续？",
["rollbackConfirmTitle"] = "回滚到快照",
["rollbackComplete"] = "回滚完成。规则已从快照恢复。",
```

- [ ] **Step 4: Verify build**

```bash
dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug
```

- [ ] **Step 5: Commit**

```bash
git add dotnet/NetFence.App/Pages/RuleProfilesPage.xaml dotnet/NetFence.App/Pages/RuleProfilesPage.xaml.cs dotnet/NetFence.App/Services/LocaleService.cs
git commit -m "feat: rewrite RuleProfilesPage with profile management, snapshots, and rollback"
```

---

### Task 6: Integration test — build, run tests, verify

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

1. Run the app as Administrator
2. Navigate to "规则档案" in sidebar — page shows empty profiles and snapshots lists
3. On "扫描封禁" page, block a test program
4. Navigate back to "规则档案" — a snapshot appears for the Block operation
5. Click "保存档案" — saves current rules as a new profile
6. Profiles DataGrid shows the saved profile with name, paths, programs, timestamp
7. Select the profile → Export JSON — file is written with correct format
8. Delete the profile → it disappears
9. Import a JSON file → profile appears in the list
10. Select a snapshot → click "回滚" → confirm → rules are restored/removed
11. "打开日志" still works from Settings page

- [ ] **Step 4: Commit any fixes**

```bash
git add -A && git commit -m "fix: resolve P2 integration issues found during manual testing"
```
