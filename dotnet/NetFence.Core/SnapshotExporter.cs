using System.Text;

namespace NetFence.Core;

public static class SnapshotExporter
{
    public static SnapshotExportResult Export(
        string path,
        IEnumerable<FirewallRuleInfo> rules,
        IEnumerable<RelatedCandidate> candidates)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var rows = ConvertToRows(rules, candidates).ToArray();
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine("Type,Selected,ProfileName,Direction,Enabled,Action,Program,Reason,ProcessId,ProcessName,DisplayName");
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(",", row.Select(Escape)));
        }

        return new SnapshotExportResult(path, rows.Length);
    }

    public static IEnumerable<string[]> ConvertToRows(
        IEnumerable<FirewallRuleInfo> rules,
        IEnumerable<RelatedCandidate> candidates)
    {
        foreach (var rule in rules)
        {
            yield return
            [
                "FirewallRule",
                "",
                rule.ProfileName,
                rule.Direction,
                rule.Enabled.ToString(),
                rule.Action,
                rule.Program,
                "",
                "",
                "",
                rule.DisplayName
            ];
        }

        foreach (var candidate in candidates)
        {
            yield return
            [
                "Candidate",
                candidate.Selected.ToString(),
                "",
                "",
                "",
                "",
                candidate.Program,
                candidate.Reason,
                candidate.ProcessId.ToString(),
                candidate.ProcessName,
                ""
            ];
        }
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
