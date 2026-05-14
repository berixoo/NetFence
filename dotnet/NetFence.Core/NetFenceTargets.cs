namespace NetFence.Core;

public static class NetFenceTargets
{
    public static IReadOnlyList<string> GetExecutableTargets(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException($"Target path does not exist: {path}", path);
        }

        if (NetFenceRules.IsProtectedSystemPath(fullPath))
        {
            throw new InvalidOperationException($"Target '{fullPath}' is under the Windows system directory.");
        }

        if (Directory.Exists(fullPath))
        {
            return Directory.EnumerateFiles(fullPath, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                })
                .Where(file => string.Equals(Path.GetExtension(file), ".exe", StringComparison.OrdinalIgnoreCase))
                .Where(file => !NetFenceRules.IsProtectedSystemPath(file))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Target '{fullPath}' is not an executable.");
        }

        return new[] { fullPath };
    }

    public static IReadOnlyList<string> GetPlannedBlockTargets(
        string path,
        IEnumerable<string>? additionalTargets = null)
    {
        var targets = new HashSet<string>(GetExecutableTargets(path), StringComparer.OrdinalIgnoreCase);
        foreach (var target in additionalTargets ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(target);
            if (!File.Exists(fullPath) ||
                !string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Additional target '{target}' is not an executable file.");
            }

            if (!NetFenceRules.IsProtectedSystemPath(fullPath))
            {
                targets.Add(fullPath);
            }
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException($"No executable files were found under '{Path.GetFullPath(path)}'.");
        }

        return targets.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
