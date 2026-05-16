using System.ServiceProcess;
using Microsoft.Win32;

namespace NetFence.Core;

public static class ServiceScanner
{
    private static readonly HashSet<string> SystemServices = new(StringComparer.OrdinalIgnoreCase)
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
        var results = new List<ServiceInfo>();

        foreach (var svc in ServiceController.GetServices())
        {
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
                    (svc.ServiceName.Contains(Path.GetFileName(targetPath), StringComparison.OrdinalIgnoreCase) ||
                     svc.DisplayName.Contains(Path.GetFileName(installDir), StringComparison.OrdinalIgnoreCase)))
                    isRelated = true;

                if (!isRelated) continue;

                var exeName = exePath is not null ? Path.GetFileName(exePath) : null;
                var isSystem = exeName is not null && SystemServices.Contains(exeName);

                results.Add(new ServiceInfo(
                    svc.ServiceName, svc.DisplayName,
                    svc.Status.ToString(), svc.StartType.ToString(),
                    exePath, isSystem));
            }
            catch { }
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
        var escapedDir = installDir.Replace("'", "''");

        var script = $$"""
            $ErrorActionPreference = 'Stop'
            Get-ScheduledTask -ErrorAction SilentlyContinue |
                ForEach-Object {
                    $task = $_
                    $actions = @($task.Actions | ForEach-Object { $_.Execute + ' ' + ($_.Arguments ?? '') })
                    $actionText = $actions -join '; '
                    $triggers = @($task.Triggers | ForEach-Object { $_.CimClass.CimClassName } | Select-Object -Unique)
                    $triggerText = $triggers -join ', '
                    [PSCustomObject]@{
                        Name = $task.TaskName
                        Path = $task.TaskPath
                        State = [string]$task.State
                        Triggers = $triggerText
                        Actions = $actionText
                        ExecutablePath = [string]$task.Actions[0].Execute
                    }
                } |
                Where-Object {
                    $_.ExecutablePath -like '*{{escapedDir}}*' -or
                    $_.Actions -like '*updater*' -or
                    $_.Actions -like '*update*' -or
                    $_.Actions -like '*helper*' -or
                    $_.Actions -like '*launcher*'
                } |
                ConvertTo-Csv -NoTypeInformation
            """;

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

    public static void DisableScheduledTask(string taskPath)
    {
        PowerShellRunner.RunRequired(
            $"Disable-ScheduledTask -TaskPath {PowerShellRunner.Quote(taskPath)}");
    }

    private static string? EmptyToNull(string? v) => string.IsNullOrWhiteSpace(v) ? null : v;
}
