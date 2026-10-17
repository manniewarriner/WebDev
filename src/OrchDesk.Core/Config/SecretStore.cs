using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace OrchDesk.Core.Config;

/// <summary>
/// secrets.bin as written by the original OrchDesk build: a JSON Dictionary&lt;keyId, secret&gt; protected with Windows DPAPI
/// (CurrentUser scope, no entropy). The file is only rewritten (atomically, via .tmp + move) when a value actually changes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SecretStore(string directory)
{
    public const string FileName = "secrets.bin";
    private readonly string path = Path.Combine(directory, FileName);
    private readonly object gate = new();
    private Dictionary<string, string>? cache;
    public string FilePath => path;

    private Dictionary<string, string> Read()
    {
        lock (gate)
            return cache ??= File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) ?? []
                : [];
    }

    public string? Get(string id) => Read().GetValueOrDefault(id);

    /// <summary>True when a non-empty value is stored. An unreadable store (another Windows user, damaged file) counts as empty.</summary>
    public bool Has(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try { return !string.IsNullOrEmpty(Get(id)); }
        catch (Exception e) when (e is CryptographicException or JsonException or IOException) { return false; }
    }

    public IReadOnlyCollection<string> Ids() => Read().Keys.ToArray();

    /// <summary>Stores or (with an empty value) removes a secret. Returns false when nothing changed and the file was left alone.</summary>
    public bool Set(string id, string? value)
    {
        lock (gate)
        {
            var current = Read();
            var existing = current.GetValueOrDefault(id);
            if (string.IsNullOrEmpty(value) ? existing is null : existing == value) return false;
            var next = new Dictionary<string, string>(current);
            if (string.IsNullOrEmpty(value)) next.Remove(id); else next[id] = value;
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path + ".tmp", ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(next), null, DataProtectionScope.CurrentUser));
            File.Move(path + ".tmp", path, true);
            cache = next;
            return true;
        }
    }

    /// <summary>Replaces every stored secret value in <paramref name="text"/> with [REDACTED], then applies the generic patterns.</summary>
    public string Redact(string text)
    {
        Dictionary<string, string> values;
        try { values = Read(); }
        catch (Exception e) when (e is CryptographicException or JsonException or IOException) { return OrchDesk.Core.Safety.Redaction.Clean(text); }
        foreach (var key in values.Values.Where(x => x.Length > 0).OrderByDescending(x => x.Length)) text = text.Replace(key, "[REDACTED]", StringComparison.Ordinal);
        return OrchDesk.Core.Safety.Redaction.Clean(text);
    }
}

/// <summary>
/// Lets saved provider fields point at a secrets.bin entry instead of holding a DPAPI blob: a value of
/// "secrets.bin:&lt;keyId&gt;" is resolved from the shared store, anything else goes to the normal protector.
/// </summary>
public sealed class SecretStoreProtector(ISecretProtector inner, Func<string, string?> lookup) : ISecretProtector
{
    public const string Prefix = "secrets.bin:";
    public static string Ref(string keyId) => Prefix + keyId;
    public static bool IsRef(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    [SupportedOSPlatform("windows")]
    public static SecretStoreProtector ForDirectory(string directory)
    {
        var store = new SecretStore(directory);
        return new(new DpapiSecretProtector(), id => { try { return store.Get(id); } catch (Exception e) when (e is CryptographicException or JsonException or IOException) { return null; } }) { Store = store };
    }

    [SupportedOSPlatform("windows")]
    public SecretStore? Store { get; private init; }

    public string Protect(string plaintext) => inner.Protect(plaintext);

    public string Unprotect(string protectedText) =>
        IsRef(protectedText)
            ? lookup(protectedText[Prefix.Length..]) is { Length: > 0 } value ? value : throw new CryptographicException("No secret stored under that key id.")
            : inner.Unprotect(protectedText);
}
