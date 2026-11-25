using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ps5Altyazi.Ps5;

/// <summary>
/// PSN girişi: Sony'nin kendi giriş sayfası açılır; giriş bitince Sony tarayıcıyı Remote Play'in yönlendirme adresine
/// gönderir. Adresteki tek kullanımlık kod, hesabın 8 baytlık kimliğine çevrilir (PS5 eşleştirmesi bunu ister).
/// Şifre ve oturum anahtarı programa gelmez / saklanmaz. İstemci bilgileri Sony'nin PS Remote Play uygulamasınınkidir.
/// </summary>
public static class PsnLogin
{
    private const string ClientId = "ba495a24-818c-472b-b12d-ff231c1b5745";
    private const string ClientSecret = "mvaiZkRsAsI1IBkY";
    private const string AuthBase = "https://auth.api.sonyentertainmentnetwork.com/2.0/oauth/";
    private const string Scope = "psn:clientapp referenceDataService:countryConfig.read pushNotification:webSocket.desktop.connect sessionManager:remotePlaySession.system.update";
    public const string RedirectUrl = "https://remoteplay.dl.playstation.net/remoteplay/redirect";

    private static readonly HttpClient Http = new(new SocketsHttpHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(25) };

    public static string LoginUrl =>
        AuthBase + "authorize?" + Form(new()
        {
            ["service_entity"] = "urn:service-entity:psn",
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = RedirectUrl,
            ["scope"] = Scope,
            ["request_locale"] = "en_US",
            ["ui"] = "pr",
            ["service_logo"] = "ps",
            ["layout_type"] = "popup",
            ["smcid"] = "remoteplay",
            ["prompt"] = "always",
            ["PlatformPrivacyWs1"] = "minimal",
        });

    public static bool IsRedirect(string url) => url.StartsWith(RedirectUrl, StringComparison.OrdinalIgnoreCase);

    /// <summary>Kullanıcının yapıştırdığı metinde yönlendirme adresini bulur.</summary>
    public static string? FindRedirect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        int i = text.IndexOf(RedirectUrl, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        int end = i;
        while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '"' && text[end] != '\'') end++;
        return text[i..end];
    }

    private static string? Param(string url, string name)
    {
        int q = url.IndexOf('?');
        if (q < 0) return null;
        foreach (var part in url[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            string key = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]);
            if (key == name) return eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
        }
        return null;
    }

    /// <summary>Yönlendirme adresinden hesap kimliği (8 bayt). Hata olursa açıklamalı istisna.</summary>
    public static async Task<byte[]> AccountIdAsync(string redirectUrl)
    {
        string? code = Param(redirectUrl, "code");
        if (string.IsNullOrEmpty(code))
        {
            string? err = Param(redirectUrl, "error_description") ?? Param(redirectUrl, "error");
            throw new InvalidOperationException(err != null ? $"Sony girişi tamamlamadı: {err}" : "Adreste giriş kodu yok.");
        }

        var basic = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(ClientId + ":" + ClientSecret)));
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, AuthBase + "token")
        {
            Content = new StringContent(Form(new()
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["scope"] = Scope,
                ["redirect_uri"] = RedirectUrl,
            }), Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        tokenReq.Headers.Authorization = basic;
        string accessToken;
        using (var doc = await SendJson(tokenReq))
        {
            if (!doc.RootElement.TryGetProperty("access_token", out var t) || t.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("Sony giriş anahtarı vermedi: " + Error(doc.RootElement));
            accessToken = t.GetString()!;
        }

        using var infoReq = new HttpRequestMessage(HttpMethod.Get, AuthBase + "token/" + Uri.EscapeDataString(accessToken));
        infoReq.Headers.Authorization = basic;
        using var info = await SendJson(infoReq);
        if (!info.RootElement.TryGetProperty("user_id", out var uid))
            throw new InvalidOperationException("Sony hesap kimliğini vermedi.");
        string idText = uid.ValueKind == JsonValueKind.String ? uid.GetString()! : uid.GetRawText();
        if (!ulong.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id))
            throw new InvalidOperationException("Hesap kimliği anlaşılamadı.");
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, id);
        return bytes;
    }

    private static async Task<JsonDocument> SendJson(HttpRequestMessage req)
    {
        HttpResponseMessage res;
        try { res = await Http.SendAsync(req); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("Sony'ye ulaşılamadı (internet?): " + ex.Message);
        }
        using (res)
        {
            string body = await res.Content.ReadAsStringAsync();
            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(body); } catch (JsonException) { }
            if (!res.IsSuccessStatusCode)
            {
                string detail = doc != null ? Error(doc.RootElement) : body.Length > 160 ? body[..160] : body;
                doc?.Dispose();
                if ((int)res.StatusCode == 429) throw new InvalidOperationException("Sony çok fazla deneme dedi; birkaç dakika sonra tekrar deneyin.");
                throw new InvalidOperationException($"Sony isteği reddetti (HTTP {(int)res.StatusCode}): {detail}");
            }
            return doc ?? throw new InvalidOperationException("Sony'nin yanıtı okunamadı.");
        }
    }

    private static string Error(JsonElement e)
    {
        string? Get(string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) ? v.ToString() : null;
        return string.Join(" – ", new[] { Get("error"), Get("error_description") }.Where0());
    }

    private static IEnumerable<string> Where0(this string?[] items)
    {
        foreach (var s in items) if (!string.IsNullOrEmpty(s)) yield return s;
    }

    private static string Form(Dictionary<string, string> values)
    {
        var sb = new StringBuilder();
        foreach (var kv in values)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
        }
        return sb.ToString();
    }
}
