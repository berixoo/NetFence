using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NetFence.Core;

public static class NetFenceRules
{
    public static string GetProfileName(string path, string? name = null)
    {
        var source = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(path)
            : name.Trim();

        if (string.IsNullOrWhiteSpace(source))
        {
            source = "Profile";
        }

        var sanitized = Regex.Replace(source, @"[^\p{L}\p{Nd}._-]+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Profile";
        }

        return sanitized.Length > 40 ? sanitized[..40] : sanitized;
    }

    public static string GetRuleName(string profileName, string programPath, FirewallDirection direction)
    {
        var hash = ShortHash($"{profileName}|{direction}|{programPath}");
        return $"NetFence {profileName} {direction} {hash}";
    }

    public static string GetRuleGroup(string profileName) => $"NetFence:{profileName}";

    public static bool IsManagedGroup(string? group) =>
        !string.IsNullOrWhiteSpace(group) &&
        group.StartsWith("NetFence:", StringComparison.OrdinalIgnoreCase);

    public static bool IsProtectedSystemPath(string path)
    {
        var windows = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path);
        return candidate.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPathUnderDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}
