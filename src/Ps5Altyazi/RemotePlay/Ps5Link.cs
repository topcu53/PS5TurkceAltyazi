// SPDX-License-Identifier: AGPL-3.0-only
// Altyazı+: PS5'e doğrudan bağlantının kayıtları ve Türkçe açıklamaları.
using System;
using System.Text.Json;
using CouchSubs.RemotePlay;
using Ps5Altyazi.Settings;

namespace Ps5Altyazi.RemotePlayLink;

/// <summary>PSN hesap kimliği ve eşlenmiş PS5 bilgisinin (DPAPI ile şifreli) okunup yazılması.</summary>
public static class Ps5Link
{
    /// <summary>Yerel kütüphane (altyazi_rp.dll) bu programda var ve yüklenebiliyor mu.</summary>
    public static bool LibraryAvailable
    {
        get
        {
            try { return NativeMethods.IsAvailable; }
            catch { return false; }
        }
    }

    public static byte[]? AccountId(AppSettings s)
    {
        string? b64 = SecretStore.Unprotect(s.PsnAccountIdProtected);
        if (b64 == null) return null;
        try
        {
            var bytes = Convert.FromBase64String(b64);
            return bytes.Length == 8 ? bytes : null;
        }
        catch { return null; }
    }

    public static void SaveAccountId(AppSettings s, byte[] accountId) =>
        s.PsnAccountIdProtected = SecretStore.Protect(Convert.ToBase64String(accountId));

    private sealed class Stored
    {
        public string Host { get; set; } = "";
        public string Name { get; set; } = "";
        public string RegistKey { get; set; } = "";
        public string Morning { get; set; } = "";
        public string? HostId { get; set; }
    }

    public static RegisteredConsole? LoadConsole(AppSettings s)
    {
        string? json = SecretStore.Unprotect(s.Ps5ConsoleProtected);
        if (json == null) return null;
        try
        {
            var d = JsonSerializer.Deserialize<Stored>(json);
            if (d == null || d.Host.Length == 0) return null;
            return new RegisteredConsole(d.Host, d.Name, Convert.FromBase64String(d.RegistKey), Convert.FromBase64String(d.Morning), d.HostId);
        }
        catch { return null; }
    }

    public static void SaveConsole(AppSettings s, RegisteredConsole c) =>
        s.Ps5ConsoleProtected = SecretStore.Protect(JsonSerializer.Serialize(new Stored
        {
            Host = c.Host, Name = c.Name, RegistKey = Convert.ToBase64String(c.RegistKey),
            Morning = Convert.ToBase64String(c.Morning), HostId = c.HostId
        }));

    public static string? LoadPasscode(AppSettings s) => SecretStore.Unprotect(s.Ps5LoginPasscodeProtected);

    /// <summary>Doğrudan bağlantı kullanılabilir mi (kütüphane var, hesap ve PS5 eşlenmiş).</summary>
    public static bool IsReady(AppSettings s) => LibraryAvailable && AccountId(s) != null && LoadConsole(s) != null;

    // ───── Türkçe açıklamalar ─────

    public static string Describe(DisconnectReason? r)
    {
        if (r == null) return "Bağlantı kapandı.";
        string d = string.IsNullOrEmpty(r.Detail) ? "" : $" ({r.Detail})";
        return r.Kind switch
        {
            DisconnectKind.Stopped => "Bağlantı durduruldu.",
            DisconnectKind.ConsoleAsleep => "PS5 kapalı ya da uyandırılamadı. PS5'te Ayarlar › Sistem › Güç Tasarrufu › Dinlenme Modunda Kullanılabilen Özellikler'de 'İnternete Bağlı Kal' ve 'PS5'in Ağ Üzerinden Açılmasını Etkinleştir' açık olmalı.",
            DisconnectKind.InUse => "Uzaktan Oynatma başka bir cihazda kullanılıyor (ör. PS Remote Play uygulaması açık). Onu kapatın.",
            DisconnectKind.Refused => "PS5 bağlantıyı reddetti (yeni uyanıyor olabilir); tekrar deneniyor.",
            DisconnectKind.Network => "Ağ bağlantısı koptu; tekrar deneniyor." + d,
            DisconnectKind.LoginPasscode => "PS5 bu hesap için 4 haneli giriş şifresi istiyor. PS5 Bağlantısı penceresinde şifreyi girin.",
            DisconnectKind.LoginPasscodeIncorrect => "PS5 giriş şifresini kabul etmedi. PS5 Bağlantısı penceresinde doğru şifreyi girin.",
            DisconnectKind.NotFound => "PS5 ağda bulunamadı" + d + ". PS5 açık ya da dinlenme modunda ve bilgisayarla aynı ağda mı?",
            DisconnectKind.RegistrationInvalid => "PS5 bu bilgisayarın eşlemesini kabul etmedi. PS5 Bağlantısı penceresinden yeniden eşleyin." + d,
            DisconnectKind.Rejected => "PS5 bağlantı isteğini reddetti" + d + ". İkinci hesapta Ayarlar › Sistem › Uzaktan Oynatma › 'Uzaktan Oynatmayı Etkinleştir' açık mı?",
            DisconnectKind.ConsoleRemotePlayCrashed => "PS5'teki Uzaktan Oynatma hizmeti çöktü. PS5'i tamamen kapatıp açın.",
            DisconnectKind.ConsoleClosed => "PS5 bağlantıyı kendisi kapattı (çoğunlukla PS5'te kullanıcı değişince)" + d + ".",
            DisconnectKind.ConsoleClosedRepeatedly => "PS5 bağlantıyı tekrar tekrar kapattı. Oyunu ana hesabınızla oynayın; ikinci hesap yalnız Altyazı+ içindir.",
            DisconnectKind.ReconnectGaveUp => "Bağlantı art arda koptu, yeniden deneme bırakıldı. PS5'i modeme kabloyla bağlamak çok yardımcı olur.",
            _ => "Bağlantı kapandı" + d + ".",
        };
    }

    public static string Describe(RegistrationException e) => e.Failure switch
    {
        RegistrationFailure.WrongAccount => "PS5 hesabı tanımadı: kod, programda giriş yapılan (ikinci) hesabın PS5 kullanıcısından alınmalı. PS5'te o kullanıcıya geçip kodu yeniden alın.",
        RegistrationFailure.WrongCode => "PS5 kodu kabul etmedi: kod yanlış ya da süresi dolmuş. 'Cihazı Bağla' ekranındaki güncel kodu girin.",
        RegistrationFailure.NoAnswer => "PS5 yanıt vermedi. PS5 açık mı, bilgisayarla aynı ağda mı, 'Cihazı Bağla' ekranı açık mı?",
        RegistrationFailure.InUse => "Uzaktan Oynatma başka bir cihazda kullanılıyor. PS Remote Play uygulamasını kapatın.",
        RegistrationFailure.ConsoleCrashed => "PS5'in Uzaktan Oynatma hizmeti hata verdi. PS5'i yeniden başlatıp tekrar deneyin.",
        RegistrationFailure.VersionMismatch => "PS5'in sistem sürümü tanınmadı (PS5 güncel mi?).",
        RegistrationFailure.NoReply => "PS5 isteği aldı ama zamanında yanıt vermedi. Tekrar deneyin.",
        RegistrationFailure.BadAddress => "PS5'in adresi geçersiz. IP adresini kontrol edin.",
        RegistrationFailure.PairingClosed => "PS5 eşleştirmeyi reddetti: 'Cihazı Bağla' ekranı açık değil ya da kodun süresi dolmuş.",
        RegistrationFailure.ConnectFailed => "PS5 bulundu ama eşleştirme bağlantısı açılamadı (güvenlik duvarı?).",
        RegistrationFailure.NoFreePort => "Bilgisayarda boş ağ bağlantısı kalmadı. Bilgisayarı yeniden başlatın.",
        RegistrationFailure.Canceled => "Eşleştirme iptal edildi.",
        _ => "Eşleştirme başarısız" + (e.Detail != null ? $" ({e.Detail})" : "") + ".",
    };

    public static string Describe(PsnAccountException e) => e.Failure switch
    {
        PsnLoginFailure.RateLimited => "Sony çok fazla deneme olduğunu söyledi. Birkaç dakika bekleyip tekrar deneyin.",
        PsnLoginFailure.Network => "Sony'ye ulaşılamadı (internet?).",
        PsnLoginFailure.Refused => "Sony girişi tamamlamadı" + (e.Detail != null ? $" ({e.Detail})" : "") + ". Tekrar giriş yapın.",
        _ => "PSN girişi tamamlanamadı" + (e.Detail != null ? $" ({e.Detail})" : "") + ".",
    };
}
