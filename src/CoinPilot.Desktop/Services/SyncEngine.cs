using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CoinPilot.Data;
using WillowWallet.Sync;

namespace CoinPilot.Services;

/// <summary>What this PC remembers about sync (data\sync.json; the session and data key are also protected with Windows DPAPI).</summary>
public sealed class SyncSettings
{
    public string ServerUrl { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string Username { get; set; } = "";
    public string ProtectedToken { get; set; } = "";
    public string ProtectedKey { get; set; } = "";
    /// <summary>Server sequence number we've pulled up to.</summary>
    public long LastSeq { get; set; }
    /// <summary>"collection/id" → UpdatedAt ticks of the version the server has. A record whose time differs needs sending;
    /// a key whose record is gone was deleted here.</summary>
    public Dictionary<string, long> Synced { get; set; } = [];
    /// <summary>Receipts and tax documents the server has ("receipt/…", "tax/…").</summary>
    public HashSet<string> Files { get; set; } = [];
    public DateTime? LastSync { get; set; }
}

/// <summary>
/// Keeps this PC's data in sync with a WillowWallet Server: sends local changes, applies changes from other devices
/// (newest edit wins), and copies receipts and tax documents both ways. Everything is encrypted before it leaves the PC.
/// Without a server WillowWallet works exactly as before, with everything saved on this PC.
/// </summary>
public static class SyncEngine
{
    public enum State { Off, Idle, Syncing, Offline, Paused, SignedOut, Error }

    public static State Status { get; private set; } = State.Off;
    /// <summary>Why the last sync didn't finish (for the Sync page and sidebar).</summary>
    public static string? Problem { get; private set; }
    public static SyncSettings? Settings => _s;
    public static bool IsConnected => _s != null && _api?.Token != null && _key != null;
    public static DateTime? LastSync => _s?.LastSync;
    public static event EventHandler? StatusChanged;

    private static SyncSettings? _s;
    private static byte[]? _key;
    private static SyncApiClient? _api;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DispatcherTimer? _timer, _debounce;
    private static string FilePath => Path.Combine(DataStore.Folder, "sync.json");
    private static readonly byte[] Entropy = "WillowWallet sync"u8.ToArray();

    public static DeviceInfo Device => new(Environment.MachineName, "Windows", App.VersionText);

    // ----- Start-up -----

    /// <summary>Called once the data is open (after the PIN, if any). Safe to call again.</summary>
    public static void Start()
    {
        if (!DataLock.IsOpen) return;
        Load();
        SyncTracker.OnChanged = (_, _, _) => SyncSoon();
        if (SelfTest.IsRunning || Screenshots.IsRunning) return;
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _timer.Tick += (_, _) => _ = SyncNowAsync();
            _timer.Start();
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _debounce.Tick += (_, _) => { _debounce.Stop(); _ = SyncNowAsync(); };
        }
        if (IsConnected) _ = SyncNowAsync();
    }

    /// <summary>A change was made here: sync in a few seconds (several quick changes go together).</summary>
    public static void SyncSoon()
    {
        if (!IsConnected || _debounce == null) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private static void Load()
    {
        _s = null; _key = null;
        _api?.Dispose(); _api = null;
        try
        {
            if (!File.Exists(FilePath)) { SetStatus(State.Off, null); return; }
            _s = JsonSerializer.Deserialize<SyncSettings>(DataLock.ReadText(FilePath));
        }
        catch (Exception) { _s = null; }
        if (_s == null || string.IsNullOrEmpty(_s.ServerUrl)) { _s = null; SetStatus(State.Off, null); return; }
        _api = new SyncApiClient(_s.ServerUrl);
        try
        {
            _api.Token = System.Text.Encoding.UTF8.GetString(Unprotect(_s.ProtectedToken));
            _key = Unprotect(_s.ProtectedKey);
            SetStatus(State.Idle, null);
        }
        catch (Exception)
        {
            // Copied from another PC (or a restored backup): this PC has to sign in again
            _api.Token = null;
            _key = null;
            SetStatus(State.SignedOut, "Sign in again to keep syncing on this PC.");
        }
    }

    private static void Save()
    {
        if (_s == null || !DataLock.IsOpen) return;
        try { DataLock.WriteText(FilePath, JsonSerializer.Serialize(_s)); }
        catch (IOException) { }
    }

    private static byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
    private static byte[] Unprotect(string b64) => ProtectedData.Unprotect(Convert.FromBase64String(b64), Entropy, DataProtectionScope.CurrentUser);

    private static void SetStatus(State state, string? problem)
    {
        Status = state;
        Problem = problem;
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    // ----- Connecting -----

    /// <summary>What the server says about itself, for the "Check" step.</summary>
    public static async Task<ServerInfo> CheckServerAsync(string url)
    {
        using var api = new SyncApiClient(url);
        return await api.InfoAsync();
    }

    /// <summary>True when there's anything on this PC (so connecting asks how to combine it with the server's copy).</summary>
    public static bool HasLocalData => DataStore.SyncStores.Any(s => s.Count > 0);

    /// <summary>Signs in (or creates the account). Returns the recovery key for a new account, otherwise null.</summary>
    public static async Task<(SessionResponse Session, string? RecoveryKey)> ConnectAsync(string url, string username, string password, bool create, string? invite, int iterations = SyncCrypto.DefaultIterations)
    {
        var api = new SyncApiClient(url);
        try
        {
            var info = await api.InfoAsync();
            SignedIn signedIn;
            string? recovery = null;
            if (create) (signedIn, recovery) = await SyncAccount.RegisterAsync(api, username, password, invite, Device, iterations);
            else signedIn = await SyncAccount.LoginAsync(api, username, password, Device);
            Adopt(api, info, signedIn);
            return (signedIn.Session, recovery);
        }
        catch
        {
            api.Dispose();
            throw;
        }
    }

    /// <summary>Forgot password: the recovery key opens the account and sets a new password.</summary>
    public static async Task<SessionResponse> RecoverAsync(string url, string username, string recoveryKey, string newPassword, int iterations = SyncCrypto.DefaultIterations)
    {
        var api = new SyncApiClient(url);
        try
        {
            var info = await api.InfoAsync();
            var signedIn = await SyncAccount.RecoverAsync(api, username, recoveryKey, newPassword, Device, iterations);
            Adopt(api, info, signedIn);
            return signedIn.Session;
        }
        catch
        {
            api.Dispose();
            throw;
        }
    }

    private static void Adopt(SyncApiClient api, ServerInfo info, SignedIn signedIn)
    {
        _api?.Dispose();
        _api = api;
        _key = signedIn.DataKey;
        // A different account or server than before starts from scratch; the same account picks up where it left off
        bool same = _s != null && _s.ServerUrl == api.BaseAddress.ToString() && _s.Username == signedIn.Session.Username;
        _s = same ? _s! : new SyncSettings();
        _s.ServerUrl = api.BaseAddress.ToString();
        _s.ServerName = info.Name;
        _s.Username = signedIn.Session.Username;
        _s.ProtectedToken = Convert.ToBase64String(Protect(System.Text.Encoding.UTF8.GetBytes(signedIn.Session.Token)));
        _s.ProtectedKey = Convert.ToBase64String(Protect(signedIn.DataKey));
        Save();
        SetStatus(State.Idle, null);
    }

    /// <summary>
    /// First sync after connecting. Combine: this PC's data and the server's are merged (newest edit wins).
    /// Server only: this PC's data is replaced with the server's copy.
    /// </summary>
    public static async Task<bool> FirstSyncAsync(bool useServerOnly)
    {
        if (_s == null) return false;
        if (useServerOnly)
        {
            using (DataStore.Batch())
                foreach (var store in DataStore.SyncStores) store.ClearLocal();
            _s.Synced.Clear();
        }
        _s.LastSeq = 0;
        Save();
        return await SyncNowAsync(manual: true);
    }

    public static async Task ChangePasswordAsync(string current, string next)
    {
        if (!IsConnected) throw new SyncApiException(null, "unauthorized", "Sign in first.");
        await SyncAccount.ChangePasswordAsync(_api!, _s!.Username, current, next, _key!);
    }

    public static Task<AccountResponse> AccountAsync() =>
        IsConnected ? _api!.AccountAsync() : throw new SyncApiException(null, "unauthorized", "Sign in first.");

    public static async Task SignOutDeviceAsync(string id)
    {
        if (!IsConnected) return;
        await _api!.SignOutDeviceAsync(id);
    }

    /// <summary>Stops syncing on this PC. The data stays here.</summary>
    public static async Task SignOutAsync()
    {
        if (_api?.Token != null) { try { await _api.LogoutAsync(); } catch (SyncApiException) { } }
        _api?.Dispose();
        _api = null;
        _key = null;
        _s = null;
        try { File.Delete(FilePath); } catch (IOException) { }
        SetStatus(State.Off, null);
    }

    /// <summary>After restoring a backup: forget what the server has so everything here is compared again.</summary>
    public static void OnDataReplaced()
    {
        Load();
        if (_s == null) return;
        _s.LastSeq = 0;
        _s.Synced.Clear();
        Save();
        SyncSoon();
    }

    /// <summary>Changes on this PC the server doesn't have yet.</summary>
    public static int PendingCount => _s == null ? 0 : FindLocalChanges().Count;

    // ----- Syncing -----

    /// <summary>Sends local changes, then fetches everyone else's. Returns true when it finished.</summary>
    public static async Task<bool> SyncNowAsync(bool manual = false)
    {
        if (!IsConnected || !DataLock.IsOpen) return false;
        if (!await Gate.WaitAsync(manual ? 30_000 : 0)) return false;
        try
        {
            SetStatus(State.Syncing, null);
            await PushAsync();
            await PullAsync();
            await SyncFilesAsync();
            _s!.LastSync = DateTime.Now;
            Save();
            SetStatus(State.Idle, null);
            return true;
        }
        catch (SyncApiException e)
        {
            Save();
            if (e.IsAuthProblem)
            {
                _api!.Token = null;
                _key = null;
                SetStatus(State.SignedOut, "You were signed out of sync (your password changed or the server's admin signed this PC out). Sign in again to keep syncing.");
            }
            else if (e.Error == "paused") SetStatus(State.Paused, e.Message);
            else if (e.Error == "offline") SetStatus(State.Offline, e.Message);
            else SetStatus(State.Error, e.Message);
            if (manual) Toaster.Show(Problem ?? e.Message);
            return false;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or IOException)
        {
            Save();
            SetStatus(State.Error, "Sync stopped: " + e.Message);
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    private sealed record LocalChange(string Collection, string Id, bool Deleted, long Ticks, long Modified, string? Json);

    private static List<LocalChange> FindLocalChanges()
    {
        var s = _s!;
        var list = new List<LocalChange>();
        foreach (var store in DataStore.SyncStores)
        {
            var present = new HashSet<string>();
            foreach (var (id, updated) in store.Entries())
            {
                present.Add(id);
                string key = store.Name + "/" + id;
                if (s.Synced.TryGetValue(key, out long ticks) && ticks == updated.Ticks) continue;
                list.Add(new LocalChange(store.Name, id, false, updated.Ticks, Ms(updated), null));
            }
            string prefix = store.Name + "/";
            foreach (var (key, ticks) in s.Synced.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && !present.Contains(kv.Key[prefix.Length..])))
            {
                // A delete is newer than the last version this PC had, even if that edit's clock ran ahead
                long modified = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Ms(new DateTime(ticks)) + 1);
                list.Add(new LocalChange(store.Name, key[prefix.Length..], true, 0, modified, null));
            }
        }
        foreach (var name in new[] { "preferences", "debt-plan" })
        {
            var file = DataStore.SettingsFile(name)!;
            if (file.UpdatedAt == default) continue;
            string key = SyncTracker.SettingsCollection + "/" + name;
            if (s.Synced.TryGetValue(key, out long ticks) && ticks == file.UpdatedAt.Ticks) continue;
            list.Add(new LocalChange(SyncTracker.SettingsCollection, name, false, file.UpdatedAt.Ticks, Ms(file.UpdatedAt), file.ToJson()));
        }
        return list;
    }

    private static long Ms(DateTime t) => new DateTimeOffset(t.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(t, DateTimeKind.Local) : t).ToUnixTimeMilliseconds();

    private static async Task PushAsync()
    {
        var changes = FindLocalChanges();
        foreach (var batch in changes.Chunk(Protocol.MaxPushBatch))
        {
            var request = new List<PushChange>();
            foreach (var c in batch)
            {
                if (c.Deleted)
                {
                    request.Add(new PushChange(c.Collection, c.Id, c.Modified, true, null));
                    continue;
                }
                string? json = c.Json ?? DataStore.SyncStore(c.Collection)?.Get(c.Id)?.Json;
                if (json == null) continue;
                request.Add(new PushChange(c.Collection, c.Id, c.Modified, false, SyncAccount.SealRecord(_key!, c.Collection, c.Id, json)));
            }
            if (request.Count == 0) continue;
            var response = await _api!.PushAsync(new PushRequest(request));
            // The server has a newer version of these: fetch them again on the pull that follows
            foreach (var r in response.Results.Where(r => r.Status == "stale"))
                if (r.Seq > 0 && r.Seq <= _s!.LastSeq) _s.LastSeq = r.Seq - 1;
            foreach (var r in response.Results.Where(r => r.Status == "ok"))
            {
                string key = r.Collection + "/" + r.Id;
                var sent = batch.First(c => c.Collection == r.Collection && c.Id == r.Id);
                if (sent.Deleted) _s!.Synced.Remove(key);
                else _s!.Synced[key] = sent.Ticks;
            }
            Save();
        }
    }

    private static async Task PullAsync()
    {
        var s = _s!;
        while (true)
        {
            var page = await _api!.ChangesAsync(s.LastSeq);
            ApplyChanges(page.Changes);
            s.LastSeq = page.Changes.Count > 0 ? page.Changes[^1].Seq : Math.Max(s.LastSeq, page.Latest);
            Save();
            if (!page.More) break;
        }
    }

    private static void ApplyChanges(List<Change> changes)
    {
        if (changes.Count == 0) return;
        var s = _s!;
        using var batch = DataStore.Batch();
        foreach (var group in changes.GroupBy(c => c.Collection))
        {
            if (group.Key == SyncTracker.SettingsCollection)
            {
                foreach (var c in group.Where(c => !c.Deleted && c.Payload != null))
                {
                    var file = DataStore.SettingsFile(c.Id);
                    if (file == null) continue;
                    string key = group.Key + "/" + c.Id;
                    bool changedHere = file.UpdatedAt != default && (!s.Synced.TryGetValue(key, out long t) || t != file.UpdatedAt.Ticks);
                    if (changedHere && Ms(file.UpdatedAt) > c.Modified) continue;
                    DataStore.ApplyRemoteSettings(c.Id, SyncAccount.OpenRecord(_key!, group.Key, c.Id, c.Payload!));
                    s.Synced[key] = DataStore.SettingsFile(c.Id)!.UpdatedAt.Ticks;
                }
                continue;
            }
            var store = DataStore.SyncStore(group.Key);
            if (store == null) continue; // something a newer app version syncs: leave it on the server
            var upserts = new List<string>();
            var deletes = new List<string>();
            foreach (var c in group.GroupBy(c => c.Id).Select(g => g.Last()))
            {
                string key = group.Key + "/" + c.Id;
                var local = store.Get(c.Id);
                bool changedHere = local != null && (!s.Synced.TryGetValue(key, out long t) || t != local.Value.UpdatedAt.Ticks);
                if (changedHere && Ms(local!.Value.UpdatedAt) > c.Modified) continue; // ours is newer: it gets sent next
                if (c.Deleted)
                {
                    if (local != null) deletes.Add(c.Id);
                    s.Synced.Remove(key);
                }
                else if (c.Payload != null)
                {
                    // Our own change coming back: nothing to do
                    if (local != null && !changedHere && Ms(local.Value.UpdatedAt) == c.Modified) continue;
                    upserts.Add(SyncAccount.OpenRecord(_key!, group.Key, c.Id, c.Payload));
                }
            }
            if (deletes.Count > 0) store.RemoveRemote(deletes);
            if (upserts.Count > 0)
            {
                store.ApplyRemote(upserts);
                foreach (var json in upserts)
                {
                    using var doc = JsonDocument.Parse(json);
                    string id = doc.RootElement.GetProperty("Id").GetString()!;
                    if (store.Get(id) is { } now) s.Synced[group.Key + "/" + id] = now.UpdatedAt.Ticks;
                }
            }
        }
    }

    // ----- Receipts and tax documents -----

    private static async Task SyncFilesAsync()
    {
        var s = _s!;
        var wanted = new List<(string Name, string Path)>();
        foreach (var t in DataStore.Transactions.Items.Where(t => t.Receipt.Length > 0))
            wanted.Add(("receipt/" + t.Receipt, FileVault.ReceiptPath(t.Receipt)));
        foreach (var d in DataStore.TaxDocuments.Items.Where(d => d.FileName.Length > 0))
        {
            string path = FileVault.TaxDocumentPath(d);
            if (!File.Exists(path)) MoveFromOtherYear(d, path);
            wanted.Add(("tax/" + d.FileName, path));
        }
        foreach (var (name, path) in wanted.DistinctBy(w => w.Name))
        {
            if (!Protocol.IsValidId(name)) continue;
            bool here = File.Exists(path);
            if (here && !s.Files.Contains(name))
            {
                var sealedBytes = SyncCrypto.SealBytes(_key!, DataLock.ReadBytes(path), SyncCrypto.FileAad(name));
                await _api!.UploadFileAsync(name, sealedBytes);
                s.Files.Add(name);
                Save();
            }
            else if (!here)
            {
                var sealedBytes = await _api!.DownloadFileAsync(name);
                if (sealedBytes == null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                DataLock.WriteBytes(path, SyncCrypto.OpenBytes(_key!, sealedBytes, SyncCrypto.FileAad(name)));
                s.Files.Add(name);
                Save();
            }
        }

        // Files no record uses any more (receipts removed, data deleted): remove them from the server too
        var keep = wanted.Select(w => w.Name).ToHashSet();
        foreach (var name in s.Files.Where(f => !keep.Contains(f)).ToList())
        {
            try { await _api!.DeleteFileAsync(name); }
            catch (SyncApiException e) when (e.Status == 404) { }
            s.Files.Remove(name);
            Save();
        }
    }

    /// <summary>A tax document moved to another year on another device: move the file here too.</summary>
    private static void MoveFromOtherYear(TaxDocument d, string path)
    {
        if (!Directory.Exists(DataStore.TaxesFolder)) return;
        var found = Directory.EnumerateFiles(DataStore.TaxesFolder, d.FileName, SearchOption.AllDirectories).FirstOrDefault();
        if (found == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Move(found, path);
    }

    /// <summary>"Synced 2 minutes ago" and similar, for the sidebar and Sync page.</summary>
    public static string StatusText => Status switch
    {
        State.Off => "Saved on this PC",
        State.Syncing => "Syncing…",
        State.Offline => "Can't reach the server",
        State.Paused => "Sync is stopped on the server",
        State.SignedOut => "Signed out of sync",
        State.Error => "Sync problem",
        _ => LastSync is DateTime t ? "Synced " + Ago(t) : "Connected",
    };

    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalSeconds < 60) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} h ago";
        return t.ToString("MMM d");
    }
}
