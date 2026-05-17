using System.Diagnostics;
using System.Text;

namespace NetFence.Core;

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError,
    bool TimedOut = false, bool Canceled = false);

public static class PowerShellRunner
{
    private static readonly SemaphoreSlim GlobalLock = new(1, 1);

    public static CommandResult Run(string script)
    {
        return RunInternal(script, Timeout.InfiniteTimeSpan, CancellationToken.None);
    }

    public static async Task<CommandResult> RunAsync(string script,
        TimeSpan timeout, CancellationToken cancel = default)
    {
        return await Task.Run(() => RunInternal(script, timeout, cancel), cancel);
    }

    public static string RunRequired(string script)
    {
        var result = Run(script);
        ThrowIfFailed(result);
        return result.StandardOutput;
    }

    public static async Task<string> RunRequiredAsync(string script,
        TimeSpan timeout, CancellationToken cancel = default)
    {
        var result = await RunAsync(script, timeout, cancel);
        ThrowIfFailed(result);
        return result.StandardOutput;
    }

    /// <summary>Serialise all firewall-modifying operations to prevent PowerShell flood.</summary>
    public static async Task<T> QueueFirewallOp<T>(Func<Task<T>> operation, CancellationToken cancel = default)
    {
        await GlobalLock.WaitAsync(cancel);
        try { return await operation(); }
        finally { GlobalLock.Release(); }
    }

    public static async Task QueueFirewallOp(Func<Task> operation, CancellationToken cancel = default) =>
        await QueueFirewallOp<object?>(async () => { await operation(); return null; }, cancel);

    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static void ThrowIfFailed(CommandResult result)
    {
        if (result.TimedOut) throw new TimeoutException("PowerShell command timed out.");
        if (result.Canceled) throw new OperationCanceledException("PowerShell command was canceled.");
        if (result.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput : result.StandardError;
            throw new InvalidOperationException(message.Trim());
        }
    }

    private static CommandResult RunInternal(string script, TimeSpan timeout, CancellationToken cancel)
    {
        var scriptPath = WriteTemporaryScript(script);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetWindowsPowerShellPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start powershell.exe.");

            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
                { if (e.Data is not null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) =>
                { if (e.Data is not null) error.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool timedOut = false;
            bool canceled = false;

            if (timeout == Timeout.InfiniteTimeSpan && cancel == CancellationToken.None)
            {
                process.WaitForExit();
            }
            else
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                cts.CancelAfter(timeout);
                try { process.WaitForExitAsync(cts.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException)
                {
                    canceled = cancel.IsCancellationRequested;
                    timedOut = !canceled;
                    try { process.Kill(entireProcessTree: true); } catch { }
                    // Bounded wait after kill — 5 seconds max
                    var exited = false;
                    try { exited = process.WaitForExit(5000); } catch { }
                    if (!exited) { /* process didn't die, return what we have */ }
                }
            }

            if (!timedOut && !canceled)
                process.WaitForExit();
            process.CancelOutputRead();
            process.CancelErrorRead();

            return new CommandResult(process.ExitCode, output.ToString(), error.ToString(), timedOut, canceled);
        }
        finally { TryDelete(scriptPath); }
    }

    private static string GetWindowsPowerShellPath()
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var fullPath = Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(fullPath) ? fullPath : "powershell.exe";
    }

    private static string WriteTemporaryScript(string script)
    {
        var directory = Path.Combine(Path.GetTempPath(), "NetFence");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.ps1");
        File.WriteAllText(path, WrapScript(script), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private static string WrapScript(string script) => string.Join(Environment.NewLine,
        "$netFenceUtf8 = New-Object System.Text.UTF8Encoding -ArgumentList $false",
        "[Console]::OutputEncoding = $netFenceUtf8",
        "[Console]::InputEncoding = $netFenceUtf8",
        "$OutputEncoding = $netFenceUtf8",
        script);
}
