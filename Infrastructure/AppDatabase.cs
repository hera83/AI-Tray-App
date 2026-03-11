using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace TrayApp.Infrastructure
{
    /// <summary>
    /// Owns the SQLite connection string and ensures the schema exists.
    ///
    /// Schema (3 tables):
    ///
    ///   sessions  – one row per conversation
    ///     id          TEXT  PRIMARY KEY   (GUID string)
    ///     title       TEXT  NOT NULL
    ///     created_at  TEXT  NOT NULL      (ISO-8601 UTC)
    ///     updated_at  TEXT  NOT NULL      (ISO-8601 UTC)
    ///
    ///   messages  – one row per chat bubble, foreign-keyed to sessions
    ///     id          TEXT  PRIMARY KEY   (GUID string)
    ///     session_id  TEXT  NOT NULL  REFERENCES sessions(id) ON DELETE CASCADE
    ///     role        INTEGER NOT NULL   (0=System 1=User 2=Assistant)
    ///     content     TEXT  NOT NULL
    ///     created_at  TEXT  NOT NULL      (ISO-8601 UTC)
    ///
    ///   settings  – flat key/value store for all AppSettings fields
    ///     key         TEXT  PRIMARY KEY
    ///     value       TEXT  NOT NULL
    ///
    /// Why this structure?
    ///   • sessions → messages is the only real relationship in the app;
    ///     a 1:N foreign key with CASCADE DELETE keeps cleanup trivial.
    ///   • A key/value settings table avoids schema migrations when new
    ///     settings fields are added — unknown keys are silently ignored
    ///     and new keys fall back to code defaults automatically.
    ///   • No ORM dependency: raw ADO.NET via Microsoft.Data.Sqlite keeps
    ///     the binary small and startup fast, which matters for a tray app
    ///     that may open/close many times per day.
    /// </summary>
    public sealed class AppDatabase
    {
        public string ConnectionString { get; }

        public AppDatabase(string dbPath)
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource   = dbPath,
                Mode         = SqliteOpenMode.ReadWriteCreate,
                Cache        = SqliteCacheMode.Shared,
                ForeignKeys  = true
            };
            ConnectionString = builder.ToString();

            EnsureSchema();
        }

        /// <summary>Open a new connection. Caller is responsible for disposing it.</summary>
        public SqliteConnection Open()
        {
            var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        private void EnsureSchema()
        {
            using var conn = Open();
            using var cmd  = conn.CreateCommand();

            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS sessions (
                    id         TEXT NOT NULL PRIMARY KEY,
                    title      TEXT NOT NULL DEFAULT 'New chat',
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS messages (
                    id         TEXT    NOT NULL PRIMARY KEY,
                    session_id TEXT    NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
                    role       INTEGER NOT NULL DEFAULT 2,
                    content    TEXT    NOT NULL DEFAULT '',
                    created_at TEXT    NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_messages_session
                    ON messages(session_id, created_at);

                CREATE TABLE IF NOT EXISTS settings (
                    key   TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL DEFAULT ''
                );
            ";

            cmd.ExecuteNonQuery();
        }
    }
}
