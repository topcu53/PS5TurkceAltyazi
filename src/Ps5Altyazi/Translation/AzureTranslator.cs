using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ps5Altyazi.Translation;

/// <summary>
/// Microsoft Azure Translator (F0 ücretsiz katman: ayda 2 milyon karakter).
/// Anahtar + bölge (ör. "westeurope"; "global" kaynakta boş bırakılabilir).
/// </summary>
public sealed class AzureTranslator : ITranslator
{
    private const string Endpoint = "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&from=en&to=tr";
    private readonly string _key;
    private readonly string? _region;

    public string Id => "azure";
    public string Name => "Azure Translator";

    public AzureTranslator(string key, string? region)
    {
        _key = key.Trim();
        _region = string.IsNullOrWhiteSpace(region) || region.Trim().Equals("global", StringComparison.OrdinalIgnoreCase)
            ? null
            : region.Trim();
    }

    public async Task<string> TranslateAsync(string text, string? context, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _key);
        if (_region != null) req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", _region);
        req.Content = new StringContent(JsonSerializer.Serialize(new[] { new { Text = text } }), Encoding.UTF8, "application/json");

        using var resp = await DeepLTranslator.Http.SendAsync(req, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        int code = (int)resp.StatusCode;
        if (!resp.IsSuccessStatusCode)
        {
            // Azure hata kodları: 401000 anahtar, 403001 ücretsiz kota doldu, 429xxx hız sınırı
            int detail = 0;
            try
            {
                using var e = JsonDocument.Parse(body);
                detail = e.RootElement.GetProperty("error").GetProperty("code").GetInt32();
            }
            catch { }

            throw code switch
            {
                401 => new TranslationException("Azure anahtarı veya bölgesi geçersiz (401).", TranslationErrorKind.Auth, code),
                403 when detail == 403001 => new TranslationException("Azure ücretsiz aylık kotası doldu (403001).", TranslationErrorKind.Quota, code),
                403 => new TranslationException($"Azure isteği reddetti (403 / {detail}).", TranslationErrorKind.Auth, code),
                429 => new TranslationException("Azure'a çok sık istek gönderildi (429).", TranslationErrorKind.Transient, code),
                >= 500 => new TranslationException($"Azure sunucusunda geçici hata ({code}).", TranslationErrorKind.Transient, code),
                _ => new TranslationException($"Azure hatası ({code}): {DeepLTranslator.Trim(body)}", TranslationErrorKind.Other, code)
            };
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement[0].GetProperty("translations")[0].GetProperty("text").GetString() ?? "";
    }
}
