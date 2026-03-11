using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TrayApp.Models;
using ChatMessage = TrayApp.Models.Message;

namespace TrayApp.Infrastructure
{
    /// <summary>
    /// SQLite-backed chat repository.
    /// Public API is identical to the old JSON version so ViewModels need no changes.
    /// Each call opens and closes its own connection (WAL mode + shared cache make
    /// this safe and fast for a single-user desktop app).
    /// </summary>
    public class ChatRepository : IChatRepository
    {
        private readonly AppDatabase _db;

        public ChatRepository(AppDatabase db) => _db = db;

        // ── write ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Upserts the session row and replaces all its messages.
        /// Replacing messages on every save keeps the logic simple; for a desktop
        /// chat app the message count per session stays small enough that this is
        /// indistinguishable from individual upserts in practice.
        /// </summary>
        public Task SaveSessionAsync(ChatSession session)
        {
            session.UpdatedAt = DateTime.UtcNow;

            using var conn = _db.Open();
            using var tx   = conn.BeginTransaction();

            // upsert session header
            Execute(conn, tx,
                @"INSERT INTO sessions (id, title, created_at, updated_at)
                  VALUES ($id, $title, $ca, $ua)
                  ON CONFLICT(id) DO UPDATE SET
                      title      = excluded.title,
                      updated_at = excluded.updated_at",
                ("$id",    session.Id.ToString()),
                ("$title", session.Title),
                ("$ca",    Iso(session.CreatedAt)),
                ("$ua",    Iso(session.UpdatedAt)));

            // delete existing messages and re-insert (safe under CASCADE on session delete too)
            Execute(conn, tx,
                "DELETE FROM messages WHERE session_id = $sid",
                ("$sid", session.Id.ToString()));

            foreach (var m in session.Messages)
            {
                Execute(conn, tx,
                    @"INSERT INTO messages (id, session_id, role, content, created_at)
                      VALUES ($id, $sid, $role, $content, $ca)",
                    ("$id",      m.Id.ToString()),
                    ("$sid",     session.Id.ToString()),
                    ("$role",    (int)m.Role),
                    ("$content", m.Content),
                    ("$ca",      Iso(m.CreatedAt)));
            }

            tx.Commit();
            return Task.CompletedTask;
        }

        // ── read ──────────────────────────────────────────────────────────────

        public Task<ChatSession[]?> LoadAllAsync()
        {
            using var conn = _db.Open();

            // load all sessions ordered newest first
            var sessions = new List<ChatSession>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, title, created_at, updated_at FROM sessions ORDER BY updated_at DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    sessions.Add(new ChatSession
                    {
                        Id        = Guid.Parse(reader.GetString(0)),
                        Title     = reader.GetString(1),
                        CreatedAt = DateTime.Parse(reader.GetString(2)),
                        UpdatedAt = DateTime.Parse(reader.GetString(3))
                    });
                }
            }

            if (sessions.Count == 0)
                return Task.FromResult<ChatSession[]?>(Array.Empty<ChatSession>());

            // bulk-load messages for all sessions in one query
            var sessionIndex = sessions.ToDictionary(s => s.Id.ToString());
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT id, session_id, role, content, created_at FROM messages ORDER BY created_at ASC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var sid = reader.GetString(1);
                    if (!sessionIndex.TryGetValue(sid, out var session)) continue;

                    session.Messages.Add(new ChatMessage
                    {
                        Id         = Guid.Parse(reader.GetString(0)),
                        Role       = (MessageRole)reader.GetInt32(2),
                        Content    = reader.GetString(3),
                        CreatedAt  = DateTime.Parse(reader.GetString(4)),
                        IsStreaming = false
                    });
                }
            }

            return Task.FromResult<ChatSession[]?>(sessions.ToArray());
        }

        public async Task<ChatSession?> LoadLatestAsync()
        {
            var all = await LoadAllAsync().ConfigureAwait(false);
            return all?.FirstOrDefault();   // already ordered newest-first by updated_at
        }

        // ── delete ────────────────────────────────────────────────────────────

        public Task DeleteSessionAsync(Guid id)
        {
            using var conn = _db.Open();
            // messages are deleted automatically via ON DELETE CASCADE
            Execute(conn, null,
                "DELETE FROM sessions WHERE id = $id",
                ("$id", id.ToString()));
            return Task.CompletedTask;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static string Iso(DateTime dt) =>
            dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        private static void Execute(
            SqliteConnection conn,
            SqliteTransaction? tx,
            string sql,
            params (string name, object? value)[] parameters)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction  = tx;
            cmd.CommandText  = sql;
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }
}
