using Microsoft.Data.Sqlite;

namespace WillowWallet.Server;

/// <summary>The server's SQLite database (willowwallet.db in the data folder). Schema changes go in <see cref="Migrations"/>.</summary>
public sealed class Db
{
    public string Path { get; }
    private readonly string _connectionString;

    public Db(ServerConfig config)
    {
        Path = System.IO.Path.Combine(config.DataFolder, "willowwallet.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path, Cache = SqliteCacheMode.Shared, DefaultTimeout = 30 }.ToString();
        Migrate();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
        return c;
    }

    private static readonly string[] Migrations =
    [
        // 1: first version
        """
        CREATE TABLE users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT NOT NULL UNIQUE COLLATE NOCASE,
            auth_hash BLOB NOT NULL, auth_salt BLOB NOT NULL,
            kdf_salt TEXT NOT NULL, kdf_iterations INTEGER NOT NULL,
            wrapped_key TEXT NOT NULL,
            recovery_hash BLOB NOT NULL, recovery_salt BLOB NOT NULL, recovery_wrapped_key TEXT NOT NULL,
            seq INTEGER NOT NULL DEFAULT 0,
            disabled INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL, last_seen_at TEXT
        );
        CREATE TABLE sessions (
            id TEXT PRIMARY KEY,
            token_hash BLOB NOT NULL UNIQUE,
            user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            device_name TEXT NOT NULL, platform TEXT NOT NULL, app_version TEXT NOT NULL,
            ip TEXT, created_at TEXT NOT NULL, last_seen_at TEXT NOT NULL
        );
        CREATE TABLE records (
            user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            collection TEXT NOT NULL, id TEXT NOT NULL,
            seq INTEGER NOT NULL, modified INTEGER NOT NULL, deleted INTEGER NOT NULL,
            payload TEXT,
            PRIMARY KEY (user_id, collection, id)
        );
        CREATE INDEX records_by_seq ON records(user_id, seq);
        CREATE TABLE files (
            user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            name TEXT NOT NULL, size INTEGER NOT NULL, uploaded_at TEXT NOT NULL,
            PRIMARY KEY (user_id, name)
        );
        CREATE TABLE invites (
            code TEXT PRIMARY KEY,
            username TEXT, note TEXT,
            uses_left INTEGER NOT NULL, created_at TEXT NOT NULL, expires_at TEXT,
            used_by TEXT
        );
        CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE admins (
            username TEXT PRIMARY KEY COLLATE NOCASE,
            hash BLOB NOT NULL, salt BLOB NOT NULL, iterations INTEGER NOT NULL, created_at TEXT NOT NULL
        );
        CREATE TABLE admin_sessions (
            token_hash BLOB PRIMARY KEY, username TEXT NOT NULL, expires_at TEXT NOT NULL
        );
        CREATE TABLE activity (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            at TEXT NOT NULL, kind TEXT NOT NULL, username TEXT, detail TEXT NOT NULL
        );
        """,
    ];

    private void Migrate()
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        using var c = Open();
        Exec(c, "PRAGMA journal_mode = WAL;");
        long version = (long)Scalar(c, "PRAGMA user_version;")!;
        for (int v = (int)version; v < Migrations.Length; v++)
        {
            using var tx = c.BeginTransaction();
            Exec(c, Migrations[v], tx: tx);
            Exec(c, $"PRAGMA user_version = {v + 1};", tx: tx);
            tx.Commit();
        }
    }

    public static int Exec(SqliteConnection c, string sql, object? args = null, SqliteTransaction? tx = null)
    {
        using var cmd = Command(c, sql, args, tx);
        return cmd.ExecuteNonQuery();
    }

    public static object? Scalar(SqliteConnection c, string sql, object? args = null, SqliteTransaction? tx = null)
    {
        using var cmd = Command(c, sql, args, tx);
        var v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }

    public static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, object? args = null, SqliteTransaction? tx = null)
    {
        using var cmd = Command(c, sql, args, tx);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    /// <summary>Parameters come from an anonymous object: new { id = 1 } binds $id.</summary>
    public static SqliteCommand Command(SqliteConnection c, string sql, object? args, SqliteTransaction? tx)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        if (args != null)
            foreach (var p in args.GetType().GetProperties())
                cmd.Parameters.AddWithValue("$" + p.Name, p.GetValue(args) ?? DBNull.Value);
        return cmd;
    }

    public static string Now() => DateTime.UtcNow.ToString("O");
    public static DateTime? Date(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : DateTime.Parse(r.GetString(i), null, System.Globalization.DateTimeStyles.RoundtripKind);

    /// <summary>A consistent copy of the database (for the admin "Download backup").</summary>
    public string BackupTo(string folder)
    {
        string file = System.IO.Path.Combine(folder, $"willowwallet-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
        using var c = Open();
        Exec(c, "VACUUM INTO $file;", new { file });
        return file;
    }
}
