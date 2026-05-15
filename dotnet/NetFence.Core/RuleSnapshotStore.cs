using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NetFence.Core;

public sealed record RuleSnapshot(
    long Id,
    string ProfileName,
    string Action,
    string RulesJson,
    string Timestamp,
    bool IsRollback);

public static class RuleSnapshotStore
{
    private const int MaxSnapshots = 50;

    public static long Create(string profileName, string action, IReadOnlyList<FirewallRuleInfo> rules)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        var rulesJson = JsonSerializer.Serialize(rules);

        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO RuleSnapshots(ProfileName, Action, RulesJson, Timestamp) VALUES(@pn,@a,@rj,@t)";
        cmd.Parameters.AddWithValue("@pn", profileName);
        cmd.Parameters.AddWithValue("@a", action);
        cmd.Parameters.AddWithValue("@rj", rulesJson);
        cmd.Parameters.AddWithValue("@t", timestamp);
        cmd.ExecuteNonQuery();

        using var lastId = conn.CreateCommand();
        lastId.CommandText = "SELECT last_insert_rowid()";
        var id = (long)lastId.ExecuteScalar()!;

        CleanupOld(conn);
        return id;
    }

    public static IReadOnlyList<RuleSnapshot> ListRecent(int limit = 20)
    {
        var results = new List<RuleSnapshot>();
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, ProfileName, Action, RulesJson, Timestamp, IsRollback FROM RuleSnapshots ORDER BY Id DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RuleSnapshot(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5) != 0));
        }
        return results;
    }

    public static RuleSnapshot? GetById(long id)
    {
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, ProfileName, Action, RulesJson, Timestamp, IsRollback FROM RuleSnapshots WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new RuleSnapshot(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5) != 0);
    }

    public static void MarkRolledBack(long id)
    {
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE RuleSnapshots SET IsRollback = 1 WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static void CleanupOld(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RuleSnapshots WHERE Id NOT IN (SELECT Id FROM RuleSnapshots ORDER BY Id DESC LIMIT @max)";
        cmd.Parameters.AddWithValue("@max", MaxSnapshots);
        cmd.ExecuteNonQuery();
    }
}
