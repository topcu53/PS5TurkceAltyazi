using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ps5Altyazi.Translation;

/// <summary>
/// Google Gemini API (Google AI Studio anahtarı) ile İngilizce → Türkçe oyun altyazısı çevirisi.
/// Hız için en hafif "Flash-Lite" modeli ve en az düşünme kullanılır. Önceki altyazılar bağlam olarak verilir
/// (karakter adları, hitap ve konu tutarlı kalsın). Ücretsiz katmanda dakikalık / günlük sınır vardır;
/// sınır dolunca (429) zincirdeki sonraki servise geçilir.
/// </summary>
public sealed class GeminiTranslator : ITranslator, IWarmable
{
    public const string DefaultModel = "gemini-3.5-flash-lite";
    // Model adı Google tarafında değişirse sırayla denenecek yedekler
    private static readonly string[] FallbackModels = { "gemini-3.1-flash-lite", "gemini-flash-lite-latest", "gemini-2.5-flash-lite" };

    private const string Base = "https://generativelanguage.googleapis.com/v1beta/models/";
    private const string Instruction =
        "You are a professional Turkish game localizer. Translate the given English subtitle line into natural, fluent, " +
        "spoken Turkish, exactly as a Turkish dub/subtitle would say it — never word-for-word. " +
        "Rules: keep it about as short as the original so it can be read quickly; keep character names, place names and " +
        "brand names unchanged; use 'sen' between friends/family and 'siz' only for clearly formal speech; match the tone " +
        "(slang, jokes, swearing, anger); translate idioms by meaning; use correct Turkish spelling and punctuation; " +
        "use the previous lines only to understand who is speaking and what 'it/he/she/that' refers to. " +
        "Output ONLY the Turkish translation of the given line, without quotes or explanations.";

    private readonly string _key;
    private string _model;
    private bool _thinkingParamOk = true;

    public string Id => "gemini";
    public string Name => "Google Gemini";
    public string Model => _model;

    public GeminiTranslator(string key, string? model)
    {
        _key = key.Trim();
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
    }

    public async Task<string> TranslateAsync(string text, string? context, CancellationToken ct)
    {
        var tried = new HashSet<string>();
        while (true)
        {
            tried.Add(_model);
            try
            {
                return await CallAsync(text, context, ct).ConfigureAwait(false);
            }
            catch (TranslationException ex) when (ex.StatusCode == 404)
            {
                // Model bulunamadı (adı değişmiş olabilir) → sıradaki yedek model
                string? next = FallbackModels.FirstOrDefault(m => !tried.Contains(m));
                if (next == null) throw;
                Logging.Log.Warn($"Gemini modeli \"{_model}\" bulunamadı, \"{next}\" deneniyor.");
                _model = next;
            }
        }
    }

    private async Task<string> CallAsync(string text, string? context, CancellationToken ct)
    {
        string prompt = string.IsNullOrWhiteSpace(context)
            ? $"Line: {text}"
            : $"Previous lines (for context only, do not translate): {context}\nLine: {text}";

        var gen = new Dictionary<string, object> { ["temperature"] = 0.2, ["maxOutputTokens"] = 300 };
        if (_thinkingParamOk) gen["thinkingConfig"] = new Dictionary<string, object> { ["thinkingLevel"] = "minimal" };

        var payload = new Dictionary<string, object>
        {
            ["systemInstruction"] = new { parts = new[] { new { text = Instruction } } },
            ["contents"] = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
            ["generationConfig"] = gen
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}{Uri.EscapeDataString(_model)}:generateContent");
        req.Headers.TryAddWithoutValidation("x-goog-api-key", _key);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await DeepLTranslator.Http.SendAsync(req, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        int code = (int)resp.StatusCode;

        if (code == 400 && _thinkingParamOk && body.Contains("thinking", StringComparison.OrdinalIgnoreCase))
        {
            // Bu model düşünme ayarını desteklemiyor → ayarsız tekrar dene
            _thinkingParamOk = false;
            return await CallAsync(text, context, ct).ConfigureAwait(false);
        }
        if (!resp.IsSuccessStatusCode) throw Error(code, body);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0)
        {
            string reason = root.TryGetProperty("promptFeedback", out var pf) && pf.TryGetProperty("blockReason", out var br)
                ? br.GetString() ?? "" : "boş yanıt";
            throw new TranslationException($"Gemini yanıt vermedi ({reason}).", TranslationErrorKind.Other, code);
        }
        var sb = new StringBuilder();
        if (cands[0].TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
            foreach (var p in parts.EnumerateArray())
            {
                if (p.TryGetProperty("thought", out var th) && th.ValueKind == JsonValueKind.True) continue;
                if (p.TryGetProperty("text", out var t)) sb.Append(t.GetString());
            }
        string result = sb.ToString().Trim().Trim('"', '“', '”');
        if (result.Length == 0) throw new TranslationException("Gemini boş çeviri döndürdü.", TranslationErrorKind.Other, code);
        return result;
    }

    /// <summary>Bağlantıyı açık tutar: model bilgisini okur (çeviri kotasından düşmez).</summary>
    public async Task WarmUpAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Base}{Uri.EscapeDataString(_model)}");
        req.Headers.TryAddWithoutValidation("x-goog-api-key", _key);
        using var resp = await DeepLTranslator.Http.SendAsync(req, ct).ConfigureAwait(false);
    }

    private static TranslationException Error(int code, string body)
    {
        string status = "", message = "";
        try
        {
            using var e = JsonDocument.Parse(body);
            var err = e.RootElement.GetProperty("error");
            if (err.TryGetProperty("status", out var s)) status = s.GetString() ?? "";
            if (err.TryGetProperty("message", out var m)) message = m.GetString() ?? "";
        }
        catch { }

        return code switch
        {
            400 when status == "INVALID_ARGUMENT" && message.Contains("API key", StringComparison.OrdinalIgnoreCase)
                => new TranslationException("Gemini API anahtarı geçersiz.", TranslationErrorKind.Auth, code),
            401 or 403 => new TranslationException($"Gemini anahtarı reddedildi ({code}): {DeepLTranslator.Trim(message)}", TranslationErrorKind.Auth, code),
            404 => new TranslationException($"Gemini modeli bulunamadı (404).", TranslationErrorKind.Other, code),
            429 => new TranslationException("Gemini ücretsiz kullanım sınırı doldu (429).", TranslationErrorKind.Quota, code),
            >= 500 => new TranslationException($"Gemini sunucusunda geçici hata ({code}).", TranslationErrorKind.Transient, code),
            _ => new TranslationException($"Gemini hatası ({code}): {DeepLTranslator.Trim(message.Length > 0 ? message : body)}", TranslationErrorKind.Other, code)
        };
    }
}
