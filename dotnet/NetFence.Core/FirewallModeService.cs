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
        ApplyModeCoreSync(profileName, targets, mode, allowedIps);
    }

    private static void ApplyModeCoreSync(string profileName, IEnumerable<string> targets,
        NetworkMode mode, IReadOnlyList<string> allowedIps)
    {
        var group = NetFenceRules.GetRuleGroup(profileName);
        PowerShellRunner.RunRequired(string.Join(Environment.NewLine,
            "$ErrorActionPreference = 'Continue'",
            $"$rules = @(Get-NetFirewallRule -Group {PowerShellRunner.Quote(group)} -ErrorAction SilentlyContinue)",
            "if ($rules.Count -gt 0) { $rules | Remove-NetFirewallRule -ErrorAction SilentlyContinue }"));
        if (mode == NetworkMode.AllowAll) return;
        ApplyModeActions(targets, group, profileName, mode, allowedIps,
            script => PowerShellRunner.RunRequired(script));
    }

    public static async Task ApplyModeAsync(string profileName, IEnumerable<string> targets,
        NetworkMode mode, IReadOnlyList<string> allowedIps,
        CancellationToken cancel = default)
    {
        await PowerShellRunner.QueueFirewallOp(async () =>
        {
            await ApplyModeCoreAsync(profileName, targets, mode, allowedIps,
                script => PowerShellRunner.RunRequiredAsync(script, TimeSpan.FromSeconds(120), cancel));
        }, cancel);
    }

    private static async Task ApplyModeCoreAsync(string profileName, IEnumerable<string> targets,
        NetworkMode mode, IReadOnlyList<string> allowedIps,
        Func<string, Task<string>> runScript)
    {
        var group = NetFenceRules.GetRuleGroup(profileName);
        await runScript(string.Join(Environment.NewLine,
            "$ErrorActionPreference = 'Continue'",
            $"$rules = @(Get-NetFirewallRule -Group {PowerShellRunner.Quote(group)} -ErrorAction SilentlyContinue)",
            "if ($rules.Count -gt 0) { $rules | Remove-NetFirewallRule -ErrorAction SilentlyContinue }"));
        if (mode == NetworkMode.AllowAll) return;
        await ApplyModeActionsAsync(targets, group, profileName, mode, allowedIps, runScript);
    }

    private static void ApplyModeActions(IEnumerable<string> targets, string group,
        string profileName, NetworkMode mode, IReadOnlyList<string> allowedIps,
        Func<string, string> runSync)
    {
        var asyncRun = new Func<string, Task<string>>(s => Task.FromResult(runSync(s)));
        ApplyModeActionsAsync(targets, group, profileName, mode, allowedIps, asyncRun).GetAwaiter().GetResult();
    }

    private static async Task ApplyModeActionsAsync(IEnumerable<string> targets, string group,
        string profileName, NetworkMode mode, IReadOnlyList<string> allowedIps,
        Func<string, Task<string>> runScript)
    {
        switch (mode)
        {
            case NetworkMode.BlockAll:
                await BatchCreateRulesCore(targets, group, profileName,
                    new[] { FirewallDirection.Inbound, FirewallDirection.Outbound }, "Block", null, runScript);
                break;
            case NetworkMode.LanOnly:
                await BatchCreateRulesCore(targets, group, profileName,
                    new[] { FirewallDirection.Outbound }, "Block", "Internet", runScript);
                await BatchCreateRulesCore(targets, group, profileName,
                    new[] { FirewallDirection.Inbound }, "Block", null, runScript);
                break;
            case NetworkMode.Custom:
                var ips = allowedIps.Where(ip => !string.IsNullOrWhiteSpace(ip)).ToList();
                if (ips.Count > 0)
                    await BatchCreateRulesCore(targets, group, profileName,
                        new[] { FirewallDirection.Outbound }, "Allow", string.Join(",", ips), runScript);
                await BatchCreateRulesCore(targets, group, profileName,
                    new[] { FirewallDirection.Inbound }, "Block", null, runScript);
                break;
        }
    }

    private static async Task BatchCreateRulesCore(IEnumerable<string> targets, string group,
        string profileName, FirewallDirection[] directions, string action, string? remoteAddress,
        Func<string, Task<string>> runScript)
    {
        var scriptLines = new List<string> { "$ErrorActionPreference = 'Continue'" };
        foreach (var target in targets)
        {
            foreach (var direction in directions)
            {
                var suffix = remoteAddress is not null ? $"_{action}_{remoteAddress.Replace(",", "_")}" : "";
                var displayName = NetFenceRules.GetRuleName(profileName, target + suffix, direction);
                var remotePart = remoteAddress is not null
                    ? $"-RemoteAddress {PowerShellRunner.Quote(remoteAddress)} "
                    : "";
                scriptLines.Add(
                    $"New-NetFirewallRule -DisplayName {PowerShellRunner.Quote(displayName)} " +
                    $"-Group {PowerShellRunner.Quote(group)} " +
                    $"-Direction {direction} -Action {action} " +
                    $"-Program {PowerShellRunner.Quote(target)} " +
                    remotePart +
                    "-Profile Any -Enabled True | Out-Null");
            }
        }
        await runScript(string.Join(Environment.NewLine, scriptLines));
    }
}
