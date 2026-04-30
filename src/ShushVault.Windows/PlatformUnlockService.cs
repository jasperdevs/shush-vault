using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Windows.Security.Credentials.UI;

namespace ShushVault.Windows;

internal sealed class PlatformUnlockService
{
    private const string HelloCredentialTarget = "ShushVault.VaultPassphrase";
    private const string DeviceCredentialTarget = "ShushVault.DeviceVaultKey";
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public string GetOrCreateDevicePassphrase()
    {
        var existing = ReadPassphrase(DeviceCredentialTarget);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        WritePassphrase(DeviceCredentialTarget, generated);
        return generated;
    }

    public void SaveDevicePassphrase(string passphrase)
        => WritePassphrase(DeviceCredentialTarget, passphrase);

    public async Task<PlatformUnlockState> GetStateAsync()
    {
        var availability = await UserConsentVerifier.CheckAvailabilityAsync();
        var available = availability is UserConsentVerifierAvailability.Available;
        return new PlatformUnlockState(
            available,
            HasSavedPassphrase(HelloCredentialTarget),
            availability switch
            {
                UserConsentVerifierAvailability.Available => "Windows Hello or PIN is available.",
                UserConsentVerifierAvailability.DeviceNotPresent => "Windows Hello hardware is not available on this device.",
                UserConsentVerifierAvailability.NotConfiguredForUser => "Set up Windows Hello or a PIN in Windows Settings first.",
                UserConsentVerifierAvailability.DisabledByPolicy => "Windows Hello is disabled by policy.",
                _ => "Windows Hello is not available."
            });
    }

    public async Task<string?> ReadPassphraseWithConsentAsync()
    {
        var result = await UserConsentVerifier.RequestVerificationAsync("Unlock Shush Vault");
        return result is UserConsentVerificationResult.Verified
            ? ReadPassphrase(HelloCredentialTarget)
            : null;
    }

    public async Task<bool> SavePassphraseWithConsentAsync(string passphrase)
    {
        var result = await UserConsentVerifier.RequestVerificationAsync("Save Shush Vault passphrase for Windows Hello");
        if (result is not UserConsentVerificationResult.Verified)
        {
            return false;
        }

        WritePassphrase(HelloCredentialTarget, passphrase);
        return true;
    }

    public void DeleteSavedPassphrase()
        => CredDelete(HelloCredentialTarget, CredTypeGeneric, 0);

    private static bool HasSavedPassphrase(string target)
        => ReadPassphrase(target) is not null;

    private static string? ReadPassphrase(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var credentialPointer))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
            return credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    private static void WritePassphrase(string target, string passphrase)
    {
        var bytes = Encoding.Unicode.GetBytes(passphrase);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException("Could not save passphrase to Windows Credential Manager.");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
}

internal sealed record PlatformUnlockState(bool IsAvailable, bool HasSavedPassphrase, string Message);
