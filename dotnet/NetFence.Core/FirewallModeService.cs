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
        // Remove existing rules
        PowerShellRunner.RunRequired(string.Join(Environment.NewLine,
            "$ErrorActionPreference = 'Stop'",
            $"$rules = @(Get-NetFirewallRule -Group {PowerShellRunner.Quote(group)} -ErrorAction SilentlyContinue)",
            "if ($rules.Count -gt 0) { $rules | Remove-NetFirewallRule -ErrorAction SilentlyContinue }"));

        if (mode == NetworkMode.AllowAll) return;

        // Block rules for all targets
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

        // Allow rules for LAN or custom
        if (mode is NetworkMode.LanOnly or NetworkMode.Custom)
        {
            var remoteIps = mode == NetworkMode.LanOnly
                ? LanRanges
                : allowedIps.Where(ip => !string.IsNullOrWhiteSpace(ip)).ToList();

            if (remoteIps.Count == 0) return;

            var ipList = string.Join(",", remoteIps);
            foreach (var target in targets)
            {
                var allowHash = NetFenceRules.GetRuleName(profileName, target + "_allow",
                    FirewallDirection.Outbound);
                PowerShellRunner.RunRequired(string.Join(Environment.NewLine,
                    "$ErrorActionPreference = 'Stop'",
                    $"New-NetFirewallRule -DisplayName {PowerShellRunner.Quote(allowHash)} " +
                    $"-Group {PowerShellRunner.Quote(group)} " +
                    "-Direction Outbound -Action Allow " +
                    $"-Program {PowerShellRunner.Quote(target)} " +
                    $"-RemoteAddress {PowerShellRunner.Quote(ipList)} " +
                    "-Profile Any -Enabled True | Out-Null"));
            }
        }
    }
}
