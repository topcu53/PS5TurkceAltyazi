using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ps5Altyazi.Translation;

public sealed record TranslationOutcome(string Text, bool FromCache, bool Fuzzy, double Milliseconds, string Engine);

/// <summary>Zincirdeki bir çeviri motorunun durumu.</summary>
public sealed class EngineSlot
{
    public required ITranslator Translator { get; init; }
    public DateTime? CooldownUntil { get; set; }
    public string? DisabledReason { get; set; }
    public int ConsecutiveFailures { get; set; }
    public long Calls { get; set; }
    public long Chars { get; set; }
    public double MsSum { get; set; }

    public bool IsUsable(DateTime now) => DisabledReason == null && (CooldownUntil == null || CooldownUntil <= now);
}

/// <summary>
/// Önbellek + sıralı çeviri motorları (DeepL → Azure → MyMemory).
/// Bir motorun kotası dolarsa otomatik olarak sıradakine geçilir ve kota dolan motor
/// bekleme süresi bitince yeniden denenir. Geçici hatada yalnızca o istek için sıradakine geçilir.
/// </summary>
public sealed class TranslationService : IDisposable
{
    /// <summary>Tek bir motor denemesi için süre sınırı (altyazı ekranda kısa kalır).</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(3);
    /// <summary>Birinci servis bu sürede yanıt vermezse yedek servis AYNI ANDA denenir; önce gelen kullanılır.</summary>
    public static readonly TimeSpan HedgeDelay = TimeSpan.FromMilliseconds(700);
    public long HedgeWins { get; private set; }

    /// <summary>Sıradaki çeviri servisinin bağlantısını açık / sıcak tutar (hata olursa sessizce geçer).</summary>
    public async Task WarmUpAsync()
    {
        var slot = _slots.FirstOrDefault(s => s.IsUsable(DateTime.Now));
        if (slot?.Translator is not IWarmable w) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await w.WarmUpAsync(cts.Token);
        }
        catch { /* ısıtma isteğe bağlı */ }
    }

    private readonly TranslationCache _cache;
    private List<EngineSlot> _slots = new();
    private string? _lastActiveId;

    /// <summary>Motor durumu değişince (kota doldu, geçildi, geri dönüldü) Türkçe mesaj.</summary>
    public event Action<string, bool>? EngineEvent; // (mesaj, uyarı mı)

    /// <summary>Kota nedeniyle beklemeye alınan motorlar (kalıcı saklamak için).</summary>
    public event Action<string, DateTime?>? CooldownChanged;

    public IReadOnlyList<EngineSlot> Slots => _slots;
    public int CacheCount => _cache.Count;
    public long CacheHits { get; private set; }
    public long ApiCalls => _slots.Sum(s => s.Calls);
    public long CharsSent => _slots.Sum(s => s.Chars);
    public double ApiMsSum => _slots.Sum(s => s.MsSum);

    public TranslationService(TranslationCache cache) => _cache = cache;

    public void SetEngines(IEnumerable<ITranslator> engines, IReadOnlyDictionary<string, DateTime>? cooldowns)
    {
        var now = DateTime.Now;
        _slots = engines.Select(t =>
        {
            var slot = new EngineSlot { Translator = t };
            if (cooldowns != null && cooldowns.TryGetValue(t.Id, out var until) && until > now) slot.CooldownUntil = until;
            return slot;
        }).ToList();
        _lastActiveId = null;
    }

    public void ResetStats()
    {
        CacheHits = 0;
        foreach (var s in _slots) { s.Calls = 0; s.Chars = 0; s.MsSum = 0; }
    }

    /// <summary>Şu an kullanılacak motor (bilgi amaçlı).</summary>
    public EngineSlot? ActiveSlot => _slots.FirstOrDefault(s => s.IsUsable(DateTime.Now));

    public void MarkQuota(string id, string reason)
    {
        var slot = _slots.FirstOrDefault(s => s.Translator.Id == id);
        if (slot == null) return;
        SetCooldown(slot, QuotaCooldown(id), reason);
    }

    public string DescribeChain()
    {
        var now = DateTime.Now;
        return string.Join(" → ", _slots.Select(s =>
            s.DisabledReason != null ? $"{s.Translator.Name} (kapalı)"
            : s.CooldownUntil > now ? $"{s.Translator.Name} (kota, {s.CooldownUntil:HH:mm}'e kadar)"
            : s.Translator.Name));
    }

    public async Task<TranslationOutcome> TranslateAsync(string text, string? context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (_cache.TryGet(text, out string cached, out bool fuzzy))
        {
            CacheHits++;
            return new TranslationOutcome(cached, true, fuzzy, sw.Elapsed.TotalMilliseconds, "önbellek");
        }

        if (_slots.Count == 0)
            throw new TranslationException("Hiç çeviri servisi ayarlanmadı. 'Çeviri Ayarları…' düğmesini kullanın.", TranslationErrorKind.Auth);

        var errors = new List<string>();
        for (int si = 0; si < _slots.Count; si++)
        {
            var slot = _slots[si];
            var now = DateTime.Now;
            if (!slot.IsUsable(now)) continue;
            ct.ThrowIfCancellationRequested();

            if (slot.CooldownUntil != null)
            {
                // Bekleme süresi bitti → yeniden deneniyor
                slot.CooldownUntil = null;
                CooldownChanged?.Invoke(slot.Translator.Id, null);
            }

            var attemptSw = Stopwatch.StartNew();
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(AttemptTimeout);
            try
            {
                var primary = slot.Translator.TranslateAsync(text, context, attemptCts.Token);

                // Yedekli deneme: birinci servis HedgeDelay içinde yanıt vermezse sıradaki servis de başlatılır
                var backup = _slots.Skip(si + 1).FirstOrDefault(s2 => s2.IsUsable(now));
                if (backup != null)
                {
                    var first = await Task.WhenAny(primary, Task.Delay(HedgeDelay, ct));
                    ct.ThrowIfCancellationRequested();
                    if (first != primary)
                    {
                        using var backupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        backupCts.CancelAfter(AttemptTimeout);
                        var bsw = Stopwatch.StartNew();
                        var second = backup.Translator.TranslateAsync(text, context, backupCts.Token);
                        var done = await Task.WhenAny(primary, second);
                        if (done == second && second.Status == TaskStatus.RanToCompletion && !string.IsNullOrWhiteSpace(second.Result))
                        {
                            attemptCts.Cancel(); // yavaş kalan birinci istek bırakılır
                            Observe(primary);
                            backup.Calls++;
                            backup.Chars += text.Length;
                            backup.MsSum += bsw.Elapsed.TotalMilliseconds;
                            backup.ConsecutiveFailures = 0;
                            HedgeWins++;
                            _cache.Put(text, second.Result, backup.Translator.Name);
                            return new TranslationOutcome(second.Result, false, false, sw.Elapsed.TotalMilliseconds, backup.Translator.Name);
                        }
                        backupCts.Cancel();
                        Observe(second);
                    }
                }

                string result = await primary;
                attemptSw.Stop();
                slot.Calls++;
                slot.Chars += text.Length;
                slot.MsSum += attemptSw.Elapsed.TotalMilliseconds;
                slot.ConsecutiveFailures = 0;
                NoteActive(slot);
                if (!string.IsNullOrWhiteSpace(result)) _cache.Put(text, result, slot.Translator.Name);
                return new TranslationOutcome(result, false, false, sw.Elapsed.TotalMilliseconds, slot.Translator.Name);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // altyazı değişti; çağıran iptal etti
            }
            catch (OperationCanceledException)
            {
                errors.Add($"{slot.Translator.Name}: zaman aşımı");
                Fail(slot, $"{slot.Translator.Name} {AttemptTimeout.TotalSeconds:F0} sn içinde yanıt vermedi");
            }
            catch (TranslationException ex) when (ex.Kind == TranslationErrorKind.Quota)
            {
                errors.Add(ex.Message);
                SetCooldown(slot, QuotaCooldown(slot.Translator.Id), ex.Message);
            }
            catch (TranslationException ex) when (ex.Kind == TranslationErrorKind.Auth)
            {
                errors.Add(ex.Message);
                slot.DisabledReason = ex.Message;
                EngineEvent?.Invoke($"{ex.Message} {slot.Translator.Name} anahtar düzeltilene kadar atlanacak.", true);
            }
            catch (TranslationException ex)
            {
                errors.Add(ex.Message);
                Fail(slot, ex.Message);
            }
            catch (Exception ex) // HttpRequestException (internet yok) vb.
            {
                errors.Add($"{slot.Translator.Name}: {ex.Message}");
                Fail(slot, $"{slot.Translator.Name} bağlantı hatası: {ex.Message}");
            }
        }

        throw new TranslationException(
            "Şu an kullanılabilir çeviri servisi yok. " + (errors.Count > 0 ? string.Join(" | ", errors) : DescribeChain()),
            TranslationErrorKind.Transient);
    }

    /// <summary>
    /// Birden çok metni çevirir (ekran çevirisi). Önce önbellek; kalanlar, sıradaki motor toplu çeviri
    /// destekliyorsa (DeepL) tek istekte, desteklemiyorsa ya da toplu istek başarısız olursa tek tek çevrilir.
    /// Çevrilemeyen öğe null kalır.
    /// </summary>
    public async Task<(string?[] Results, int FromCache, string Engine, double Milliseconds)> TranslateManyAsync(
        IReadOnlyList<string> texts, string? context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var results = new string?[texts.Count];
        var missing = new List<int>();
        int fromCache = 0;
        for (int i = 0; i < texts.Count; i++)
        {
            if (_cache.TryGet(texts[i], out string cached, out _)) { results[i] = cached; fromCache++; CacheHits++; }
            else missing.Add(i);
        }
        if (missing.Count == 0) return (results, fromCache, "önbellek", sw.Elapsed.TotalMilliseconds);

        string engine = "";
        var slot = _slots.FirstOrDefault(s => s.IsUsable(DateTime.Now));
        if (slot?.Translator is IBatchTranslator batch)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(AttemptTimeout + TimeSpan.FromSeconds(2));
                var src = missing.Select(i => texts[i]).ToList();
                var attemptSw = Stopwatch.StartNew();
                var tr = await batch.TranslateManyAsync(src, context, cts.Token);
                slot.Calls++;
                slot.Chars += src.Sum(t => t.Length);
                slot.MsSum += attemptSw.Elapsed.TotalMilliseconds;
                slot.ConsecutiveFailures = 0;
                NoteActive(slot);
                for (int k = 0; k < missing.Count; k++)
                {
                    results[missing[k]] = tr[k];
                    if (!string.IsNullOrWhiteSpace(tr[k])) _cache.Put(src[k], tr[k], slot.Translator.Name);
                }
                return (results, fromCache, slot.Translator.Name, sw.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (TranslationException ex) when (ex.Kind == TranslationErrorKind.Quota)
            {
                SetCooldown(slot, QuotaCooldown(slot.Translator.Id), ex.Message);
            }
            catch (TranslationException ex) when (ex.Kind == TranslationErrorKind.Auth)
            {
                slot.DisabledReason = ex.Message;
                EngineEvent?.Invoke($"{ex.Message} {slot.Translator.Name} anahtar düzeltilene kadar atlanacak.", true);
            }
            catch (Exception) { /* aşağıda tek tek denenir */ }
        }

        // Tek tek (zincirdeki yedek motorlarla)
        foreach (int i in missing)
        {
            try
            {
                // Yedek motorlar yer tutucuyu bilmez: simgesiz düz metin gönder (Türkçe, simge kopyasıyla gösterilir)
                string plain = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(texts[i], "<[^>]+>", " "));
                plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ").Trim();
                var o = await TranslateAsync(plain, context, ct);
                results[i] = o.Text;
                engine = o.Engine;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (TranslationException) { /* bu öğe çevrilemedi */ }
        }
        return (results, fromCache, engine, sw.Elapsed.TotalMilliseconds);
    }

    private static void Observe(Task t) =>
        t.ContinueWith(x => _ = x.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    private void NoteActive(EngineSlot slot)
    {
        if (_lastActiveId == slot.Translator.Id) return;
        bool first = _lastActiveId == null;
        _lastActiveId = slot.Translator.Id;
        bool isPrimary = ReferenceEquals(slot, _slots[0]);
        if (!first || !isPrimary)
            EngineEvent?.Invoke(isPrimary
                ? $"Çeviri yeniden {slot.Translator.Name} ile yapılıyor."
                : $"Çeviri otomatik olarak {slot.Translator.Name} servisine geçti. ({DescribeChain()})", !isPrimary);
    }

    private void Fail(EngineSlot slot, string message)
    {
        slot.ConsecutiveFailures++;
        if (slot.ConsecutiveFailures >= 3)
        {
            slot.ConsecutiveFailures = 0;
            slot.CooldownUntil = DateTime.Now.AddMinutes(1);
            EngineEvent?.Invoke($"{message}. Art arda 3 hata: {slot.Translator.Name} 1 dakika atlanacak.", true);
        }
        else
        {
            EngineEvent?.Invoke($"{message}; bu altyazı için sıradaki servis deneniyor.", true);
        }
    }

    private void SetCooldown(EngineSlot slot, TimeSpan wait, string reason)
    {
        slot.CooldownUntil = DateTime.Now.Add(wait);
        CooldownChanged?.Invoke(slot.Translator.Id, slot.CooldownUntil);
        var next = _slots.FirstOrDefault(s => s.IsUsable(DateTime.Now));
        EngineEvent?.Invoke($"{reason} {slot.Translator.Name} {slot.CooldownUntil:dd.MM HH:mm}'e kadar atlanacak" +
                            (next != null ? $"; çeviri {next.Translator.Name} ile devam ediyor." : "; başka kullanılabilir servis yok!"), true);
    }

    /// <summary>Kota dolunca ne kadar sonra yeniden denensin.</summary>
    private static TimeSpan QuotaCooldown(string id) => id switch
    {
        "deepl" => TimeSpan.FromHours(6),   // aylık kota; 6 saatte bir yoklanır (yeni dönem başladıysa kendiliğinden döner)
        "azure" => TimeSpan.FromHours(6),
        "mymemory" => TimeSpan.FromHours(1), // günlük kota
        "gemini" => TimeSpan.FromMinutes(2), // ücretsiz katman çoğunlukla dakikalık sınır
        _ => TimeSpan.FromHours(1)
    };

    public void Dispose() => _cache.Dispose();
}
