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
        var group = NetFenceRules.GetRuleGroup(profileName);

        // Remove existing rules for this profile
        PowerShellRunner.RunRequired(string.Join(Environment.NewLine,
            "$ErrorActionPreference = 'Stop'",
            $"$rules = @(Get-NetFirewallRule -Group {PowerShellRunner.Quote(group)} -ErrorAction SilentlyContinue)",
            "if ($rules.Count -gt 0) { $rules | Remove-NetFirewallRule -ErrorAction SilentlyContinue }"));

        if (mode == NetworkMode.AllowAll) return;

        switch (mode)
        {
            case NetworkMode.BlockAll:
                CreateBlockRules(targets, group, profileName, blockInbound: true, blockOutbound: true);
                break;
            case NetworkMode.LanOnly:
                // Allow rules first (matched before Block in evaluation order)
                CreateAllowRules(targets, group, profileName, LanRanges);
                // Block only internet (Intranet = 10/8, 172.16/12, 192.168/16)
                CreateBlockRules(targets, group, profileName, blockInbound: true, blockOutbound: false);
                CreateInternetBlockRules(targets, group, profileName);
                break;
            case NetworkMode.Custom:
                var ips = allowedIps.Where(ip => !string.IsNullOrWhiteSpace(ip)).ToList();
                if (ips.Count > 0)
                {
                    // Allow rules must be created FIRST to match before Block
                    CreateAllowRules(targets, group, profileName, ips);
                }
                CreateBlockRules(targets, group, profileName, blockInbound: true, blockOutbound: true);
                break;
        }
    }

    private static void CreateBlockRules(IEnumerable<string> targets, string group,
        string profileName, bool blockInbound, bool blockOutbound)
    {
        var directions = new List<FirewallDirection>();
        if (blockInbound) directions.Add(FirewallDirection.Inbound);
        if (blockOutbound) directions.Add(FirewallDirection.Outbound);

        var scriptLines = new List<string> { "$ErrorActionPreference = 'Stop'" };
        foreach (var target in targets)
        {
            foreach (var direction in directions)
            {
                var displayName = NetFenceRules.GetRuleName(profileName, target, direction);
                scriptLines.Add(
                    $"New-NetFirewallRule -DisplayName {PowerShellRunner.Quote(displayName)} " +
                    $"-Group {PowerShellRunner.Quote(group)} " +
                    $"-Direction {direction} -Action Block " +
                    $"-Program {PowerShellRunner.Quote(target)} " +
                    "-Profile Any -Enabled True | Out-Null");
            }
        }
        PowerShellRunner.RunRequired(string.Join(Environment.NewLine, scriptLines));
    }

    private static void CreateInternetBlockRules(IEnumerable<string> targets, string group,
        string profileName)
    {
        // RemoteAddress "Internet" blocks public IPs but allows Intranet (10/8, 172.16/12, 192.168/16)
        var scriptLines = new List<string> { "$ErrorActionPreference = 'Stop'" };
        foreach (var target in targets)
        {
            var displayName = NetFenceRules.GetRuleName(profileName, target + "_inet",
                FirewallDirection.Outbound);
            scriptLines.Add(
                $"New-NetFirewallRule -DisplayName {PowerShellRunner.Quote(displayName)} " +
                $"-Group {PowerShellRunner.Quote(group)} " +
                "-Direction Outbound -Action Block " +
                $"-Program {PowerShellRunner.Quote(target)} " +
                "-RemoteAddress Internet " +
                "-Profile Any -Enabled True | Out-Null");
        }
        PowerShellRunner.RunRequired(string.Join(Environment.NewLine, scriptLines));
    }

    private static void CreateAllowRules(IEnumerable<string> targets, string group,
        string profileName, IReadOnlyList<string> remoteIps)
    {
        var ipList = string.Join(",", remoteIps);
        var scriptLines = new List<string> { "$ErrorActionPreference = 'Stop'" };
        foreach (var target in targets)
        {
            var allowDisplayName = NetFenceRules.GetRuleName(profileName, target + "_allow",
                FirewallDirection.Outbound);
            scriptLines.Add(
                $"New-NetFirewallRule -DisplayName {PowerShellRunner.Quote(allowDisplayName)} " +
                $"-Group {PowerShellRunner.Quote(group)} " +
                "-Direction Outbound -Action Allow " +
                $"-Program {PowerShellRunner.Quote(target)} " +
                $"-RemoteAddress {PowerShellRunner.Quote(ipList)} " +
                "-Profile Any -Enabled True | Out-Null");
        }
        PowerShellRunner.RunRequired(string.Join(Environment.NewLine, scriptLines));
    }
}
