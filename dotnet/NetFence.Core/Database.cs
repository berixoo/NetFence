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

    public static SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnsureCreated(conn);
        return conn;
    }

    public static string DataDirectory => Path.GetDirectoryName(
        new SqliteConnectionStringBuilder(ConnectionString).DataSource)!;

    private static void EnsureCreated(SqliteConnection conn)
    {
        if (_initialized) return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS RuleProfiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
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
        _initialized = true;
    }
}
