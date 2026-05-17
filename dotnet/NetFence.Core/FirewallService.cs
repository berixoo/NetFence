using System.Text.Json;

namespace NetFence.Core;

public static class FirewallService
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    public static async Task<IReadOnlyList<FirewallRuleInfo>> GetStatusAsync(CancellationToken cancel = default)
    {
        var result = await PowerShellRunner.RunAsync(GetStatusScript, ReadTimeout, cancel);
        return ParseStatusOutput(result.StandardOutput);
    }

    public static IReadOnlyList<FirewallRuleInfo> GetStatus()
    {
        return ParseStatusOutput(PowerShellRunner.RunRequired(GetStatusScript));
    }

    private static string GetStatusScript => string.Join(Environment.NewLine,
        "$ErrorActionPreference = 'Continue'",
        "Get-NetFirewallRule -ErrorAction SilentlyContinue |",
        "    Where-Object { $_.Group -like 'NetFence:*' } |",
        "    Sort-Object Group, DisplayName |",
        "    ForEach-Object {",
        "        $app = $_ | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue",
        "        $addr = $_ | Get-NetFirewallAddressFilter -ErrorAction SilentlyContinue",
        "        [PSCustomObject]@{",
        "            ProfileName = ($_.Group -replace '^NetFence:', '')",
        "            DisplayName = $_.DisplayName",
        "            Direction = [string]$_.Direction",
        "            Enabled = [string]$_.Enabled",
        "            Action = [string]$_.Action",
        "            Program = [string]$app.Program",
        "            RemoteAddress = [string]($addr.RemoteAddress -join ',')",
        "        }",
        "    } |",
        "    ConvertTo-Csv -NoTypeInformation");

    private static IReadOnlyList<FirewallRuleInfo> ParseStatusOutput(string output) =>
        LiveSystemInfo.ParseCsv(output)
            .Select(row => new FirewallRuleInfo(
                row.GetValueOrDefault("ProfileName") ?? "",
                row.GetValueOrDefault("DisplayName") ?? "",
                row.GetValueOrDefault("Direction") ?? "",
                string.Equals(row.GetValueOrDefault("Enabled"), "True", StringComparison.OrdinalIgnoreCase),
                row.GetValueOrDefault("Action") ?? "",
                row.GetValueOrDefault("Program") ?? "",
                row.GetValueOrDefault("RemoteAddress") ?? ""))
            .ToArray();

    public static async Task<FirewallOperationResult> BlockAsync(
        string path, string? name, bool includeLinked,
        IEnumerable<string> additionalTargets, CancellationToken cancel = default)
    {
        return await PowerShellRunner.QueueFirewallOp(async () =>
        {
            var profileName = NetFenceRules.GetProfileName(path, name);
            var targets = new HashSet<string>(
                NetFenceTargets.GetPlannedBlockTargets(path, additionalTargets),
                StringComparer.OrdinalIgnoreCase);

            if (includeLinked)
            {
                var rows = LiveSystemInfo.GetProcessRows();
                var networkIds = LiveSystemInfo.GetNetworkProcessIds();
                foreach (var c in RelatedProcessScanner.GetRelatedCandidates(path, rows, networkIds))
                { if (c.Selected) targets.Add(c.Program); }
            }

            var result = await ApplyBatchBlockRules(profileName, targets, cancel);
            OperationLog.Write(OperationLog.DefaultPath, "Block",
                $"Blocked profile '{profileName}': {result.SuccessCount} success, {result.FailureCount} failed.", targets);
            OperationHistoryStore.Record("Block", profileName, result.SuccessCount);
            NetworkMonitor.InvalidateBlockedCache();
            return result;
        });
    }

    // Sync wrapper for legacy callers (ProcessWatcher auto-block)
    public static (string ProfileName, IReadOnlyList<string> Targets) Block(
        string path, string? name, bool includeLinked,
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
            foreach (var c in RelatedProcessScanner.GetRelatedCandidates(path, rows, networkIds))
            { if (c.Selected) targets.Add(c.Program); }
        }

        var group = NetFenceRules.GetRuleGroup(profileName);
        var result = ApplyBatchBlockRulesSync(profileName, targets);
        OperationLog.Write(OperationLog.DefaultPath, "Block",
            $"Blocked profile '{profileName}': {result.SuccessCount} success, {result.FailureCount} failed.", targets);
        OperationHistoryStore.Record("Block", profileName, result.SuccessCount);
        NetworkMonitor.InvalidateBlockedCache();
        return (profileName, targets.ToArray());
    }

    public static async Task<int> UnblockAsync(string path, string? name, CancellationToken cancel = default)
    {
        return await PowerShellRunner.QueueFirewallOp(async () =>
        {
            var profileName = NetFenceRules.GetProfileName(path, name);
            var group = NetFenceRules.GetRuleGroup(profileName);
            var script = string.Join(Environment.NewLine,
                "$ErrorActionPreference = 'Continue'",
                $"$rules = @(Get-NetFirewallRule -Group {PowerShellRunner.Quote(group)} -ErrorAction SilentlyContinue)",
                "if ($rules.Count -gt 0) { $rules | Remove-NetFirewallRule -ErrorAction SilentlyContinue }",
                "$rules.Count");
            var removed = ParseSingleInt(await PowerShellRunner.RunRequiredAsync(script, WriteTimeout, cancel));
            OperationLog.Write(OperationLog.DefaultPath, "Unblock", $"Unblocked profile '{profileName}': {removed} rules.", []);
            OperationHistoryStore.Record("Unblock", profileName, removed);
            NetworkMonitor.InvalidateBlockedCache();
            return removed;
        });
    }

    // Sync wrapper
    public static (string ProfileName, int RemovedRuleCount) Unblock(string path, string? name)
    {
        var profileName = NetFenceRules.GetProfileName(path, name);
        var group = NetFenceRules.GetRuleGroup(profileName);
        var script = string.Join(Environment.NewLine,
            "$ErrorActionPreference = 'Continue'",
            $"$rules = @(Get-NetFirewallRule -Group {PowerShellRunner.Quote(group)} -ErrorAction SilentlyContinue)",
            "if ($rules.Count -gt 0) { $rules | Remove-NetFirewallRule -ErrorAction SilentlyContinue }",
            "$rules.Count");
        var removed = ParseSingleInt(PowerShellRunner.RunRequired(script));
        OperationLog.Write(OperationLog.DefaultPath, "Unblock", $"Unblocked profile '{profileName}': {removed} rules.", []);
        OperationHistoryStore.Record("Unblock", profileName, removed);
        NetworkMonitor.InvalidateBlockedCache();
        return (profileName, removed);
    }

    public static IReadOnlyList<FirewallProgramTarget> GetSelectedProgramUnblockTargets(
        IEnumerable<FirewallRuleInfo> selectedRules) =>
        selectedRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.ProfileName) && !string.IsNullOrWhiteSpace(rule.Program))
            .Where(rule => Path.IsPathFullyQualified(rule.Program))
            .Select(rule => new FirewallProgramTarget(rule.ProfileName, Path.GetFullPath(rule.Program)))
            .Distinct()
            .OrderBy(rule => rule.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.Program, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static async Task<int> UnblockSelectedProgramsAsync(
        IEnumerable<FirewallRuleInfo> selectedRules, CancellationToken cancel = default)
    {
        return await PowerShellRunner.QueueFirewallOp(async () =>
        {
            var targets = GetSelectedProgramUnblockTargets(selectedRules);
            if (targets.Count == 0)
                throw new InvalidOperationException("Select one or more NetFence firewall rules first.");

            var scriptLines = new List<string> { "$ErrorActionPreference = 'Continue'", "$removed = 0" };
            foreach (var g in targets.GroupBy(t => t.ProfileName, StringComparer.OrdinalIgnoreCase))
            {
                var vn = "$tp" + (StringComparer.OrdinalIgnoreCase.GetHashCode(g.Key) & 0x7FFFFFFF);
                scriptLines.Add(string.Join(Environment.NewLine,
                    $"{vn} = @{{}}",
                    string.Join(Environment.NewLine, g.Select(t => $"{vn}[{PowerShellRunner.Quote(t.Program)}] = $true")),
                    $"$rules = @(Get-NetFirewallRule -Group {PowerShellRunner.Quote(NetFenceRules.GetRuleGroup(g.Key))} -ErrorAction SilentlyContinue)",
                    "foreach ($rule in $rules) {",
                    "    $app = $rule | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue",
                    $"    if ({vn}.ContainsKey([string]$app.Program)) {{",
                    "        $rule | Remove-NetFirewallRule -ErrorAction SilentlyContinue",
                    "        $removed += 1", "    }", "}"));
            }
            scriptLines.Add("$removed");
            var removed = ParseSingleInt(await PowerShellRunner.RunRequiredAsync(
                string.Join(Environment.NewLine, scriptLines), WriteTimeout, cancel));
            OperationLog.Write(OperationLog.DefaultPath, "UnblockSelected",
                $"Removed {removed} rule(s) for {targets.Count} selected files.", targets.Select(t => t.Program));
            OperationHistoryStore.Record("UnblockSelected", "selected", targets.Count);
            NetworkMonitor.InvalidateBlockedCache();
            return removed;
        });
    }

    // Sync wrapper
    public static int UnblockSelectedPrograms(IEnumerable<FirewallRuleInfo> selectedRules)
    {
        var targets = GetSelectedProgramUnblockTargets(selectedRules);
        if (targets.Count == 0)
            throw new InvalidOperationException("Select one or more NetFence firewall rules first.");

        var scriptLines = new List<string> { "$ErrorActionPreference = 'Continue'", "$removed = 0" };
        foreach (var g in targets.GroupBy(t => t.ProfileName, StringComparer.OrdinalIgnoreCase))
        {
            var vn = "$tp" + (StringComparer.OrdinalIgnoreCase.GetHashCode(g.Key) & 0x7FFFFFFF);
            scriptLines.Add(string.Join(Environment.NewLine,
                $"{vn} = @{{}}",
                string.Join(Environment.NewLine, g.Select(t => $"{vn}[{PowerShellRunner.Quote(t.Program)}] = $true")),
                $"$rules = @(Get-NetFirewallRule -Group {PowerShellRunner.Quote(NetFenceRules.GetRuleGroup(g.Key))} -ErrorAction SilentlyContinue)",
                "foreach ($rule in $rules) {",
                "    $app = $rule | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue",
                $"    if ({vn}.ContainsKey([string]$app.Program)) {{",
                "        $rule | Remove-NetFirewallRule -ErrorAction SilentlyContinue",
                "        $removed += 1", "    }", "}"));
        }
        scriptLines.Add("$removed");
        var removed = ParseSingleInt(PowerShellRunner.RunRequired(string.Join(Environment.NewLine, scriptLines)));
        OperationLog.Write(OperationLog.DefaultPath, "UnblockSelected",
            $"Removed {removed} rule(s) for {targets.Count} selected files.", targets.Select(t => t.Program));
        OperationHistoryStore.Record("UnblockSelected", "selected", targets.Count);
        NetworkMonitor.InvalidateBlockedCache();
        return removed;
    }

    public static async Task<int> UnblockAllAsync(CancellationToken cancel = default)
    {
        return await PowerShellRunner.QueueFirewallOp(async () =>
        {
            var script = string.Join(Environment.NewLine,
                "$ErrorActionPreference = 'Continue'",
                "$rules = @(Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.Group -like 'NetFence:*' })",
                "if ($rules.Count -gt 0) { $rules | Remove-NetFirewallRule -ErrorAction SilentlyContinue }",
                "$rules.Count");
            var removed = ParseSingleInt(await PowerShellRunner.RunRequiredAsync(script, WriteTimeout, cancel));
            OperationLog.Write(OperationLog.DefaultPath, "UnblockAll", $"Removed all {removed} NetFence rules.", []);
            OperationHistoryStore.Record("UnblockAll", "all", removed);
            NetworkMonitor.InvalidateBlockedCache();
            return removed;
        });
    }

    // Sync wrapper
    public static int UnblockAll()
    {
        var script = string.Join(Environment.NewLine,
            "$ErrorActionPreference = 'Continue'",
            "$rules = @(Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.Group -like 'NetFence:*' })",
            "if ($rules.Count -gt 0) { $rules | Remove-NetFirewallRule -ErrorAction SilentlyContinue }",
            "$rules.Count");
        var removed = ParseSingleInt(PowerShellRunner.RunRequired(script));
        OperationLog.Write(OperationLog.DefaultPath, "UnblockAll", $"Removed all {removed} NetFence rules.", []);
        OperationHistoryStore.Record("UnblockAll", "all", removed);
        NetworkMonitor.InvalidateBlockedCache();
        return removed;
    }

    private static async Task<FirewallOperationResult> ApplyBatchBlockRules(
        string profileName, HashSet<string> targets, CancellationToken cancel)
    {
        var group = NetFenceRules.GetRuleGroup(profileName);
        var ordered = targets.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

        // Build PS script with per-rule try/catch for partial success
        var script = new List<string> {
            "$ErrorActionPreference = 'Continue'",
            "$success = 0; $failure = 0",
            $"$group = {PowerShellRunner.Quote(group)}",
            "$existingNames = @{}",
            "Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | ForEach-Object { $existingNames[$_.DisplayName] = $true }"
        };

        foreach (var target in ordered)
        {
            foreach (var direction in new[] { FirewallDirection.Outbound, FirewallDirection.Inbound })
            {
                var dn = NetFenceRules.GetRuleName(profileName, target, direction);
                var qn = PowerShellRunner.Quote(dn);
                var qt = PowerShellRunner.Quote(target);
                script.Add(
                    $"try {{ " +
                    $"if ($existingNames.ContainsKey({qn})) {{ " +
                    $"Set-NetFirewallRule -DisplayName {qn} -Enabled True -Action Block -Direction {direction} -Profile Any -ErrorAction Stop | Out-Null }} " +
                    $"else {{ New-NetFirewallRule -DisplayName {qn} -Group $group -Direction {direction} -Action Block -Program {qt} -Profile Any -Enabled True -ErrorAction Stop | Out-Null }}; " +
                    $"$success++ }} catch {{ $failure++; [Console]::Error.WriteLine(\"FAIL|{target}|{direction}|$($_.Exception.Message)\") }}");
            }
        }

        script.Add("[PSCustomObject]@{ SuccessCount = $success; FailureCount = $failure } | ConvertTo-Csv -NoTypeInformation");
        var output = await PowerShellRunner.RunRequiredAsync(string.Join(Environment.NewLine, script), WriteTimeout, cancel);
        var rows = LiveSystemInfo.ParseCsv(output);
        var sc = int.TryParse(rows.FirstOrDefault()?.GetValueOrDefault("SuccessCount"), out var s) ? s : 0;
        var fc = int.TryParse(rows.FirstOrDefault()?.GetValueOrDefault("FailureCount"), out var f) ? f : 0;

        return new FirewallOperationResult(profileName, sc, fc, new List<string>());
    }

    private static FirewallOperationResult ApplyBatchBlockRulesSync(
        string profileName, HashSet<string> targets)
    {
        var pn = profileName;
        var group = NetFenceRules.GetRuleGroup(pn);
        var ordered = targets.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        var sl = new List<string> {
            "$ErrorActionPreference = 'Continue'",
            "$success = 0; $failure = 0",
            $"$group = {PowerShellRunner.Quote(group)}",
            "$existing = @{}",
            "Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | ForEach-Object { $existing[$_.DisplayName] = $true }"
        };
        foreach (var t in ordered)
        {
            foreach (var d in new[] { FirewallDirection.Outbound, FirewallDirection.Inbound })
            {
                var dn = NetFenceRules.GetRuleName(pn, t, d);
                var qn = PowerShellRunner.Quote(dn);
                var qt = PowerShellRunner.Quote(t);
                sl.Add($"try {{ " +
                    $"if ($existing.ContainsKey({qn})) {{ Set-NetFirewallRule -DisplayName {qn} -Enabled True -Action Block -Direction {d} -Profile Any -ErrorAction Stop | Out-Null }} " +
                    $"else {{ New-NetFirewallRule -DisplayName {qn} -Group $group -Direction {d} -Action Block -Program {qt} -Profile Any -Enabled True -ErrorAction Stop | Out-Null }}; " +
                    $"$success++ }} catch {{ $failure++; [Console]::Error.WriteLine(\"FAIL|{t}|{d}|$($_.Exception.Message)\") }}");
            }
        }
        sl.Add("[PSCustomObject]@{ SuccessCount = $success; FailureCount = $failure } | ConvertTo-Csv -NoTypeInformation");
        var output = PowerShellRunner.RunRequired(string.Join(Environment.NewLine, sl));
        var rows = LiveSystemInfo.ParseCsv(output);
        var sc = int.TryParse(rows.FirstOrDefault()?.GetValueOrDefault("SuccessCount"), out var s2) ? s2 : 0;
        var fc = int.TryParse(rows.FirstOrDefault()?.GetValueOrDefault("FailureCount"), out var f2) ? f2 : 0;
        return new FirewallOperationResult(pn, sc, fc, new List<string>());
    }

    private static int ParseSingleInt(string output)
    {
        var value = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }
}
