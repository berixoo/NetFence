# NetFence P5 — Network Modes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development

**Goal:** Add 4 network modes to rule profiles (block_all / allow_all / lan_only / custom IPs), with mode-specific firewall rule creation.

**Architecture:** `FirewallModeService` creates mode-specific rule sets using `PowerShellRunner`. `RuleProfileStore` extended with AllowedIpsJson/AllowedDomainsJson columns. `RuleProfilesPage` gains mode dropdown + IP editor.

**Tech Stack:** .NET 9, WPF, SQLite, PowerShell

---

### Task 1: FirewallModeService.cs + Database migration

**Files:**
- Create: `dotnet/NetFence.Core/FirewallModeService.cs`
- Modify: `dotnet/NetFence.Core/Database.cs` (add migration for new columns)
- Modify: `dotnet/NetFence.Core/Models.cs` (extend RuleProfile, add NetworkMode enum)
- Modify: `dotnet/NetFence.Core/RuleProfileStore.cs` (handle new fields)

- [ ] **Step 1: Add NetworkMode enum + extend RuleProfile in Models.cs**

```csharp
public enum NetworkMode
{
    BlockAll,   // "block_all"
    AllowAll,   // "allow_all"
    LanOnly,    // "lan_only"
    Custom      // "custom"
}
```

Update RuleProfile record (in RuleProfileStore.cs) to:
```csharp
public sealed record RuleProfile(
    long Id, string Name, List<string> Paths, List<string> Programs,
    string Mode, List<string> AllowedIps, List<string> AllowedDomains,
    string CreatedAt, string UpdatedAt);
```

- [ ] **Step 2: Add migration to Database.cs**

In `EnsureCreated`, after the CREATE TABLE statements, add:
```csharp
// Migration: add columns if not existing
try
{
    using var migCmd = conn.CreateCommand();
    migCmd.CommandText = "ALTER TABLE RuleProfiles ADD COLUMN AllowedIpsJson TEXT NOT NULL DEFAULT '[]'";
    migCmd.ExecuteNonQuery();
}
catch { /* column may already exist */ }
try
{
    using var migCmd = conn.CreateCommand();
    migCmd.CommandText = "ALTER TABLE RuleProfiles ADD COLUMN AllowedDomainsJson TEXT NOT NULL DEFAULT '[]'";
    migCmd.ExecuteNonQuery();
}
catch { }
```

- [ ] **Step 3: Create FirewallModeService.cs**

```csharp
namespace NetFence.Core;

public static class FirewallModeService
{
    public static readonly IReadOnlyList<string> LanRanges = new[]
    {
        "127.0.0.0/8", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"
    };

    public static string ModeToKey(NetworkMode mode) => mode switch
    {
        NetworkMode.BlockAll => "block_all",
        NetworkMode.AllowAll => "allow_all",
        NetworkMode.LanOnly => "lan_only",
        NetworkMode.Custom => "custom",
        _ => "block_all"
    };

    public static NetworkMode KeyToMode(string key) => key switch
    {
        "allow_all" => NetworkMode.AllowAll,
        "lan_only" => NetworkMode.LanOnly,
        "custom" => NetworkMode.Custom,
        _ => NetworkMode.BlockAll
    };

    public static void ApplyMode(string profileName, IEnumerable<string> targets,
        NetworkMode mode, IReadOnlyList<string> allowedIps,
        IReadOnlyList<string> allowedDomains)
    {
        // Remove existing rules for this profile
        var group = NetFenceRules.GetRuleGroup(profileName);
        var removeScript = string.Join(Environment.NewLine,
            "$ErrorActionPreference = 'Stop'",
            $"$rules = @(Get-NetFirewallRule -Group {PowerShellRunner.Quote(group)} -ErrorAction SilentlyContinue)",
            "if ($rules.Count -gt 0) { $rules | Remove-NetFirewallRule }");
        PowerShellRunner.RunRequired(removeScript);

        if (mode == NetworkMode.AllowAll) return;

        // Apply block rules for all targets (all modes except AllowAll)
        foreach (var target in targets)
        {
            foreach (var direction in new[] { FirewallDirection.Outbound, FirewallDirection.Inbound })
            {
                var displayName = NetFenceRules.GetRuleName(profileName, target, direction);
                PowerShellRunner.RunRequired(string.Join(Environment.NewLine,
                    "$ErrorActionPreference = 'Stop'",
                    $"New-NetFirewallRule -DisplayName {PowerShellRunner.Quote(displayName)} " +
                    $"-Group {PowerShellRunner.Quote(group)} " +
                    $"-Direction {direction} -Action Block " +
                    $"-Program {PowerShellRunner.Quote(target)} " +
                    "-Profile Any -Enabled True | Out-Null"));
            }
        }

        // Add allow rules for LAN or custom mode (Outbound only)
        if (mode is NetworkMode.LanOnly or NetworkMode.Custom)
        {
            var remoteIps = mode == NetworkMode.LanOnly
                ? LanRanges
                : allowedIps.Where(ip => !string.IsNullOrWhiteSpace(ip)).ToList();

            if (remoteIps.Count > 0)
            {
                var ipList = string.Join(",", remoteIps);
                foreach (var target in targets)
                {
                    var allowName = NetFenceRules.GetRuleName(profileName, target + "_allow",
                        FirewallDirection.Outbound);
                    PowerShellRunner.RunRequired(string.Join(Environment.NewLine,
                        "$ErrorActionPreference = 'Stop'",
                        $"New-NetFirewallRule -DisplayName {PowerShellRunner.Quote(allowName)} " +
                        $"-Group {PowerShellRunner.Quote(group)} " +
                        "-Direction Outbound -Action Allow " +
                        $"-Program {PowerShellRunner.Quote(target)} " +
                        $"-RemoteAddress {PowerShellRunner.Quote(ipList)} " +
                        "-Profile Any -Enabled True | Out-Null"));
                }
            }
        }
    }
}
```

- [ ] **Step 4: Update RuleProfileStore to handle new fields**

In `ListAll()` and `GetById()` — change SELECT to include AllowedIpsJson, AllowedDomainsJson:
```sql
SELECT Id, Name, PathsJson, ProgramsJson, Mode, CreatedAt, UpdatedAt,
       COALESCE(AllowedIpsJson, '[]'), COALESCE(AllowedDomainsJson, '[]')
FROM RuleProfiles ORDER BY UpdatedAt DESC
```

In the reader loop, deserialize AllowedIps and AllowedDomains from Json.

In `Save()` — add parameters `List<string>? allowedIps = null, List<string>? allowedDomains = null`. Serialize them and insert/update along with the other fields.

- [ ] **Step 5: Build + Commit**

```bash
dotnet build dotnet/NetFence.Core/NetFence.Core.csproj -c Debug
git add dotnet/NetFence.Core/
git commit -m "feat: add FirewallModeService, network modes, DB migration for allowed IPs/domains"
```

---

### Task 2: RuleProfilesPage UI — mode selector + IP editor

**Files:**
- Modify: `dotnet/NetFence.App/Pages/RuleProfilesPage.xaml`
- Modify: `dotnet/NetFence.App/Pages/RuleProfilesPage.xaml.cs`
- Modify: `dotnet/NetFence.App/Services/LocaleService.cs` (translation keys)

- [ ] **Step 1: Add UI elements to RuleProfilesPage.xaml**

After the profile action buttons row, add:
```xml
<!-- Mode selector row -->
<StackPanel Grid.Row="..." Orientation="Horizontal" Margin="0,0,0,6">
    <TextBlock x:Name="ModeLabel" VerticalAlignment="Center"
               Foreground="{DynamicResource PrimaryText}" Margin="0,0,6,0"/>
    <ComboBox x:Name="ModeBox" Width="160" Height="28"
              SelectionChanged="ModeBox_SelectionChanged">
        <ComboBoxItem x:Name="ModeBlockAll" Tag="block_all"/>
        <ComboBoxItem x:Name="ModeAllowAll" Tag="allow_all"/>
        <ComboBoxItem x:Name="ModeLanOnly" Tag="lan_only"/>
        <ComboBoxItem x:Name="ModeCustom" Tag="custom"/>
    </ComboBox>
</StackPanel>

<!-- IP/Domain editor (visible for custom mode) -->
<StackPanel x:Name="CustomIpPanel" Visibility="Collapsed" Margin="0,0,0,6">
    <TextBlock x:Name="AllowedIpsLabel" Margin="0,0,0,4"
               Foreground="{DynamicResource PrimaryText}"/>
    <TextBox x:Name="AllowedIpsBox" Height="80" MinHeight="60"
             TextWrapping="Wrap" AcceptsReturn="True"
             Background="{DynamicResource InputBackground}"
             Foreground="{DynamicResource PrimaryText}"
             BorderBrush="{DynamicResource BorderColor}"
             VerticalScrollBarVisibility="Auto"/>
</StackPanel>
```

- [ ] **Step 2: Update code-behind**

In `SaveProfileButton_Click`: read ModeBox selection, read AllowedIpsBox text (split by newlines), pass to `RuleProfileStore.Save()`.

In `LoadProfileButton_Click`: pass mode/allowedIps/allowedDomains to `FirewallModeService.ApplyMode()`.

Add `ModeBox_SelectionChanged`: show/hide CustomIpPanel based on whether "custom" is selected.

- [ ] **Step 3: Translation keys**

Add to en-US/zh-CN:
```
["networkMode"] / "Network Mode" / "联网模式"
["modeBlockAll"] / "Block All" / "禁止全部"
["modeAllowAll"] / "Allow All" / "允许全部"
["modeLanOnly"] / "LAN Only" / "仅局域网"
["modeCustom"] / "Custom IP/Domain" / "指定IP/域名"
["allowedIpsLabel"] / "Allowed IPs/Domains (one per line)" / "允许的 IP/域名（每行一个）"
["modeApplied"] / "Mode '{0}' applied." / "模式 '{0}' 已应用。"
```

- [ ] **Step 4: Build + Commit**

```bash
dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Debug
git add dotnet/NetFence.App/
git commit -m "feat: add network mode selector and IP/domain editor to RuleProfilesPage"
```

---

### Task 3: Integration test

- [ ] **Step 1: Build Release + run core tests**

```bash
dotnet build dotnet/NetFence.App/NetFence.App.csproj -c Release
dotnet run --project dotnet/NetFence.Core.Tests/NetFence.Core.Tests.csproj -c Release
```

- [ ] **Step 2: Manual verification**
1. Open "规则档案" → mode dropdown shows 4 options
2. Select "仅局域网" → no IP editor shown
3. Select "指定IP/域名" → IP editor appears
4. Enter IPs → save profile → load → rules created with Allow for specified IPs
5. Select "允许全部" → load → NetFence rules removed
6. Export/import preserves mode and IPs
