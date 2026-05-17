using System.ServiceProcess;
using Microsoft.Win32;

namespace NetFence.Core;

public static class ServiceScanner
{
    private static readonly HashSet<string> SystemServiceExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost.exe", "lsass.exe", "wininit.exe", "services.exe",
        "csrss.exe", "smss.exe", "winlogon.exe", "explorer.exe",
        "RuntimeBroker.exe", "SearchHost.exe", "Registry", "spoolsv.exe",
        "WSearch", "MsMpEng.exe", "SecurityHealthService.exe", "SgrmBroker.exe"
    };

    public static IReadOnlyList<ServiceInfo> ScanServices(string targetPath)
    {
        if (!Directory.Exists(targetPath) && !File.Exists(targetPath))
            throw new InvalidOperationException($"Path not found: {targetPath}");

        var installDir = Directory.Exists(targetPath) ? targetPath : Path.GetDirectoryName(targetPath)!;
        var targetName = Path.GetFileName(targetPath);
        var results = new List<ServiceInfo>();

        foreach (var svc in ServiceController.GetServices())
        {
            using (svc)
            {
            // Skip kernel drivers, file-system drivers, adapters
            if (svc.ServiceType != ServiceType.Win32OwnProcess &&
                svc.ServiceType != ServiceType.Win32ShareProcess)
                continue;

            try
            {
                var exePath = GetServiceImagePath(svc.ServiceName);
                var isRelated = false;

                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    if (NetFenceRules.IsPathUnderDirectory(exePath, installDir))
                        isRelated = true;
                }

                if (!isRelated &&
                    (svc.ServiceName.Contains(targetName, StringComparison.OrdinalIgnoreCase) ||
                     svc.DisplayName.Contains(Path.GetFileName(installDir), StringComparison.OrdinalIgnoreCase)))
                    isRelated = true;

                if (!isRelated) continue;

                var isSystem = DetermineIsSystem(svc, exePath);

                results.Add(new ServiceInfo(
                    svc.ServiceName, svc.DisplayName,
                    svc.Status.ToString(), GetEffectiveStartMode(svc),
                    exePath, isSystem));
            }
            catch (Exception) { /* skip unreadable service */ }
            }
        }

        return results.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void StopService(string serviceName)
    {
        using var svc = new ServiceController(serviceName);
        if (svc.Status != ServiceControllerStatus.Running) return;
        svc.Stop();
        svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
    }

    public static void DisableService(string serviceName)
    {
        PowerShellRunner.RunRequired(
            $"Set-Service -Name {PowerShellRunner.Quote(serviceName)} -StartupType Disabled");
    }

    private static bool DetermineIsSystem(ServiceController svc, string? exePath)
    {
        // Check by known system executable names
        var exeName = exePath is not null ? Path.GetFileName(exePath) : null;
        if (exeName is not null && SystemServiceExes.Contains(exeName))
            return true;
        // If we couldn't read the image path due to access denial, treat as system
        if (exePath is null && exeName is null)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @$"SYSTEM\CurrentControlSet\Services\{svc.ServiceName}");
                if (key is null) return false;
                var imagePath = key.GetValue("ImagePath") as string;
                // Key exists but value access denied — likely protected
                return imagePath is null;
            }
            catch { return true; }
        }
        return false;
    }

    private static string GetEffectiveStartMode(ServiceController svc)
    {
        var mode = svc.StartType.ToString();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @$"SYSTEM\CurrentControlSet\Services\{svc.ServiceName}");
            if (key?.GetValue("DelayedAutoStart") is int delayed && delayed == 1)
                mode += " (Delayed)";
        }
        catch { }
        return mode;
    }

    private static string? GetServiceImagePath(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @$"SYSTEM\CurrentControlSet\Services\{serviceName}");
            var imagePath = key?.GetValue("ImagePath") as string;
            if (string.IsNullOrWhiteSpace(imagePath)) return null;

            var path = imagePath.Trim('"').Trim();
            if (path.StartsWith(@"\??\")) path = path[4..];
            if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path);
            if (path.Contains("svchost.exe", StringComparison.OrdinalIgnoreCase))
            {
                var argsIdx = path.IndexOf(" -k ", StringComparison.OrdinalIgnoreCase);
                if (argsIdx > 0) path = path[..argsIdx].Trim();
            }
            return File.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch { return null; }
    }

    public static IReadOnlyList<ScheduledTaskInfo> ScanScheduledTasks(string targetPath)
    {
        if (!Directory.Exists(targetPath) && !File.Exists(targetPath))
            throw new InvalidOperationException($"Path not found: {targetPath}");

        var installDir = Directory.Exists(targetPath) ? targetPath : Path.GetDirectoryName(targetPath)!;
        var escapedDir = EscapePowerShellWildcards(installDir);

        var script = string.Join(Environment.NewLine,
            "$ErrorActionPreference = 'Stop'",
            "Get-ScheduledTask -ErrorAction SilentlyContinue |",
            "    ForEach-Object {",
            "        $task = $_",
            "        $actions = @($task.Actions | ForEach-Object { $_.Execute + ' ' + ($_.Arguments ?? '') })",
            "        $actionText = $actions -join '; '",
            "        $triggers = @($task.Triggers | ForEach-Object { $_.CimClass.CimClassName } | Select-Object -Unique)",
            "        $triggerText = $triggers -join ', '",
            "        [PSCustomObject]@{",
            "            Name = $task.TaskName",
            "            Path = $task.TaskPath",
            "            State = [string]$task.State",
            "            Triggers = $triggerText",
            "            Actions = $actionText",
            "            ExecutablePath = [string]$task.Actions[0].Execute",
            "        }",
            "    } |",
            $"    Where-Object {{ $_.ExecutablePath -like '*{escapedDir}*' }} |",
            "    ConvertTo-Csv -NoTypeInformation");

        return LiveSystemInfo.ParseCsv(PowerShellRunner.RunRequired(script))
            .Select(row => new ScheduledTaskInfo(
                row.GetValueOrDefault("Name") ?? "",
                row.GetValueOrDefault("Path") ?? "",
                row.GetValueOrDefault("State") ?? "",
                row.GetValueOrDefault("Triggers") ?? "",
                row.GetValueOrDefault("Actions") ?? "",
                EmptyToNull(row.GetValueOrDefault("ExecutablePath"))))
            .ToArray();
    }

    public static void DisableScheduledTask(string taskPath, string taskName)
    {
        PowerShellRunner.RunRequired(
            $"Disable-ScheduledTask -TaskPath {PowerShellRunner.Quote(taskPath)} -TaskName {PowerShellRunner.Quote(taskName)}");
    }

    private static string EscapePowerShellWildcards(string value)
    {
        return value
            .Replace("`", "``")
            .Replace("'", "''")
            .Replace("[", "`[")
            .Replace("]", "`]")
            .Replace("*", "`*")
            .Replace("?", "`?");
    }

    private static string? EmptyToNull(string? v) => string.IsNullOrWhiteSpace(v) ? null : v;
}
