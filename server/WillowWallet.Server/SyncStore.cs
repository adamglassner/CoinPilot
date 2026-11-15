using System.Security.Cryptography;
using System.Text;
using WillowWallet.Sync;

namespace WillowWallet.Server;

/// <summary>
/// Each user's records (sealed, so the server can't read them) and files. Every stored change gets the user's next
/// sequence number; devices ask for "changes after N". When two devices change the same record, the newer change wins.
/// </summary>
public sealed class SyncStore(Db db, ServerConfig config)
{
    public ChangesResponse Changes(long userId, long since)
    {
        using var c = db.Open();
        var list = Db.Query(c, """
            SELECT collection, id, seq, modified, deleted, payload FROM records
            WHERE user_id = $userId AND seq > $since ORDER BY seq LIMIT $limit
            """, r => new Change(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4) != 0, r.IsDBNull(5) ? null : r.GetString(5)),
            new { userId, since, limit = Protocol.MaxPullBatch + 1 });
        bool more = list.Count > Protocol.MaxPullBatch;
        if (more) list.RemoveAt(list.Count - 1);
        long latest = (long)Db.Scalar(c, "SELECT seq FROM users WHERE id = $userId", new { userId })!;
        return new ChangesResponse(list, latest, more);
    }

    public PushResponse Push(long userId, PushRequest request)
    {
        if (request.Changes == null || request.Changes.Count == 0) return new PushResponse([], LatestSeq(userId));
        if (request.Changes.Count > Protocol.MaxPushBatch)
            throw new ApiProblem(400, "too_many", $"Send at most {Protocol.MaxPushBatch} changes at a time.");
        foreach (var ch in request.Changes)
        {
            if (!Protocol.IsValidCollection(ch.Collection) || !Protocol.IsValidId(ch.Id))
                throw new ApiProblem(400, "bad_request", "Invalid collection or id.");
            if (!ch.Deleted && (ch.Payload == null || !ch.Payload.StartsWith("v1.") || ch.Payload.Length > Protocol.MaxPayloadChars))
                throw new ApiProblem(400, "bad_request", $"Invalid payload for {ch.Collection}/{ch.Id}.");
        }

        var results = new List<PushResult>();
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        long seq = (long)Db.Scalar(c, "SELECT seq FROM users WHERE id = $userId", new { userId }, tx)!;
        foreach (var ch in request.Changes)
        {
            var existing = Db.Query(c, "SELECT modified, seq FROM records WHERE user_id = $userId AND collection = $col AND id = $id",
                r => (Modified: r.GetInt64(0), Seq: r.GetInt64(1)), new { userId, col = ch.Collection, id = ch.Id }, tx);
            if (existing.Count > 0 && existing[0].Modified > ch.Modified)
            {
                results.Add(new PushResult(ch.Collection, ch.Id, "stale", existing[0].Seq));
                continue;
            }
            seq++;
            Db.Exec(c, """
                INSERT INTO records(user_id, collection, id, seq, modified, deleted, payload) VALUES($userId, $col, $id, $seq, $modified, $deleted, $payload)
                ON CONFLICT(user_id, collection, id) DO UPDATE SET seq = excluded.seq, modified = excluded.modified, deleted = excluded.deleted, payload = excluded.payload
                """, new { userId, col = ch.Collection, id = ch.Id, seq, modified = ch.Modified, deleted = ch.Deleted ? 1 : 0, payload = ch.Deleted ? null : ch.Payload }, tx);
            results.Add(new PushResult(ch.Collection, ch.Id, "ok", seq));
        }
        Db.Exec(c, "UPDATE users SET seq = $seq WHERE id = $userId", new { seq, userId }, tx);
        tx.Commit();
        return new PushResponse(results, seq);
    }

    public long LatestSeq(long userId)
    {
        using var c = db.Open();
        return (long)(Db.Scalar(c, "SELECT seq FROM users WHERE id = $userId", new { userId }) ?? 0L);
    }

    /// <summary>Removes the user's synced records and files but keeps the account (admin "Erase synced data").</summary>
    public void EraseData(long userId)
    {
        DeleteAllFiles(userId);
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        Db.Exec(c, "DELETE FROM records WHERE user_id = $userId", new { userId }, tx);
        Db.Exec(c, "DELETE FROM files WHERE user_id = $userId", new { userId }, tx);
        tx.Commit();
    }

    // ----- Files (receipts, tax documents): sealed by the app, stored as-is -----

    private string UserFolder(long userId) => Path.Combine(config.DataFolder, "files", userId.ToString());
    private string FilePath(long userId, string name) =>
        Path.Combine(UserFolder(userId), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant());

    public async Task SaveFile(long userId, string name, Stream body, CancellationToken ct)
    {
        if (!Protocol.IsValidId(name)) throw new ApiProblem(400, "bad_request", "Invalid file name.");
        Directory.CreateDirectory(UserFolder(userId));
        string path = FilePath(userId, name), temp = path + ".upload";
        long size = 0;
        await using (var file = File.Create(temp))
        {
            var buffer = new byte[81920];
            int n;
            while ((n = await body.ReadAsync(buffer, ct)) > 0)
            {
                size += n;
                if (size > Protocol.MaxFileBytes)
                {
                    file.Close();
                    File.Delete(temp);
                    throw new ApiProblem(413, "too_large", $"Files can be up to {Protocol.MaxFileBytes / 1024 / 1024} MB.");
                }
                await file.WriteAsync(buffer.AsMemory(0, n), ct);
            }
        }
        File.Move(temp, path, overwrite: true);
        using var c = db.Open();
        Db.Exec(c, """
            INSERT INTO files(user_id, name, size, uploaded_at) VALUES($userId, $name, $size, $now)
            ON CONFLICT(user_id, name) DO UPDATE SET size = excluded.size, uploaded_at = excluded.uploaded_at
            """, new { userId, name, size, now = Db.Now() });
    }

    public string? FileFor(long userId, string name)
    {
        if (!Protocol.IsValidId(name)) return null;
        string path = FilePath(userId, name);
        return File.Exists(path) ? path : null;
    }

    public bool DeleteFile(long userId, string name)
    {
        string? path = FileFor(userId, name);
        if (path != null) File.Delete(path);
        using var c = db.Open();
        return Db.Exec(c, "DELETE FROM files WHERE user_id = $userId AND name = $name", new { userId, name }) > 0 || path != null;
    }

    public void DeleteAllFiles(long userId)
    {
        try { if (Directory.Exists(UserFolder(userId))) Directory.Delete(UserFolder(userId), recursive: true); }
        catch (IOException) { }
    }

    // ----- Numbers for the web panel -----

    public (long Records, long Files, long FileBytes) Totals()
    {
        using var c = db.Open();
        return ((long)Db.Scalar(c, "SELECT COUNT(*) FROM records WHERE deleted = 0")!,
                (long)Db.Scalar(c, "SELECT COUNT(*) FROM files")!,
                (long)Db.Scalar(c, "SELECT COALESCE(SUM(size), 0) FROM files")!);
    }
}
