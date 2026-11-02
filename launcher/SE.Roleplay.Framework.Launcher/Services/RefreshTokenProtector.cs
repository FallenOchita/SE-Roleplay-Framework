using System.Security.Cryptography;
using System.Text;

namespace SE.Roleplay.Framework.Launcher.Services;

/// <summary>Protects refresh tokens with Windows DPAPI for the current Windows user.</summary>
public static class RefreshTokenProtector
{
    public static string Protect(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("A token is required.", nameof(token));
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string protectedToken)
    {
        var protectedBytes = Convert.FromBase64String(protectedToken);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
    }
}
