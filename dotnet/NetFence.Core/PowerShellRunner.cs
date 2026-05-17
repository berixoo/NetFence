using System.Diagnostics;
using System.Text;

namespace NetFence.Core;

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

public static class PowerShellRunner
{
    public static CommandResult Run(string script)
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

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start powershell.exe.");
            // Read stdout and stderr concurrently to avoid pipe-buffer deadlock
            var outputTcs = new TaskCompletionSource<string>();
            var errorTcs = new TaskCompletionSource<string>();
            ThreadPool.QueueUserWorkItem(_ => outputTcs.TrySetResult(process.StandardOutput.ReadToEnd()));
            ThreadPool.QueueUserWorkItem(_ => errorTcs.TrySetResult(process.StandardError.ReadToEnd()));
            process.WaitForExit();
            var output = outputTcs.Task.GetAwaiter().GetResult();
            var error = errorTcs.Task.GetAwaiter().GetResult();
            return new CommandResult(process.ExitCode, output, error);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    public static string RunRequired(string script)
    {
        var result = Run(script);
        if (result.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            throw new InvalidOperationException(message.Trim());
        }

        return result.StandardOutput;
    }

    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

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
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary script cleanup failure should not hide the command result.
        }
    }

    private static string WrapScript(string script) => string.Join(Environment.NewLine,
        "$netFenceUtf8 = New-Object System.Text.UTF8Encoding -ArgumentList $false",
        "[Console]::OutputEncoding = $netFenceUtf8",
        "[Console]::InputEncoding = $netFenceUtf8",
        "$OutputEncoding = $netFenceUtf8",
        script);
}
