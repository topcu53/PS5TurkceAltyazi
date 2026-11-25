using System;
using System.Text.Json;
using Ps5Altyazi.Settings;

namespace Ps5Altyazi.Ps5;

/// <summary>PSN hesap kimliği ve eşlenmiş PS5 bilgilerinin ayarlarda (DPAPI ile şifreli) saklanması.</summary>
public static class Ps5Store
{
    public static byte[]? AccountId(AppSettings s)
    {
        string? hex = SecretStore.Unprotect(s.PsnAccountIdProtected);
        if (hex == null) return null;
        try { var b = Convert.FromHexString(hex); return b.Length == 8 ? b : null; }
        catch { return null; }
    }

    public static void SaveAccountId(AppSettings s, byte[] id) => s.PsnAccountIdProtected = SecretStore.Protect(Convert.ToHexString(id));

    public static Ps5Pairing? Pairing(AppSettings s)
    {
        string? json = SecretStore.Unprotect(s.Ps5ConsoleProtected);
        if (json == null) return null;
        try
        {
            var p = JsonSerializer.Deserialize<Ps5Pairing>(json);
            return p != null && p.Host.Length > 0 && p.Key.Length == 32 && p.Morning.Length == 32 ? p : null;
        }
        catch { return null; }
    }

    public static void SavePairing(AppSettings s, Ps5Pairing p) => s.Ps5ConsoleProtected = SecretStore.Protect(JsonSerializer.Serialize(p));

    public static string? Passcode(AppSettings s) => SecretStore.Unprotect(s.Ps5LoginPasscodeProtected);

    /// <summary>Doğrudan bağlantı kurulmuş mu (yardımcı program var, hesap ve PS5 eşlenmiş).</summary>
    public static bool IsReady(AppSettings s) => Ps5Helper.Available && AccountId(s) != null && Pairing(s) != null;
}
