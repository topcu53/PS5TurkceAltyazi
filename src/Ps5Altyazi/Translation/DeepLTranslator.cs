using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ps5Altyazi.Translation;

/// <summary>Çeviri motoru soyutlaması (yeni motorlar bu arayüzle eklenir).</summary>
public interface ITranslator
{
    /// <summary>Kalıcı kimlik (ayarlarda bekleme süresi tutmak için): "deepl", "azure", "mymemory".</summary>
    string Id { get; }
    string Name { get; }
    Task<string> TranslateAsync(string text, string? context, CancellationToken ct);
}

/// <summary>Tek istekte birden çok metni çevirebilen motor (ekran çevirisi: menüdeki bütün öğeler bir seferde).</summary>
public interface IBatchTranslator
{
    Task<string[]> TranslateManyAsync(IReadOnlyList<string> texts, string? context, CancellationToken ct);
}

public enum TranslationErrorKind
{
    /// <summary>Ücretsiz kota doldu → bu motor bir süre atlanır, sıradakine geçilir.</summary>
    Quota,
    /// <summary>Anahtar geçersiz → anahtar değişene kadar motor kapalı.</summary>
    Auth,
    /// <summary>Geçici (zaman aşımı, 429, 5xx, internet) → bu istek için sıradakine geçilir.</summary>
    Transient,
    Other
}

/// <summary>Kullanıcıya gösterilebilir çeviri hatası (Türkçe açıklamalı).</summary>
public sealed class TranslationException : Exception
{
    public TranslationErrorKind Kind { get; }
    public int? StatusCode { get; }

    public TranslationException(string message, TranslationErrorKind kind = TranslationErrorKind.Other, int? statusCode = null)
        : base(message)
    {
        Kind = kind;
        StatusCode = statusCode;
    }
}

/// <summary>
/// DeepL API (Free / Pro). İngilizce → Türkçe.
/// Free anahtarlar ":fx" ile biter ve api-free.deepl.com adresini kullanır.
/// Önceki altyazılar "context" olarak gönderilir (DeepL bunu ücretlendirmez) → daha tutarlı, bağlama uygun çeviri.
/// </summary>
public sealed class DeepLTranslator : ITranslator, IBatchTranslator
{
    internal static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly string _key;
    private readonly Uri _base;

    public bool IsFree { get; }
    public string Id => "deepl";
    public string Name => IsFree ? "DeepL Free" : "DeepL Pro";

    public DeepLTranslator(string key)
    {
        _key = key.Trim();
        IsFree = _key.EndsWith(":fx", StringComparison.OrdinalIgnoreCase);
        _base = new Uri(IsFree ? "https://api-free.deepl.com/" : "https://api.deepl.com/");
    }

    public static string Mask(string key)
    {
        key = key.Trim();
        return key.Length <= 6 ? "****" : "…" + key[^6..];
    }

    public async Task<string> TranslateAsync(string text, string? context, CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["text"] = new[] { text },
            ["source_lang"] = "EN",
            ["target_lang"] = "TR",
            ["preserve_formatting"] = true
        };
        if (!string.IsNullOrWhiteSpace(context)) payload["context"] = context;

        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(_base, "v2/translate"));
        req.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {_key}");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw Error((int)resp.StatusCode, body);

        using var doc = JsonDocument.Parse(body);
        var arr = doc.RootElement.GetProperty("translations");
        if (arr.GetArrayLength() == 0) throw new TranslationException("DeepL boş yanıt döndürdü.", TranslationErrorKind.Transient);
        return arr[0].GetProperty("text").GetString() ?? "";
    }

    /// <summary>En çok 50 metni tek istekte çevirir (DeepL sınırı); sıra korunur.</summary>
    public async Task<string[]> TranslateManyAsync(IReadOnlyList<string> texts, string? context, CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["text"] = texts,
            ["source_lang"] = "EN",
            ["target_lang"] = "TR",
            ["preserve_formatting"] = true
        };
        if (!string.IsNullOrWhiteSpace(context)) payload["context"] = context;
        // Tuş simgeleri <x id="n"/> yer tutucusu olarak gider; DeepL bunları Türkçe cümlede doğru yere koyar
        foreach (var t in texts)
            if (t.Contains("<x ")) { payload["tag_handling"] = "xml"; break; }

        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(_base, "v2/translate"));
        req.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {_key}");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw Error((int)resp.StatusCode, body);

        using var doc = JsonDocument.Parse(body);
        var arr = doc.RootElement.GetProperty("translations");
        if (arr.GetArrayLength() != texts.Count)
            throw new TranslationException("DeepL eksik yanıt döndürdü.", TranslationErrorKind.Transient);
        var result = new string[texts.Count];
        for (int i = 0; i < result.Length; i++) result[i] = arr[i].GetProperty("text").GetString() ?? "";
        return result;
    }

    /// <summary>Bu ayki kullanım (karakter) ve sınır.</summary>
    public async Task<(long Used, long Limit)> GetUsageAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(_base, "v2/usage"));
        req.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {_key}");
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw Error((int)resp.StatusCode, body);
        using var doc = JsonDocument.Parse(body);
        long used = doc.RootElement.TryGetProperty("character_count", out var c) ? c.GetInt64() : 0;
        long limit = doc.RootElement.TryGetProperty("character_limit", out var l) ? l.GetInt64() : 0;
        return (used, limit);
    }

    private static TranslationException Error(int code, string body) => code switch
    {
        401 or 403 => new("DeepL API anahtarı geçersiz (403). 'Çeviri Ayarları'ndan kontrol edin.", TranslationErrorKind.Auth, code),
        456 => new("DeepL aylık ücretsiz kotası doldu (456).", TranslationErrorKind.Quota, code),
        429 => new("DeepL'e çok sık istek gönderildi (429).", TranslationErrorKind.Transient, code),
        >= 500 => new($"DeepL sunucusunda geçici hata ({code}).", TranslationErrorKind.Transient, code),
        _ => new($"DeepL hatası ({code}): {Trim(body)}", TranslationErrorKind.Other, code)
    };

    internal static string Trim(string s) => s.Length > 200 ? s[..200] + "…" : s;
}
