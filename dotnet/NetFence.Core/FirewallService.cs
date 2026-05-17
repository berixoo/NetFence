namespace NetFence.Core;

public static class FirewallService
{
    public static IReadOnlyList<FirewallRuleInfo> GetStatus()
    {
        var script = """
            $ErrorActionPreference = 'Stop'
            Get-NetFirewallRule -ErrorAction SilentlyContinue |
                Where-Object { $_.Group -like 'NetFence:*' } |
                Sort-Object Group, DisplayName |
                ForEach-Object {
                    $app = $_ | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue
                    [PSCustomObject]@{
                        ProfileName = ($_.Group -replace '^NetFence:', '')
                        DisplayName = $_.DisplayName
                        Direction = [string]$_.Direction
                        Enabled = [string]$_.Enabled
                        Action = [string]$_.Action
                        Program = [string]$app.Program
                    }
                } |
                ConvertTo-Csv -NoTypeInformation
            """;

        return LiveSystemInfo.ParseCsv(PowerShellRunner.RunRequired(script))
            .Select(row => new FirewallRuleInfo(
                row.GetValueOrDefault("ProfileName") ?? "",
                row.GetValueOrDefault("DisplayName") ?? "",
                row.GetValueOrDefault("Direction") ?? "",
                string.Equals(row.GetValueOrDefault("Enabled"), "True", StringComparison.OrdinalIgnoreCase),
                row.GetValueOrDefault("Action") ?? "",
                row.GetValueOrDefault("Program") ?? ""))
            .ToArray();
    }

    public static (string ProfileName, IReadOnlyList<string> Targets) Block(
        string path,
        string? name,
        bool includeLinked,
        IEnumerable<string> additionalTargets)
    {
        var profileName = NetFenceRules.GetProfileName(path, name);
        var targets = new HashSet<string>(
            NetFenceTargets.GetPlannedBlockTargets(path, additionalTargets),
            StringComparer.OrdinalIgnoreCase);

        if (includeLinked)
        {
            var rows = LiveSystemInfo.GetProcessRows();
            var networkIds = LiveSystemInfo.GetNetworkProcessIds();
            foreach (var candidate in RelatedProcessScanner.GetRelatedCandidates(path, rows, networkIds))
            {
                if (candidate.Selected)
                {
                    targets.Add(candidate.Program);
                }
            }
        }

        var group = NetFenceRules.GetRuleGroup(profileName);
        var scriptLines = new List<string>
        {
            "$ErrorActionPreference = 'Stop'",
            $"$group = {PowerShellRunner.Quote(group)}",
            "$existingNames = @{}",
            "Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | ForEach-Object { $existingNames[$_.DisplayName] = $true }"
        };

        foreach (var target in targets.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var direction in new[] { FirewallDirection.Outbound, FirewallDirection.Inbound })
            {
                var displayName = NetFenceRules.GetRuleName(profileName, target, direction);
                var quotedName = PowerShellRunner.Quote(displayName);
                scriptLines.Add(string.Join(Environment.NewLine,
                    $"if ($existingNames.ContainsKey({quotedName})) {{",
                    $"    Set-NetFirewallRule -DisplayName {quotedName} -Enabled True -Action Block -Direction {direction} -Profile Any | Out-Null",
                    "} else {",
                    $"    New-NetFirewallRule -DisplayName {quotedName} -Group $group -Direction {direction} -Action Block -Program {PowerShellRunner.Quote(target)} -Profile Any -Enabled True -Description {PowerShellRunner.Quote($"Managed by NetFence for profile '{profileName}'. Remove with NetFence to restore networking.")} | Out-Null",
                    "}"));
            }
        }

        var preRules = GetStatus();
        RuleSnapshotStore.Create(profileName, "Block", preRules);

        PowerShellRunner.RunRequired(string.Join(Environment.NewLine, scriptLines));
        OperationLog.Write(OperationLog.DefaultPath, "Block", $"Blocked profile '{profileName}' for {targets.Count} executable file(s).", targets);
        OperationHistoryStore.Record("Block", profileName, targets.Count);
        NetworkMonitor.InvalidateBlockedCache();
        return (profileName, targets.ToArray());
    }

    public static (string ProfileName, int RemovedRuleCount) Unblock(string path, string? name)
    {
        var profileName = NetFenceRules.GetProfileName(path, name);
        var group = NetFenceRules.GetRuleGroup(profileName);
        var script = string.Join(Environment.NewLine,
            "$ErrorActionPreference = 'Stop'",
            $"$rules = @(Get-NetFirewallRule -Group {PowerShellRunner.Quote(group)} -ErrorAction SilentlyContinue)",
            "if ($rules.Count -gt 0) { $rules | Remove-NetFirewallRule }",
            "$rules.Count");

        var preRules = GetStatus();
        RuleSnapshotStore.Create(profileName, "Unblock", preRules);

        var removed = ParseSingleInt(PowerShellRunner.RunRequired(script));
        OperationLog.Write(OperationLog.DefaultPath, "Unblock", $"Removed {removed} rule(s) for profile '{profileName}'.", []);
        OperationHistoryStore.Record("Unblock", profileName, removed);
        NetworkMonitor.InvalidateBlockedCache();
        return (profileName, removed);
    }

    public static IReadOnlyList<FirewallProgramTarget> GetSelectedProgramUnblockTargets(IEnumerable<FirewallRuleInfo> selectedRules) =>
        selectedRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.ProfileName) && !string.IsNullOrWhiteSpace(rule.Program))
            .Where(rule => Path.IsPathFullyQualified(rule.Program))
            .Select(rule => new FirewallProgramTarget(rule.ProfileName, Path.GetFullPath(rule.Program)))
            .Distinct()
            .OrderBy(rule => rule.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.Program, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static int UnblockSelectedPrograms(IEnumerable<FirewallRuleInfo> selectedRules)
    {
        var targets = GetSelectedProgramUnblockTargets(selectedRules);
        if (targets.Count == 0)
        {
            throw new InvalidOperationException("Select one or more NetFence firewall rules first.");
        }

        var scriptLines = new List<string>
        {
            "$ErrorActionPreference = 'Stop'",
            "$removed = 0"
        };

        foreach (var group in targets.GroupBy(target => target.ProfileName, StringComparer.OrdinalIgnoreCase))
        {
            var targetSetName = "$targetPrograms" + (StringComparer.OrdinalIgnoreCase.GetHashCode(group.Key) & 0x7FFFFFFF);
            scriptLines.Add(string.Join(Environment.NewLine,
                $"{targetSetName} = @{{}}",
                string.Join(Environment.NewLine, group.Select(target => $"{targetSetName}[{PowerShellRunner.Quote(target.Program)}] = $true")),
                $"$rules = @(Get-NetFirewallRule -Group {PowerShellRunner.Quote(NetFenceRules.GetRuleGroup(group.Key))} -ErrorAction SilentlyContinue)",
                "foreach ($rule in $rules) {",
                "    $app = $rule | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue",
                $"    if ({targetSetName}.ContainsKey([string]$app.Program)) {{",
                "        $rule | Remove-NetFirewallRule",
                "        $removed += 1",
                "    }",
                "}"));
        }

        scriptLines.Add("$removed");

        var preRules = GetStatus();
        RuleSnapshotStore.Create("selected", "UnblockSelected", preRules);

        var removed = ParseSingleInt(PowerShellRunner.RunRequired(string.Join(Environment.NewLine, scriptLines)));
        OperationLog.Write(OperationLog.DefaultPath, "UnblockSelected", $"Removed {removed} rule(s) for {targets.Count} selected executable file(s).", targets.Select(target => target.Program));
        OperationHistoryStore.Record("UnblockSelected", "selected", targets.Count);
        NetworkMonitor.InvalidateBlockedCache();
        return removed;
    }

    public static int UnblockAll()
    {
        var script = """
            $ErrorActionPreference = 'Stop'
            $rules = @(Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.Group -like 'NetFence:*' })
            if ($rules.Count -gt 0) { $rules | Remove-NetFirewallRule }
            $rules.Count
            """;

        var preRules = GetStatus();
        RuleSnapshotStore.Create("all", "UnblockAll", preRules);

        var removed = ParseSingleInt(PowerShellRunner.RunRequired(script));
        OperationLog.Write(OperationLog.DefaultPath, "UnblockAll", $"Removed {removed} NetFence rule(s).", []);
        OperationHistoryStore.Record("UnblockAll", "all", removed);
        NetworkMonitor.InvalidateBlockedCache();
        return removed;
    }

    private static int ParseSingleInt(string output)
    {
        var value = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }
}
