using System.Windows;
using System.Windows.Controls;

namespace CoinPilot.Views;

/// <summary>First sign-in when this PC and the server both have data: combine them, or use only the server's.</summary>
public sealed class SyncMergeDialog : DialogShell
{
    private readonly RadioButton _combine, _server;
    public bool UseServerOnly => _server.IsChecked == true;

    public SyncMergeDialog(int localRecords)
        : base("This PC and the server both have data", $"There are {localRecords:N0} records on this PC, and your account on the server already has data from another device. How should WillowWallet put them together?",
               allowDelete: false, saveText: "Continue", width: 560)
    {
        HeaderGlyph = "";
        _combine = Option("Combine them (recommended)", "Everything from this PC and the server ends up on both. When the same item was changed in both places, the newest change wins. If both have the same sample or test data, you may see it twice.", true);
        _server = Option("Use only the server's data", "This PC's records are replaced with what's on the server. Use this when this PC's data is old or was only for trying WillowWallet out.", false);
        Body.Children.Add(_combine);
        Body.Children.Add(_server);
    }

    private RadioButton Option(string title, string text, bool isChecked)
    {
        var panel = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        var detail = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), MaxWidth = 440 };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        panel.Children.Add(detail);
        return new RadioButton { Content = panel, GroupName = "merge", IsChecked = isChecked, Style = (Style)FindResource("CardRadio") };
    }

    protected override string? TrySave() => null;
}

/// <summary>Forgot the sync password: username, recovery key and a new password.</summary>
public sealed class SyncRecoverDialog : DialogShell
{
    private readonly TextBox _user, _key;
    private readonly PasswordBox _pass, _confirm;
    public string Username => _user.Text.Trim();
    public string RecoveryKey => _key.Text.Trim();
    public string NewPassword => _pass.Password;

    public SyncRecoverDialog(string username)
        : base("Forgot your sync password?", "Use the recovery key you saved when you created your account. It opens your encrypted data so you can set a new password. Your other devices will need to sign in again.",
               allowDelete: false, saveText: "Set new password", width: 520)
    {
        HeaderGlyph = "";
        _user = AddField("Username", new TextBox { Text = username }, required: true);
        _key = AddField("Recovery key", new TextBox { FontFamily = new System.Windows.Media.FontFamily("Consolas"), CharacterCasing = CharacterCasing.Upper }, "Looks like ABCD-EFGH-IJKL-… (8 groups of 4).", required: true);
        _pass = AddField("New password", new PasswordBox(), "At least 8 characters.", required: true);
        _confirm = AddField("Type it again", new PasswordBox(), required: true);
    }

    protected override string? TrySave()
    {
        if (Username.Length == 0) return "Enter your username.";
        if (WillowWallet.Sync.SyncCrypto.ParseRecoveryKey(RecoveryKey) == null) return "That isn't a recovery key. It has 32 letters and numbers, like ABCD-EFGH-….";
        if (_pass.Password.Length < 8) return "Use at least 8 characters for your new password.";
        if (_pass.Password != _confirm.Password) return "The two passwords don't match.";
        return null;
    }
}

/// <summary>Change the sync password (re-locks the data key; other devices sign in again).</summary>
public sealed class SyncPasswordDialog : DialogShell
{
    private readonly PasswordBox _current, _next, _confirm;
    public string Current => _current.Password;
    public string Next => _next.Password;

    public SyncPasswordDialog()
        : base("Change your sync password", "Your other devices will be signed out and need the new password. Your recovery key stays the same.",
               allowDelete: false, saveText: "Change password", width: 500)
    {
        HeaderGlyph = "";
        _current = AddField("Current password", new PasswordBox(), required: true);
        _next = AddField("New password", new PasswordBox(), "At least 8 characters.", required: true);
        _confirm = AddField("Type it again", new PasswordBox(), required: true);
    }

    protected override string? TrySave()
    {
        if (_current.Password.Length == 0) return "Enter your current password.";
        if (_next.Password.Length < 8) return "Use at least 8 characters for your new password.";
        if (_next.Password != _confirm.Password) return "The two new passwords don't match.";
        return null;
    }
}
