namespace NetFence.Core;

public static class RelatedProcessScanner
{
    public static IReadOnlyList<RelatedCandidate> GetRelatedCandidates(
        string path,
        IEnumerable<ProcessRow> processRows,
        IEnumerable<int>? networkProcessIds = null)
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

        var rows = processRows.ToArray();
        var networkSet = new HashSet<int>(networkProcessIds ?? Array.Empty<int>());
        var installDirectory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath)!;
        var targetExecutables = new HashSet<string>(NetFenceTargets.GetExecutableTargets(fullPath), StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<string, CandidateBuilder>(StringComparer.OrdinalIgnoreCase);
        var rootProcessIds = new HashSet<int>();

        foreach (var target in targetExecutables)
        {
                Add(candidates, target, "selected target", 0, "", true);
        }

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ExecutablePath) ||
                !string.Equals(Path.GetExtension(row.ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
                NetFenceRules.IsProtectedSystemPath(row.ExecutablePath) ||
                !File.Exists(row.ExecutablePath))
            {
                continue;
            }

            var executable = Path.GetFullPath(row.ExecutablePath);
            if (targetExecutables.Contains(executable))
            {
                rootProcessIds.Add(row.ProcessId);
                Add(candidates, executable, "running target process", row.ProcessId, row.ProcessName, true);
            }

            if (NetFenceRules.IsPathUnderDirectory(executable, installDirectory))
            {
                Add(candidates, executable, "same install folder", row.ProcessId, row.ProcessName, true);
            }

            if (!string.IsNullOrWhiteSpace(row.CommandLine) &&
                (row.CommandLine.Contains(fullPath, StringComparison.OrdinalIgnoreCase) ||
                 row.CommandLine.Contains(installDirectory, StringComparison.OrdinalIgnoreCase)))
            {
                Add(candidates, executable, "command line references target", row.ProcessId, row.ProcessName, ShouldAutoSelect(executable, installDirectory));
            }
        }

        foreach (var childPath in GetLinkedProcessPaths(rootProcessIds, rows))
        {
            var row = rows.FirstOrDefault(item => string.Equals(item.ExecutablePath, childPath, StringComparison.OrdinalIgnoreCase));
            Add(candidates, childPath, "child process", row?.ProcessId ?? 0, row?.ProcessName ?? "", ShouldAutoSelect(childPath, installDirectory));
        }

        foreach (var row in rows)
        {
            if (networkSet.Contains(row.ProcessId) &&
                !string.IsNullOrWhiteSpace(row.ExecutablePath) &&
                candidates.ContainsKey(Path.GetFullPath(row.ExecutablePath)))
            {
                Add(candidates, row.ExecutablePath, "active network connection", row.ProcessId, row.ProcessName, ShouldAutoSelect(row.ExecutablePath, installDirectory));
            }
        }

        return candidates.Values
            .Select(builder => builder.ToCandidate())
            .OrderBy(candidate => candidate.Program, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> GetLinkedProcessPaths(HashSet<int> rootProcessIds, IReadOnlyList<ProcessRow> rows)
    {
        var byParent = rows.GroupBy(row => row.ParentProcessId).ToDictionary(group => group.Key, group => group.ToArray());
        var seen = new HashSet<int>(rootProcessIds);
        var queue = new Queue<int>(rootProcessIds);

        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!byParent.TryGetValue(parent, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (!seen.Add(child.ProcessId))
                {
                    continue;
                }
                queue.Enqueue(child.ProcessId);
                if (!string.IsNullOrWhiteSpace(child.ExecutablePath) &&
                    File.Exists(child.ExecutablePath) &&
                    !NetFenceRules.IsProtectedSystemPath(child.ExecutablePath))
                {
                    yield return Path.GetFullPath(child.ExecutablePath);
                }
            }
        }
    }

    private static void Add(
        Dictionary<string, CandidateBuilder> candidates,
        string program,
        string reason,
        int processId,
        string processName,
        bool selected)
    {
        if (string.IsNullOrWhiteSpace(program) ||
            !File.Exists(program) ||
            !string.Equals(Path.GetExtension(program), ".exe", StringComparison.OrdinalIgnoreCase) ||
            NetFenceRules.IsProtectedSystemPath(program))
        {
            return;
        }

        var fullPath = Path.GetFullPath(program);
        if (!candidates.TryGetValue(fullPath, out var builder))
        {
            builder = new CandidateBuilder(fullPath);
            candidates[fullPath] = builder;
        }

        if (!selected && IsSharedRuntimeOrDependency(fullPath))
        {
            builder.AddReason("shared runtime or common dependency");
        }
        builder.AddReason(reason);
        builder.Selected = builder.Selected || selected;
        if (builder.ProcessId == 0 && processId > 0)
        {
            builder.ProcessId = processId;
        }
        if (string.IsNullOrWhiteSpace(builder.ProcessName) && !string.IsNullOrWhiteSpace(processName))
        {
            builder.ProcessName = processName;
        }
    }

    private static bool ShouldAutoSelect(string program, string installDirectory) =>
        NetFenceRules.IsPathUnderDirectory(program, installDirectory) || !IsSharedRuntimeOrDependency(program);

    private static bool IsSharedRuntimeOrDependency(string program)
    {
        var fileName = Path.GetFileName(program);
        if (SharedRuntimeNames.Contains(fileName))
        {
            return true;
        }

        var normalized = Path.GetFullPath(program).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return SharedRuntimePathFragments.Any(fragment =>
            normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly HashSet<string> SharedRuntimeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet.exe",
        "java.exe",
        "javaw.exe",
        "node.exe",
        "python.exe",
        "pythonw.exe",
        "msedgewebview2.exe",
        "crashpad_handler.exe",
        "cefsharp.browsersubprocess.exe",
        "vshost.exe"
    };

    private static readonly string[] SharedRuntimePathFragments =
    [
        $"{Path.DirectorySeparatorChar}Common Files{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}Microsoft{Path.DirectorySeparatorChar}EdgeWebView{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}Microsoft{Path.DirectorySeparatorChar}EdgeCore{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}dotnet{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}Java{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}nodejs{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}Python{Path.DirectorySeparatorChar}"
    ];

    private sealed class CandidateBuilder(string program)
    {
        private readonly List<string> _reasons = [];
        public bool Selected { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "";

        public void AddReason(string reason)
        {
            if (!_reasons.Contains(reason, StringComparer.OrdinalIgnoreCase))
            {
                _reasons.Add(reason);
            }
        }

        public RelatedCandidate ToCandidate() =>
            new(Selected, program, string.Join("; ", _reasons), ProcessId, ProcessName);
    }
}
