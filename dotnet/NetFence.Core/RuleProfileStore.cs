using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NetFence.Core;

public sealed record RuleProfile(
    long Id,
    string Name,
    List<string> Paths,
    List<string> Programs,
    string Mode,
    List<string> AllowedIps,
    List<string> AllowedDomains,
    string CreatedAt,
    string UpdatedAt);

public static class RuleProfileStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
        { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IReadOnlyList<RuleProfile> ListAll()
    {
        var results = new List<RuleProfile>();
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Name, PathsJson, ProgramsJson, Mode, CreatedAt, UpdatedAt,
                   COALESCE(AllowedIpsJson, '[]'), COALESCE(AllowedDomainsJson, '[]')
            FROM RuleProfiles ORDER BY UpdatedAt DESC
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RuleProfile(
                reader.GetInt64(0),
                reader.GetString(1),
                JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [],
                JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [],
                reader.GetString(4),
                JsonSerializer.Deserialize<List<string>>(reader.GetString(7)) ?? [],
                JsonSerializer.Deserialize<List<string>>(reader.GetString(8)) ?? [],
                reader.GetString(5),
                reader.GetString(6)));
        }
        return results;
    }

    public static RuleProfile? GetById(long id)
    {
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Name, PathsJson, ProgramsJson, Mode, CreatedAt, UpdatedAt,
                   COALESCE(AllowedIpsJson, '[]'), COALESCE(AllowedDomainsJson, '[]')
            FROM RuleProfiles WHERE Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new RuleProfile(
            reader.GetInt64(0),
            reader.GetString(1),
            JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [],
            JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [],
            reader.GetString(4),
            JsonSerializer.Deserialize<List<string>>(reader.GetString(7)) ?? [],
            JsonSerializer.Deserialize<List<string>>(reader.GetString(8)) ?? [],
            reader.GetString(5),
            reader.GetString(6));
    }

    public static long Save(string name, List<string> paths, List<string> programs,
        string mode = "block_all", List<string>? allowedIps = null, List<string>? allowedDomains = null)
    {
        var now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        var pathsJson = JsonSerializer.Serialize(paths, JsonOpts);
        var programsJson = JsonSerializer.Serialize(programs, JsonOpts);
        var allowedIpsJson = JsonSerializer.Serialize(allowedIps ?? [], JsonOpts);
        var allowedDomainsJson = JsonSerializer.Serialize(allowedDomains ?? [], JsonOpts);

        using var conn = Database.OpenConnection();
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT Id FROM RuleProfiles WHERE Name = @name";
        checkCmd.Parameters.AddWithValue("@name", name);
        var existing = checkCmd.ExecuteScalar();

        if (existing is long id)
        {
            using var update = conn.CreateCommand();
            update.CommandText = "UPDATE RuleProfiles SET PathsJson=@p, ProgramsJson=@pr, Mode=@m, AllowedIpsJson=@ai, AllowedDomainsJson=@ad, UpdatedAt=@u WHERE Id=@id";
            update.Parameters.AddWithValue("@p", pathsJson);
            update.Parameters.AddWithValue("@pr", programsJson);
            update.Parameters.AddWithValue("@m", mode);
            update.Parameters.AddWithValue("@ai", allowedIpsJson);
            update.Parameters.AddWithValue("@ad", allowedDomainsJson);
            update.Parameters.AddWithValue("@u", now);
            update.Parameters.AddWithValue("@id", id);
            update.ExecuteNonQuery();
            return id;
        }
        else
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO RuleProfiles(Name, PathsJson, ProgramsJson, Mode, AllowedIpsJson, AllowedDomainsJson, CreatedAt, UpdatedAt) VALUES(@n,@p,@pr,@m,@ai,@ad,@c,@u)";
            insert.Parameters.AddWithValue("@n", name);
            insert.Parameters.AddWithValue("@p", pathsJson);
            insert.Parameters.AddWithValue("@pr", programsJson);
            insert.Parameters.AddWithValue("@m", mode);
            insert.Parameters.AddWithValue("@ai", allowedIpsJson);
            insert.Parameters.AddWithValue("@ad", allowedDomainsJson);
            insert.Parameters.AddWithValue("@c", now);
            insert.Parameters.AddWithValue("@u", now);
            insert.ExecuteNonQuery();

            using var lastId = conn.CreateCommand();
            lastId.CommandText = "SELECT last_insert_rowid()";
            return (long)lastId.ExecuteScalar()!;
        }
    }

    public static bool Delete(long id)
    {
        using var conn = Database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RuleProfiles WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }
}
