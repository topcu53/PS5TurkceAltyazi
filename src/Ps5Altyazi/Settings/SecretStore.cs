using System;
using System.Security.Cryptography;
using System.Text;

namespace Ps5Altyazi.Settings;

/// <summary>
/// API anahtarlarını Windows DPAPI ile şifreler (yalnızca bu Windows kullanıcısı çözebilir).
/// settings.json içinde düz metin anahtar tutulmaz.
/// </summary>
public static class SecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PS5TurkceAltyazi.v1");

    public static string Protect(string secret) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser));

    public static string? Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), Entropy,
                DataProtectionScope.CurrentUser));
        }
        catch
        {
            return null;
        }
    }
}
