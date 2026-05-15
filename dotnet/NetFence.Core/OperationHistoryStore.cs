using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NetFence.Core;

public sealed record OperationRecord(
    long Id,
    string Action,
    string ProfileName,
    int TargetCount,
    string Timestamp,
    string DetailsJson);

public static class OperationHistoryStore
{
    public static void Record(string action, string profileName, int targetCount, object? details = null)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        var detailsJson = details is null ? "{}" : JsonSerializer.Serialize(details);

        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO OperationHistory(Action, ProfileName, TargetCount, Timestamp, DetailsJson) VALUES(@a,@pn,@tc,@t,@dj)";
        cmd.Parameters.AddWithValue("@a", action);
        cmd.Parameters.AddWithValue("@pn", profileName);
        cmd.Parameters.AddWithValue("@tc", targetCount);
        cmd.Parameters.AddWithValue("@t", timestamp);
        cmd.Parameters.AddWithValue("@dj", detailsJson);
        cmd.ExecuteNonQuery();
    }

    public static IReadOnlyList<OperationRecord> ListRecent(int limit = 30)
    {
        var results = new List<OperationRecord>();
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Action, ProfileName, TargetCount, Timestamp, DetailsJson FROM OperationHistory ORDER BY Id DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new OperationRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5)));
        }
        return results;
    }
}
