using System.IO;
using System.Security.Cryptography;
using CoinPilot.Data;
using Windows.Security.Credentials.UI;

namespace CoinPilot.Services;

/// <summary>
/// Unlocking with Windows Hello (fingerprint, face or the Windows PIN) when PIN/password protection is on.
///
/// Turning it on keeps a copy of the data key on this PC, protected for your Windows account (DPAPI), in
/// %AppData%\WillowWallet\hello.bin. It's only used after Windows Hello confirms it's you. It's not part of the data
/// folder, so it's never in a backup and never synced; your PIN or password and recovery key still work as before.
/// </summary>
public static class WindowsHello
{
    private static readonly byte[] Entropy = "WillowWallet Windows Hello v1"u8.ToArray();

    private static string FilePath => Path.Combine(AppPaths.Root, "hello.bin");

    /// <summary>Turned on for this PC (and protection is on).</summary>
    public static bool IsOn => DataLock.IsEnabled && File.Exists(FilePath);

    /// <summary>Windows Hello is set up on this PC (a fingerprint reader, camera or Windows PIN).</summary>
    public static async Task<bool> IsAvailableAsync()
    {
        try { return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available; }
        catch { return false; }
    }

    /// <summary>Asks Windows Hello. Returns null when it's you, or a message saying why not.</summary>
    public static async Task<string?> VerifyAsync(string message)
    {
        try
        {
            var result = await UserConsentVerifier.RequestVerificationAsync(message);
            return result switch
            {
                UserConsentVerificationResult.Verified => null,
                UserConsentVerificationResult.Canceled => "Canceled.",
                UserConsentVerificationResult.DeviceNotPresent or UserConsentVerificationResult.NotConfiguredForUser =>
                    "Windows Hello isn't set up on this PC. Set it up in Windows Settings → Accounts → Sign-in options.",
                UserConsentVerificationResult.DisabledByPolicy => "Windows Hello is turned off on this PC by policy.",
                UserConsentVerificationResult.RetriesExhausted => "Too many tries. Use your PIN or password.",
                _ => "Windows Hello couldn't confirm it's you.",
            };
        }
        catch (Exception ex)
        {
            return $"Windows Hello isn't available ({ex.Message}).";
        }
    }

    /// <summary>Turns it on (WillowWallet must be unlocked). Returns null, or why it couldn't.</summary>
    public static async Task<string?> TurnOnAsync()
    {
        if (!DataLock.IsEnabled) return "Turn on a PIN or password first.";
        var key = DataLock.CopyKey();
        if (key == null) return "Unlock WillowWallet first.";
        if (!await IsAvailableAsync()) return "Windows Hello isn't set up on this PC. Set it up in Windows Settings → Accounts → Sign-in options.";
        var problem = await VerifyAsync("Turn on Windows Hello for WillowWallet");
        if (problem != null) return problem;
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllBytes(FilePath, ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser));
            return null;
        }
        catch (Exception ex)
        {
            return $"Couldn't turn it on ({ex.Message}).";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static void TurnOff()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Unlocks with Windows Hello. Returns null when unlocked, or a message.</summary>
    public static async Task<string?> UnlockAsync()
    {
        if (!IsOn) return "Windows Hello isn't turned on for WillowWallet.";
        var problem = await VerifyAsync("Unlock WillowWallet");
        if (problem != null) return problem;
        byte[] key;
        try
        {
            key = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
        }
        catch (Exception)
        {
            TurnOff();
            return "Windows Hello needs to be turned on again. Unlock with your PIN or password, then turn it on in Settings → Security.";
        }
        if (DataLock.UnlockWithKey(key)) return null;
        // The data changed (restored from a backup or protection was reset): this key no longer fits.
        TurnOff();
        return "Your data was changed since Windows Hello was turned on. Unlock with your PIN or password, then turn it on again in Settings → Security.";
    }
}
