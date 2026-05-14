using System.Text;

namespace NetFence.Core;

public static class OperationLog
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetFence");

    public static string DefaultPath => Path.Combine(DataDirectory, "NetFence.log");

    public static void Write(string logPath, string action, string message, IEnumerable<string>? items)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(logPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lines = new List<string>
        {
            $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {action} - {message}"
        };
        lines.AddRange((items ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => $"  - {item}"));

        File.AppendAllLines(logPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
