using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ps5Altyazi.Translation;

/// <summary>
/// MyMemory (translated.net) – anahtar gerektirmeyen ücretsiz son yedek.
/// Günlük sınır: e-postasız ~5.000 karakter, e-posta ("de" parametresi) verilirse ~50.000 karakter.
/// Kalitesi DeepL'den düşüktür; yalnızca diğerleri kullanılamadığında devreye girer.
/// </summary>
public sealed class MyMemoryTranslator : ITranslator
{
    private readonly string? _email;

    public string Id => "mymemory";
    public string Name => "MyMemory";

    public MyMemoryTranslator(string? email) =>
        _email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    public async Task<string> TranslateAsync(string text, string? context, CancellationToken ct)
    {
        string url = "https://api.mymemory.translated.net/get?langpair=en%7Ctr&q=" + Uri.EscapeDataString(text);
        if (_email != null) url += "&de=" + Uri.EscapeDataString(_email);

        using var resp = await DeepLTranslator.Http.GetAsync(url, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        int code = (int)resp.StatusCode;

        if (code == 429)
            throw new TranslationException("MyMemory günlük ücretsiz sınırı doldu (429).", TranslationErrorKind.Quota, code);
        if (code >= 500)
            throw new TranslationException($"MyMemory sunucusunda geçici hata ({code}).", TranslationErrorKind.Transient, code);
        if (!resp.IsSuccessStatusCode)
            throw new TranslationException($"MyMemory hatası ({code}): {DeepLTranslator.Trim(body)}", TranslationErrorKind.Other, code);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        bool quotaFinished = root.TryGetProperty("quotaFinished", out var q) && q.ValueKind == JsonValueKind.True;
        int status = root.TryGetProperty("responseStatus", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 200;
        string translated = root.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "";

        if (quotaFinished || status == 429 || translated.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
            throw new TranslationException("MyMemory günlük ücretsiz sınırı doldu.", TranslationErrorKind.Quota, status);
        if (status != 200)
            throw new TranslationException($"MyMemory çeviri yapamadı ({status}).", TranslationErrorKind.Transient, status);

        return WebUtility.HtmlDecode(translated);
    }
}
