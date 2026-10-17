using System.Security.Cryptography;

namespace OrchDesk.Tests;

public sealed class DpapiFactAttribute : Xunit.FactAttribute
{
    public DpapiFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) { Skip = "DPAPI requires Windows."; return; }
        try { ProtectedData.Protect([1, 2, 3], null, DataProtectionScope.CurrentUser); }
        catch (CryptographicException) { Skip = "DPAPI CurrentUser is unavailable in this process user profile (sandbox restriction)."; }
    }
}
