using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace OrchDesk.Core.Config;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}

/// <summary>
/// Windows DPAPI, scoped to the current Windows user: the encrypted key in config.json is useless if copied to
/// another account or machine. That's also why each machine (PC and laptop) needs its keys entered once.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OrchDesk.v1");

    public string Protect(string plaintext) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser));

    public string Unprotect(string protectedText) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser));
}

public static class SecretExtensions
{
    public static string? TryUnprotect(this ISecretProtector p, string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try { return p.Unprotect(value); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }
}
