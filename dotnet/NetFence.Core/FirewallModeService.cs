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

        PowerShellRunner.RunRequired(string.Join(Environment.NewLine,
            "$ErrorActionPreference = 'Stop'",
            $"$rules = @(Get-NetFirewallRule -Group {PowerShellRunner.Quote(group)} -ErrorAction SilentlyContinue)",
            "if ($rules.Count -gt 0) { $rules | Remove-NetFirewallRule -ErrorAction SilentlyContinue }"));

        if (mode == NetworkMode.AllowAll) return;

        switch (mode)
        {
            case NetworkMode.BlockAll:
                BatchCreateRules(targets, group, profileName,
                    new[] { FirewallDirection.Inbound, FirewallDirection.Outbound }, "Block", null);
                break;

            case NetworkMode.LanOnly:
                // Block Outbound to Internet (allows Intranet: 10/8, 172.16/12, 192.168/16, plus localhost)
                BatchCreateRules(targets, group, profileName,
                    new[] { FirewallDirection.Outbound }, "Block", "Internet");
                // Block Inbound from everywhere
                BatchCreateRules(targets, group, profileName,
                    new[] { FirewallDirection.Inbound }, "Block", null);
                break;

            case NetworkMode.Custom:
                var ips = allowedIps.Where(ip => !string.IsNullOrWhiteSpace(ip)).ToList();
                if (ips.Count > 0)
                {
                    var ipList = string.Join(",", ips);
                    BatchCreateRules(targets, group, profileName,
                        new[] { FirewallDirection.Outbound }, "Allow", ipList);
                }
                // Block Inbound from everywhere
                BatchCreateRules(targets, group, profileName,
                    new[] { FirewallDirection.Inbound }, "Block", null);
                // Note: Outbound Block is NOT created in Custom mode because Windows Firewall
                // gives Block precedence over Allow. Programs can still reach non-allowed
                // destinations via outbound connections. This is a Windows Firewall limitation.
                break;
        }
    }

    private static void BatchCreateRules(IEnumerable<string> targets, string group,
        string profileName, FirewallDirection[] directions, string action, string? remoteAddress)
    {
        var scriptLines = new List<string> { "$ErrorActionPreference = 'Stop'" };
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
        PowerShellRunner.RunRequired(string.Join(Environment.NewLine, scriptLines));
    }
}
