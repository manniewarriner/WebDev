using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace OrchDesk.Core.Safety;

// Text that came from outside (skill files, memory notes, chat messages) is cleaned before a model sees it. Invisible characters are how a
// hidden instruction rides along in a file that looks harmless: Unicode "tag" characters, zero-width characters and bidirectional overrides.
public static class TextSafety
{
    public static bool IsInvisible(Rune r) => r.Value is >= 0xE0000 and <= 0xE007F      // tag characters (hidden ASCII)
        or >= 0x200B and <= 0x200F or >= 0x202A and <= 0x202E or >= 0x2060 and <= 0x2064 or >= 0x2066 and <= 0x2069 // zero-width, bidi marks and overrides
        or 0xFEFF or 0x00AD or 0x180E or 0x034F;
    public static bool HasTagCharacters(string s) { foreach (var r in s.EnumerateRunes()) if (r.Value is >= 0xE0000 and <= 0xE007F) return true; return false; }

    // Removes invisible characters and control characters other than tab and newlines.
    public static string Strip(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        var sb = new StringBuilder(s.Length);
        foreach (var r in s.EnumerateRunes())
        {
            if (IsInvisible(r)) continue;
            if (r.Value < 0x20 && r.Value is not (0x09 or 0x0A or 0x0D)) continue;
            if (r.Value is >= 0x7F and <= 0x9F) continue;
            sb.Append(r.ToString());
        }
        return sb.ToString();
    }

    // Text that tells a model to download, install, fetch URLs or save memory. Fine in a skill you wrote yourself, suspicious from anywhere else.
    private static readonly Regex Risky = new(@"\b(curl|wget|invoke-webrequest|iwr|invoke-expression|iex|certutil|bitsadmin|npm\s+(i|install)|pip\s+install|choco\s+install|winget\s+install)\b|https?://|\bremember tool\b|\bmemory\.md\b|\bpowershell\s+-enc", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    public static bool LooksRisky(string s) { try { return Risky.IsMatch(s); } catch (RegexMatchTimeoutException) { return true; } }
}

/// <summary>Generic secret patterns (bearer tokens, sk- keys, key=value pairs) removed from text that leaves the app.</summary>
public static class Redaction
{
    public static string Clean(string text) => Regex.Replace(Regex.Replace(Regex.Replace(text, @"\bBearer\s+\S+", "Bearer [REDACTED]", RegexOptions.IgnoreCase), @"\bsk[-_][\w-]+", "[REDACTED]"), @"((?:api[_-]?key|token|secret|password)\s*[=:]\s*[""']?)[^\s""',}]+", "$1[REDACTED]", RegexOptions.IgnoreCase);
}

/// <summary>Refuses local model jobs when the PC is short on free RAM (baseline MinimumFreeRamGb).</summary>
public static class MemoryGuard
{
    [StructLayout(LayoutKind.Sequential)] private class MemoryStatus { public uint Length = (uint)Marshal.SizeOf<MemoryStatus>(); public uint Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);
    public static double FreeGb() { var m = new MemoryStatus(); if (!OperatingSystem.IsWindows() || !GlobalMemoryStatusEx(m)) return double.MaxValue; return m.AvailablePhysical / Math.Pow(1024, 3); }
    public static void Ensure(double minimum, Func<double>? free = null) { var gb = (free ?? FreeGb)(); if (gb < minimum) throw new InvalidOperationException($"Local job refused: {gb:F1} GB free RAM; {minimum:F1} GB required."); }
}
