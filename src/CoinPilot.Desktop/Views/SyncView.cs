using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CoinPilot.Data;
using CoinPilot.Services;
using WillowWallet.Sync;

namespace CoinPilot.Views;

/// <summary>
/// Sync & Devices: connect this PC to a WillowWallet Server (sign in or create an account), see sync status and the
/// devices on the account. Without a server, WillowWallet keeps everything on this PC as before.
/// </summary>
public sealed class SyncView : DataPage
{
    // Kept between rebuilds so typing isn't lost
    private static string _url = "", _user = "", _invite = "";
    private static bool _create;
    private static ServerInfo? _checked;
    private static AccountResponse? _account;
    private static DateTime _accountAt;
    private string? _error;
    private bool _busy;

    public SyncView()
    {
        Loaded += (_, _) => SyncEngine.StatusChanged += OnSync;
        Unloaded += (_, _) => SyncEngine.StatusChanged -= OnSync;
    }

    private void OnSync(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Rebuild);

    protected override void Build()
    {
        Add(PageKit.Header("", "Sync & Devices", "Keep WillowWallet up to date on this PC and your phone, with a server you run yourself."));
        if (SyncEngine.IsConnected) BuildConnected();
        else BuildConnect();
    }

    // ----- Not connected -----

    private void BuildConnect()
    {
        var s = SyncEngine.Settings;
        bool signedOut = SyncEngine.Status == SyncEngine.State.SignedOut && s != null;
        if (signedOut)
        {
            _url = s!.ServerUrl;
            _user = s.Username;
            _create = false;
        }

        var cols = Ui.Columns(1.25, 0.06, 1);
        var form = new StackPanel();

        if (signedOut)
            form.Children.Add(Banner("", SyncEngine.Problem ?? "Sign in again to keep syncing.", danger: true));

        form.Children.Add(Label("Server address"));
        var urlRow = new DockPanel();
        var check = PageKit.Ghost("", "Check", async (_, _) => await CheckServer());
        check.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(check, Dock.Right);
        urlRow.Children.Add(check);
        var url = new TextBox { Text = _url, IsEnabled = !signedOut };
        AutomationName(url, "Server address");
        url.TextChanged += (_, _) => { _url = url.Text; _checked = null; };
        urlRow.Children.Add(url);
        form.Children.Add(urlRow);
        form.Children.Add(Hint(_checked != null
            ? $"✓ {_checked.Name} · version {_checked.Version}" + (_checked.Syncing ? "" : " · syncing is stopped right now") + (_checked.OpenSignup ? " · anyone can sign up" : " · sign-up needs an invite code")
            : "The address of your WillowWallet Server, like http://192.168.1.20:8484. The person who runs it can tell you."));

        if (!signedOut)
        {
            var mode = PageKit.ChoiceChips(["Sign in", "Create an account"], _create ? "Create an account" : "Sign in", v => { _create = v != "Sign in"; Rebuild(); });
            mode.Margin = new Thickness(0, 14, 0, 4);
            form.Children.Add(mode);
        }

        form.Children.Add(Label("Username"));
        var user = new TextBox { Text = _user, IsEnabled = !signedOut };
        AutomationName(user, "Username");
        user.TextChanged += (_, _) => _user = user.Text;
        form.Children.Add(user);
        if (_create) form.Children.Add(Hint("3 to 32 letters, numbers, dots, dashes or underscores."));

        form.Children.Add(Label("Password"));
        var pass = new PasswordBox();
        AutomationName(pass, "Password");
        form.Children.Add(pass);
        PasswordBox? confirm = null;
        TextBox? invite = null;
        if (_create)
        {
            form.Children.Add(Hint("At least 8 characters. It encrypts your data, so nobody else (not even the server's admin) can read it."));
            form.Children.Add(Label("Type it again"));
            confirm = new PasswordBox();
            AutomationName(confirm, "Confirm password");
            form.Children.Add(confirm);
            form.Children.Add(Label("Invite code"));
            invite = new TextBox { Text = _invite, CharacterCasing = CharacterCasing.Upper };
            AutomationName(invite, "Invite code");
            invite.TextChanged += (_, _) => _invite = invite.Text;
            form.Children.Add(invite);
            form.Children.Add(Hint(_checked?.OpenSignup == true ? "Not needed on this server." : "Ask the person who runs the server for one."));
        }

        if (_error != null) form.Children.Add(Banner("", _error, danger: true));

        var go = PageKit.Primary(_create ? "" : "", _busy ? "Working…" : _create ? "Create account and sync" : "Sign in and sync",
            async (_, _) => await Connect(pass.Password, confirm?.Password, invite?.Text));
        go.IsEnabled = !_busy;
        go.HorizontalAlignment = HorizontalAlignment.Left;
        go.Margin = new Thickness(0, 18, 0, 0);
        form.Children.Add(go);
        pass.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && !_create) await Connect(pass.Password, null, null); };

        var links = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        links.Children.Add(PageKit.Link("Forgot your password?", () => _ = Recover()).Also(b => b.Margin = new Thickness(0, 0, 18, 0)));
        if (signedOut) links.Children.Add(PageKit.Link("Stop syncing on this PC", () => _ = StopSyncing()));
        form.Children.Add(links);

        cols.Children.Add(PageKit.Card(form, signedOut ? "Sign in again" : "Connect to a WillowWallet Server").At(0));
        cols.Children.Add(HowItWorks().At(2));
        Add(cols);
    }

    private Border HowItWorks()
    {
        var panel = new StackPanel();
        foreach (var (glyph, title, text) in new[]
                 {
                     ("", "Your data stays on this PC", "Sync adds a copy on your server. WillowWallet keeps working offline and catches up when it can reach the server."),
                     ("", "Encrypted before it leaves", "Your budgets, transactions, receipts and tax documents are locked with your password. The server stores them but can't read them."),
                     ("", "Your PC and your phone", "Sign in with the same account in WillowWallet for Android to see everything on both."),
                     ("", "Your own server", "Run WillowWallet Server on a PC, a Raspberry Pi, a NAS or with Docker. Get it from the Bytehawk Studio Launcher or GitHub."),
                 })
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var tile = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            tile.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
            tile.Child = Ui.Text(glyph, size: 17, brushKey: "Brush.Accent").Also(t => { t.Style = Res("Icon"); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
            DockPanel.SetDock(tile, Dock.Left);
            row.Children.Add(tile);
            var words = new StackPanel();
            words.Children.Add(Ui.Text(title, weight: FontWeights.SemiBold, size: 14));
            words.Children.Add(Ui.Text(text, muted: true).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 3, 0, 0); }));
            row.Children.Add(words);
            panel.Children.Add(row);
        }
        var link = PageKit.Ghost("", "Get WillowWallet Server", (_, _) =>
            Process.Start(new ProcessStartInfo("https://github.com/adamglassner/CoinPilot/releases?q=server") { UseShellExecute = true }));
        link.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(link);
        return PageKit.Card(panel, "How sync works");
    }

    private async Task CheckServer()
    {
        _error = null;
        try { _checked = await SyncEngine.CheckServerAsync(_url); }
        catch (SyncApiException e) { _checked = null; _error = e.Message; }
        Rebuild();
    }

    private async Task Connect(string password, string? confirm, string? invite)
    {
        _error = null;
        if (string.IsNullOrWhiteSpace(_url)) { _error = "Enter the server's address."; Rebuild(); return; }
        if (string.IsNullOrWhiteSpace(_user)) { _error = "Enter your username."; Rebuild(); return; }
        if (password.Length == 0) { _error = "Enter your password."; Rebuild(); return; }
        if (_create && password != confirm) { _error = "The two passwords don't match."; Rebuild(); return; }
        _busy = true;
        Rebuild();
        try
        {
            var (session, recovery) = await SyncEngine.ConnectAsync(_url, _user, password, _create, invite);
            if (recovery != null) new RecoveryKeyDialog(recovery, forSync: true) { Owner = Window.GetWindow(this) }.ShowDialog();
            await FirstSync(session.LatestSeq > 0);
            _invite = "";
        }
        catch (SyncApiException e)
        {
            _error = e.Error == "invite_required" && !_create ? e.Message : e.Message;
        }
        finally
        {
            _busy = false;
            Rebuild();
        }
    }

    private async Task FirstSync(bool serverHasData)
    {
        bool serverOnly = false;
        int local = DataStore.SyncStores.Sum(s => s.Count);
        if (serverHasData && local > 0)
        {
            var choose = new SyncMergeDialog(local) { Owner = Window.GetWindow(this) };
            if (choose.ShowDialog() != true) choose = new SyncMergeDialog(local); // closed: combine
            serverOnly = choose.UseServerOnly;
        }
        if (await SyncEngine.FirstSyncAsync(serverOnly))
            Toaster.Show(serverOnly ? "Signed in. This PC now has your account's data." : "Signed in and synced. Changes now go to your other devices automatically.");
    }

    private async Task Recover()
    {
        var dlg = new SyncRecoverDialog(_user) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        _error = null;
        _busy = true;
        Rebuild();
        try
        {
            var session = await SyncEngine.RecoverAsync(_url, dlg.Username, dlg.RecoveryKey, dlg.NewPassword);
            Toaster.Show("New password set. Your other devices need to sign in with it.");
            await FirstSync(session.LatestSeq > 0);
        }
        catch (SyncApiException e) { _error = e.Message; }
        finally
        {
            _busy = false;
            Rebuild();
        }
    }

    // ----- Connected -----

    private void BuildConnected()
    {
        var s = SyncEngine.Settings!;
        var state = SyncEngine.Status;
        int pending = SyncEngine.PendingCount;
        bool ok = state is SyncEngine.State.Idle or SyncEngine.State.Syncing;
        Add(PageKit.KpiRow(
            PageKit.Kpi("Status", SyncEngine.StatusText, ok ? "Everything is encrypted before upload" : SyncEngine.Problem, hero: true),
            PageKit.Kpi("Server", s.ServerName, s.ServerUrl.TrimEnd('/')),
            PageKit.Kpi("Waiting to sync", pending == 0 ? "Nothing" : pending.ToString("N0"), pending == 0 ? "All changes are on the server" : "Sent at the next sync"),
            PageKit.Kpi("Devices", _account?.Devices.Count.ToString() ?? "…", "Signed in to " + s.Username)));

        var cols = Ui.Columns(1, 0.04, 1);
        var left = new StackPanel();
        var right = new StackPanel().At(2);
        cols.Children.Add(left);
        cols.Children.Add(right);

        // Sync now
        var now = new StackPanel();
        if (!ok && SyncEngine.Problem != null) now.Children.Add(Banner(state == SyncEngine.State.Paused ? "" : "", SyncEngine.Problem, danger: state != SyncEngine.State.Paused));
        now.Children.Add(Ui.Text(state == SyncEngine.State.Syncing ? "Syncing now…" :
            SyncEngine.LastSync is DateTime t ? $"Last synced {t:dddd, MMMM d} at {t:h:mm tt}. WillowWallet syncs a few seconds after each change and every minute while it's open." :
            "Not synced yet.", muted: true).Also(x => { x.TextWrapping = TextWrapping.Wrap; x.Margin = new Thickness(0, 0, 0, 14); }));
        var syncNow = PageKit.Primary("", state == SyncEngine.State.Syncing ? "Syncing…" : "Sync now", async (_, _) =>
        {
            if (await SyncEngine.SyncNowAsync(manual: true)) Toaster.Show("Synced");
        });
        syncNow.IsEnabled = state != SyncEngine.State.Syncing;
        syncNow.HorizontalAlignment = HorizontalAlignment.Left;
        now.Children.Add(syncNow);
        left.Children.Add(PageKit.Card(now, "Sync"));

        // Account
        var acct = new StackPanel();
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int r = 0;
        foreach (var (k, v) in new[] { ("Username", s.Username), ("Server", s.ServerUrl.TrimEnd('/')), ("This PC", Environment.MachineName),
                     ("On the server", _account == null ? "…" : $"{_account.Records:N0} records · {FileVault.SizeText(_account.FileBytes)} of files") })
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var key = Ui.Text(k, muted: true).Also(x => { x.Margin = new Thickness(0, 0, 18, 8); Grid.SetRow(x, r); });
            var val = Ui.Text(v).Also(x => { x.Margin = new Thickness(0, 0, 0, 8); x.TextWrapping = TextWrapping.Wrap; Grid.SetRow(x, r); Grid.SetColumn(x, 1); });
            grid.Children.Add(key);
            grid.Children.Add(val);
            r++;
        }
        acct.Children.Add(grid);
        var buttons = new WrapPanel();
        buttons.Children.Add(PageKit.Ghost("", "Change password", async (_, _) => await ChangePassword()).Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
        buttons.Children.Add(PageKit.Ghost("", "Stop syncing on this PC", async (_, _) => await StopSyncing()).Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
        acct.Children.Add(buttons);
        left.Children.Add(PageKit.Card(acct, "Account"));

        // Devices
        var devs = new StackPanel();
        if (_account == null) devs.Children.Add(Ui.Text("Loading…", muted: true));
        else
        {
            bool first = true;
            foreach (var d in _account.Devices)
            {
                var row = new DockPanel { Margin = new Thickness(0, first ? 0 : 6, 0, 6) };
                if (!first) devs.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4) }.Also(b => b.SetResourceReference(Border.BackgroundProperty, "Brush.Border")));
                first = false;
                var tile = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 14, 0) };
                tile.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
                tile.Child = Ui.Text(d.Platform.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "" : "", size: 17, brushKey: "Brush.Accent")
                    .Also(t => { t.Style = Res("Icon"); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
                DockPanel.SetDock(tile, Dock.Left);
                row.Children.Add(tile);
                if (d.IsThisDevice)
                {
                    var pill = PageKit.Pill("This PC", "Brush.Accent");
                    pill.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(pill, Dock.Right);
                    row.Children.Add(pill);
                }
                else
                {
                    string id = d.Id, name = d.Name;
                    var signOut = PageKit.Link("Sign out", () => _ = SignOutDevice(id, name));
                    signOut.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(signOut, Dock.Right);
                    row.Children.Add(signOut);
                }
                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                words.Children.Add(Ui.Text(d.Name, weight: FontWeights.SemiBold, size: 14));
                words.Children.Add(Ui.Text($"{d.Platform} {d.AppVersion} · {(d.IsThisDevice ? "now" : SyncEngine.Ago(d.LastSeen.ToLocalTime()))}", muted: true, size: 12.5));
                row.Children.Add(words);
                devs.Children.Add(row);
            }
            devs.Children.Add(Ui.Text("To add your phone, install WillowWallet for Android and sign in with the same server, username and password.", muted: true, size: 12.5)
                .Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 12, 0, 0); }));
        }
        right.Children.Add(PageKit.Card(devs, "Devices on this account"));
        Add(cols);

        if (_account == null || DateTime.Now - _accountAt > TimeSpan.FromSeconds(30)) _ = LoadAccount();
    }

    private async Task LoadAccount()
    {
        _accountAt = DateTime.Now;
        try
        {
            _account = await SyncEngine.AccountAsync();
            Rebuild();
        }
        catch (SyncApiException) { }
    }

    private async Task ChangePassword()
    {
        var dlg = new SyncPasswordDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await SyncEngine.ChangePasswordAsync(dlg.Current, dlg.Next);
            Toaster.Show("Password changed. Your other devices need to sign in with the new one.");
            _account = null;
            Rebuild();
        }
        catch (SyncApiException e) { Toaster.Show(e.Message); }
    }

    private async Task SignOutDevice(string id, string name)
    {
        var ok = new ConfirmDialog($"Sign out {name}?", "It stops syncing until someone signs in on it again. Its own copy of the data stays on it.", "Sign out") { Owner = Window.GetWindow(this) };
        if (ok.ShowDialog() != true) return;
        try
        {
            await SyncEngine.SignOutDeviceAsync(id);
            _account = null;
            Rebuild();
        }
        catch (SyncApiException e) { Toaster.Show(e.Message); }
    }

    private async Task StopSyncing()
    {
        var ok = new ConfirmDialog("Stop syncing on this PC?", "Your data stays on this PC and WillowWallet keeps working as before. The copy on the server stays too, so you can sign in again any time.", "Stop syncing") { Owner = Window.GetWindow(this) };
        if (ok.ShowDialog() != true) return;
        await SyncEngine.SignOutAsync();
        _account = null;
        _checked = null;
        Toaster.Show("Sync is off. Everything is saved on this PC.");
        Rebuild();
    }

    // ----- Bits -----

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };

    private static TextBlock Hint(string text) => Ui.Text(text, muted: true, size: 12.5).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 5, 0, 0); });

    private static void AutomationName(UIElement e, string name) => System.Windows.Automation.AutomationProperties.SetName(e, name);

    private Border Banner(string glyph, string text, bool danger)
    {
        var border = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 14), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BorderBrushProperty, danger ? "Brush.Danger" : "Brush.Accent");
        border.SetResourceReference(Border.BackgroundProperty, danger ? "Brush.Surface" : "Brush.AccentSoft");
        var row = new DockPanel();
        var icon = Ui.Text(glyph, size: 15, brushKey: danger ? "Brush.Danger" : "Brush.Accent").Also(t => { t.Style = Res("Icon"); t.Margin = new Thickness(0, 1, 10, 0); t.VerticalAlignment = VerticalAlignment.Top; });
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(Ui.Text(text, brushKey: danger ? "Brush.Danger" : "Brush.Text").Also(t => t.TextWrapping = TextWrapping.Wrap));
        border.Child = row;
        return border;
    }
}
