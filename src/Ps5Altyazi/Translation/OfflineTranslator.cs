using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Ps5Altyazi.Translation;

/// <summary>
/// Bilgisayarda (internetsiz) çeviri: kota yok, hız sınırı yok → çeviri hiç durmaz. İnternet servisleri (DeepL, Gemini,
/// MyMemory) kota / hız sınırına takıldığında ya da geç kaldığında devreye girer. Ekran kartı gerekmez (işlemci).
/// Model (~530 MB, bir kez) Hugging Face'ten indirilir: %LOCALAPPDATA%\PS5TurkceAltyazi\models\opus-mt-tc-big-en-tr.
/// </summary>
public sealed class OfflineTranslator : ITranslator, IDisposable
{
    public const string ModelName = "opus-mt-tc-big-en-tr";
    private const string BaseUrl = "https://huggingface.co/onnx-community/opus-mt-tc-big-en-tr/resolve/main/";

    public static string ModelDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS5TurkceAltyazi", "models", ModelName);

    public static bool IsInstalled =>
        MarianOnnx.Files.All(f => File.Exists(Path.Combine(ModelDir, f.Replace('/', Path.DirectorySeparatorChar))));

    private static MarianOnnx? _shared;
    private static Task<MarianOnnx>? _loading;
    private static readonly object Gate = new();
    private static readonly SemaphoreSlim Busy = new(1, 1);

    public string Id => "offline";
    public string Name => "Bilgisayar (çevrimdışı)";

    /// <summary>Modeli arka planda belleğe yükler (ilk çeviri beklemesin). Model yoksa bir şey yapmaz.</summary>
    public static Task<MarianOnnx>? Preload()
    {
        if (!IsInstalled) return null;
        lock (Gate)
        {
            if (_shared != null) return Task.FromResult(_shared);
            return _loading ??= Task.Run(() =>
            {
                var m = new MarianOnnx(ModelDir);
                m.Translate("Hello."); // ilk çalıştırma ısınması
                lock (Gate) _shared = m;
                return m;
            });
        }
    }

    public async Task<string> TranslateAsync(string text, string? context, CancellationToken ct)
    {
        var load = Preload() ?? throw new TranslationException("Çevrimdışı çeviri modeli indirilmemiş.", TranslationErrorKind.Auth);
        MarianOnnx m;
        try { m = await load.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            lock (Gate) _loading = null;
            throw new TranslationException("Çevrimdışı model açılamadı: " + ex.Message, TranslationErrorKind.Transient);
        }
        await Busy.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => m.Translate(text), ct).ConfigureAwait(false);
        }
        finally { Busy.Release(); }
    }

    /// <summary>Modeli indirir. progress: (indirilen bayt, toplam bayt).</summary>
    public static async Task DownloadAsync(IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.Combine(ModelDir, "onnx"));
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AltyaziPlus");

        // Toplam boyut (ilerleme çubuğu için)
        long total = 0;
        var sizes = new long[MarianOnnx.Files.Length];
        for (int i = 0; i < MarianOnnx.Files.Length; i++)
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, BaseUrl + MarianOnnx.Files[i]);
            using var r = await http.SendAsync(head, ct).ConfigureAwait(false);
            sizes[i] = r.Content.Headers.ContentLength ?? 0;
            total += sizes[i];
        }

        long done = 0;
        var buf = new byte[1 << 16];
        for (int i = 0; i < MarianOnnx.Files.Length; i++)
        {
            string rel = MarianOnnx.Files[i];
            string dest = Path.Combine(ModelDir, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(dest) && (sizes[i] == 0 || new FileInfo(dest).Length == sizes[i]))
            {
                done += sizes[i];
                progress?.Report((done, total));
                continue;
            }
            string part = dest + ".part";
            using (var resp = await http.GetAsync(BaseUrl + rel, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = File.Create(part);
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    progress?.Report((done, total));
                }
            }
            File.Move(part, dest, overwrite: true);
        }
    }

    public void Dispose() { }
}
