namespace NetFence.Core;

public static class LiveSystemInfo
{
    public static IReadOnlyList<ProcessRow> GetProcessRows()
    {
        var script = """
            $ErrorActionPreference = 'Stop'
            Get-CimInstance Win32_Process |
                Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CommandLine |
                ConvertTo-Csv -NoTypeInformation
            """;

        return ParseCsv(PowerShellRunner.RunRequired(script))
            .Select(row => new ProcessRow(
                ParseInt(row.GetValueOrDefault("ProcessId")),
                ParseInt(row.GetValueOrDefault("ParentProcessId")),
                row.GetValueOrDefault("Name") ?? "",
                EmptyToNull(row.GetValueOrDefault("ExecutablePath")),
                EmptyToNull(row.GetValueOrDefault("CommandLine"))))
            .ToArray();
    }

    public static IReadOnlyList<int> GetNetworkProcessIds()
    {
        var script = """
            $ErrorActionPreference = 'Stop'
            Get-NetTCPConnection -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty OwningProcess -Unique
            """;

        return PowerShellRunner.RunRequired(script)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => int.TryParse(line.Trim(), out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .Order()
            .ToArray();
    }

    internal static IReadOnlyList<Dictionary<string, string>> ParseCsv(string csv)
    {
        var lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return Array.Empty<Dictionary<string, string>>();
        }

        var headers = ParseCsvLine(lines[0]);
        var rows = new List<Dictionary<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            var values = ParseCsvLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
            {
                row[headers[i]] = i < values.Count ? values[i] : "";
            }
            rows.Add(row);
        }

        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new List<char>();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Add('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Add(ch);
                }
            }
            else if (ch == ',')
            {
                result.Add(new string(current.ToArray()));
                current.Clear();
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else
            {
                current.Add(ch);
            }
        }

        result.Add(new string(current.ToArray()));
        return result;
    }

    private static int ParseInt(string? value) => int.TryParse(value, out var id) ? id : 0;

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
