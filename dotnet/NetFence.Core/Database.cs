using Microsoft.Data.Sqlite;

namespace NetFence.Core;

public static class Database
{
    private static readonly string ConnectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetFence",
            "NetFence.db")
    }.ToString();

    private static bool _initialized;
    private static readonly object _initLock = new();

    public static SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(ConnectionString);
        try
        {
            conn.Open();
            EnsureCreated(conn);
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    public static string DataDirectory => Path.GetDirectoryName(
        new SqliteConnectionStringBuilder(ConnectionString).DataSource)!;

    private static void EnsureCreated(SqliteConnection conn)
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;

            using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS RuleProfiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                PathsJson TEXT NOT NULL DEFAULT '[]',
                ProgramsJson TEXT NOT NULL DEFAULT '[]',
                Mode TEXT NOT NULL DEFAULT 'block_all',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RuleSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileName TEXT NOT NULL,
                Action TEXT NOT NULL,
                RulesJson TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                IsRollback INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS OperationHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Action TEXT NOT NULL,
                ProfileName TEXT NOT NULL DEFAULT '',
                TargetCount INTEGER NOT NULL DEFAULT 0,
                Timestamp TEXT NOT NULL,
                DetailsJson TEXT NOT NULL DEFAULT '{}'
            );
            """;
        cmd.ExecuteNonQuery();

        // Migrations: add columns that may not exist in older DBs
        try { using var m1 = conn.CreateCommand(); m1.CommandText = "ALTER TABLE RuleProfiles ADD COLUMN AllowedIpsJson TEXT NOT NULL DEFAULT '[]'"; m1.ExecuteNonQuery(); } catch { }
        try { using var m2 = conn.CreateCommand(); m2.CommandText = "ALTER TABLE RuleProfiles ADD COLUMN AllowedDomainsJson TEXT NOT NULL DEFAULT '[]'"; m2.ExecuteNonQuery(); } catch { }

        _initialized = true;
        }
    }
}
