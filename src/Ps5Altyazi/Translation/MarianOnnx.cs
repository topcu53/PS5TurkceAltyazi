using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Ps5Altyazi.Translation;

/// <summary>Çevirinin çalışacağı donanım.</summary>
public enum MarianDevice
{
    /// <summary>İşlemci (Intel / AMD): int8 model, her bilgisayarda çalışır.</summary>
    Cpu,
    /// <summary>Ekran kartı (DirectML: NVIDIA, AMD, Intel – DirectX 12): fp16 model.</summary>
    Gpu,
}

/// <summary>
/// İnternetsiz İngilizce→Türkçe çeviri: Helsinki-NLP "opus-mt-tc-big-en-tr" (Marian) modelinin ONNX sürümü.
/// <list type="bullet">
/// <item>CPU profili: int8 model (encoder ~136 MB + merged decoder ~396 MB), ONNX Runtime CPU.</item>
/// <item>GPU profili: fp16 model (encoder ~270 MB + decoder ~321 MB + with-past decoder ~296 MB), DirectML ile
/// her DirectX 12 ekran kartında (NVIDIA / AMD / Intel). Ağırlıklar ve KV önbelleği ekran kartında tutulur;
/// her adımda yalnızca son kelime parçasının olasılıkları ve küçük önbellek tensörleri taşınır.</item>
/// </list>
/// Kodlayıcı bir kez, kod çözücü her yeni parça için çalışır (açgözlü arama, geçmiş önbellekli).
/// Bu dosya Windows'a bağlı değildir (CI'da gerçek modelle test edilir: tests/OfflineTest).
/// </summary>
public sealed class MarianOnnx : IDisposable
{
    // CPU profili
    public const string EncoderFile = "encoder_model_quantized.onnx";
    public const string DecoderFile = "decoder_model_merged_quantized.onnx";
    // GPU profili
    public const string GpuEncoderFile = "encoder_model_fp16.onnx";
    public const string GpuDecoderFile = "decoder_model_fp16.onnx";
    public const string GpuDecoderPastFile = "decoder_with_past_model_fp16.onnx";

    public static readonly string[] CommonFiles = { "config.json", "generation_config.json", "tokenizer.json", "vocab.json" };
    public static readonly string[] CpuFiles = { "onnx/" + EncoderFile, "onnx/" + DecoderFile };
    public static readonly string[] GpuFiles = { "onnx/" + GpuEncoderFile, "onnx/" + GpuDecoderFile, "onnx/" + GpuDecoderPastFile };
    /// <summary>CPU profili için gereken dosyalar (eski sürümlerle uyum).</summary>
    public static string[] Files => CommonFiles.Concat(CpuFiles).ToArray();

    public static string[] FilesFor(MarianDevice d) => CommonFiles.Concat(d == MarianDevice.Gpu ? GpuFiles : CpuFiles).ToArray();

    public MarianDevice Device { get; }
    public string DeviceName { get; }
    public int Threads { get; }
    public TimeSpan LoadTime { get; }

    private readonly InferenceSession _enc, _decFirst;
    private readonly InferenceSession? _decPast; // GPU: ayrı "with past" kod çözücü; CPU: merged (null)
    private readonly bool _fp16;
    private readonly Dictionary<string, int> _vocab;
    private readonly string[] _idToPiece;
    private readonly Dictionary<string, double> _scores = new(StringComparer.Ordinal);
    private readonly int _maxPieceLen;
    private readonly int _eos, _pad, _unk, _start, _heads, _headDim;
    private readonly HashSet<int> _special = new();
    private readonly object _lock = new();

    /// <param name="dir">Model klasörü.</param>
    /// <param name="device">Cpu ya da Gpu (DirectML).</param>
    /// <param name="gpuIndex">DirectML aygıt sırası (DXGI ekran kartı sırası).</param>
    /// <param name="threads">CPU iş parçacığı; 0 = otomatik (fiziksel çekirdek sayısı kadar, en çok 8).</param>
    public MarianOnnx(string dir, MarianDevice device = MarianDevice.Cpu, int gpuIndex = 0, int threads = 0, int physicalCores = 0)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Device = device;
        using (var cfg = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "config.json"))))
        {
            var r = cfg.RootElement;
            _eos = r.GetProperty("eos_token_id").GetInt32();
            _pad = r.GetProperty("pad_token_id").GetInt32();
            _start = r.TryGetProperty("decoder_start_token_id", out var ds) ? ds.GetInt32() : _pad;
            _heads = r.TryGetProperty("decoder_attention_heads", out var dh) ? dh.GetInt32() : 16;
            int dModel = r.TryGetProperty("d_model", out var dm) ? dm.GetInt32() : 1024;
            _headDim = dModel / _heads;
        }

        using (var vj = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "vocab.json"))))
        {
            _vocab = new Dictionary<string, int>(StringComparer.Ordinal);
            int max = 0;
            foreach (var p in vj.RootElement.EnumerateObject())
            {
                int id = p.Value.GetInt32();
                _vocab[p.Name] = id;
                if (id > max) max = id;
            }
            _idToPiece = new string[max + 1];
            foreach (var kv in _vocab) _idToPiece[kv.Value] = kv.Key;
        }
        _unk = _vocab.TryGetValue("<unk>", out var u) ? u : 1;
        _special.Add(_eos); _special.Add(_pad); _special.Add(_unk);

        // Kaynak dil parçalayıcısı (SentencePiece Unigram): parçalar ve olasılık puanları tokenizer.json'dan
        using (var tj = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "tokenizer.json"))))
        {
            var model = tj.RootElement.GetProperty("model");
            foreach (var e in model.GetProperty("vocab").EnumerateArray())
            {
                string piece = e[0].GetString() ?? "";
                double score = e[1].GetDouble();
                if (piece.Length == 0 || piece.StartsWith('<') && piece.EndsWith('>')) continue;
                _scores[piece] = score;
                if (piece.Length > _maxPieceLen) _maxPieceLen = piece.Length;
            }
        }

        int cores = physicalCores > 0 ? physicalCores : Math.Max(1, Environment.ProcessorCount / 2);
        Threads = threads > 0 ? Math.Clamp(threads, 1, Environment.ProcessorCount) : Math.Clamp(cores, 1, 8);
        string onnx = Path.Combine(dir, "onnx");
        if (device == MarianDevice.Gpu)
        {
            SessionOptions Gpu()
            {
                var o = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    // DirectML gereği: bellek deseni kapalı, sıralı yürütme
                    EnableMemoryPattern = false,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    IntraOpNumThreads = Math.Min(Threads, 4),
                };
                o.AppendExecutionProvider_DML(gpuIndex);
                return o;
            }
            _enc = new InferenceSession(Path.Combine(onnx, GpuEncoderFile), Gpu());
            _decFirst = new InferenceSession(Path.Combine(onnx, GpuDecoderFile), Gpu());
            _decPast = new InferenceSession(Path.Combine(onnx, GpuDecoderPastFile), Gpu());
            DeviceName = $"Ekran kartı #{gpuIndex} (DirectML, FP16)";
        }
        else
        {
            var so = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Threads,
                InterOpNumThreads = 1,
            };
            _enc = new InferenceSession(Path.Combine(onnx, EncoderFile), so);
            _decFirst = new InferenceSession(Path.Combine(onnx, DecoderFile), so);
            DeviceName = $"İşlemci ({Threads} iş parçacığı, INT8)";
        }
        _fp16 = _enc.OutputMetadata.Values.First().ElementDataType == TensorElementType.Float16;
        LoadTime = sw.Elapsed;
    }

    /// <summary>Gereken dosyalar klasörde var mı?</summary>
    public static bool HasFiles(string dir, MarianDevice d) =>
        FilesFor(d).All(f => File.Exists(Path.Combine(dir, f.Replace('/', Path.DirectorySeparatorChar))));

    // ───────── Parçalayıcı ─────────

    /// <summary>SentencePiece Unigram: metni en olası parça dizisine böler (Viterbi).</summary>
    public List<int> Encode(string text)
    {
        text = text.Normalize(NormalizationForm.FormKC);
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var ids = new List<int>();
        foreach (var word in text.Split(' '))
        {
            string w = "▁" + word;
            int n = w.Length;
            var best = new double[n + 1];
            var back = new int[n + 1];
            for (int i = 1; i <= n; i++) { best[i] = double.NegativeInfinity; back[i] = -1; }
            for (int i = 0; i < n; i++)
            {
                if (double.IsNegativeInfinity(best[i])) continue;
                int maxLen = Math.Min(_maxPieceLen, n - i);
                bool any = false;
                for (int len = 1; len <= maxLen; len++)
                {
                    if (!_scores.TryGetValue(w.Substring(i, len), out double sc)) continue;
                    any = true;
                    if (best[i] + sc > best[i + len]) { best[i + len] = best[i] + sc; back[i + len] = i; }
                }
                if (!any && best[i] - 20 > best[i + 1]) { best[i + 1] = best[i] - 20; back[i + 1] = i; } // bilinmeyen harf
            }
            var pieces = new List<string>();
            for (int e = n; e > 0; e = back[e]) pieces.Add(w.Substring(back[e], e - back[e]));
            pieces.Reverse();
            foreach (var p in pieces) ids.Add(_vocab.TryGetValue(p, out int id) ? id : _unk);
        }
        ids.Add(_eos);
        return ids;
    }

    public string Decode(IEnumerable<int> ids)
    {
        var sb = new StringBuilder();
        foreach (int id in ids)
        {
            if (_special.Contains(id) || id < 0 || id >= _idToPiece.Length) continue;
            sb.Append(_idToPiece[id]);
        }
        return sb.ToString().Replace('▁', ' ').Trim();
    }

    // ───────── Çeviri ─────────

    public string Translate(string text, int maxNewTokens = 0)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        lock (_lock) return _fp16 ? TranslateCore<Float16>(text, maxNewTokens) : TranslateCore<float>(text, maxNewTokens);
    }

    private static DenseTensor<T> Copy<T>(Tensor<T> t) => new(t.ToArray(), t.Dimensions.ToArray());

    private string TranslateCore<T>(string text, int maxNewTokens) where T : unmanaged
    {
        var src = Encode(text);
        if (src.Count > 256) src = src.Take(255).Append(_eos).ToList();
        int n = src.Count;
        var ids = new DenseTensor<long>(src.Select(x => (long)x).ToArray(), new[] { 1, n });
        var mask = new DenseTensor<long>(Enumerable.Repeat(1L, n).ToArray(), new[] { 1, n });

        DenseTensor<T> encOut;
        using (var r = _enc.Run(new[] { NamedOnnxValue.CreateFromTensor("input_ids", ids), NamedOnnxValue.CreateFromTensor("attention_mask", mask) }))
            encOut = Copy(r.First().AsTensor<T>());

        int limit = maxNewTokens > 0 ? maxNewTokens : Math.Min(256, n * 2 + 16);
        var past = new Dictionary<string, DenseTensor<T>>(StringComparer.Ordinal);
        var empty = new DenseTensor<T>(Array.Empty<T>(), new[] { 1, _heads, 0, _headDim });

        var output = new List<int>();
        int last = _start;
        for (int step = 0; step < limit; step++)
        {
            // CPU: tek "merged" kod çözücü (use_cache_branch ile). GPU: ilk adım ayrı, sonrası "with past" kod çözücü.
            var sess = step > 0 && _decPast != null ? _decPast : _decFirst;
            var meta = sess.InputMetadata;
            var inputs = new List<NamedOnnxValue>();
            if (meta.ContainsKey("encoder_attention_mask")) inputs.Add(NamedOnnxValue.CreateFromTensor("encoder_attention_mask", mask));
            inputs.Add(NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(new long[] { last }, new[] { 1, 1 })));
            if (meta.ContainsKey("encoder_hidden_states")) inputs.Add(NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encOut));
            if (meta.ContainsKey("use_cache_branch"))
                inputs.Add(NamedOnnxValue.CreateFromTensor("use_cache_branch", new DenseTensor<bool>(new[] { step > 0 }, new[] { 1 })));
            foreach (var name in meta.Keys)
                if (name.StartsWith("past_key_values.", StringComparison.Ordinal))
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, past.TryGetValue(name, out var pv) ? pv : empty));

            using var res = sess.Run(inputs);
            int next = -1;
            foreach (var o in res)
            {
                if (o.Name == "logits") next = ArgMax(o);
                else if (o.Name.StartsWith("present.", StringComparison.Ordinal))
                {
                    string pastName = "past_key_values." + o.Name["present.".Length..];
                    // Kodlayıcı (çapraz dikkat) önbelleği ilk adımda hesaplanır, sonra hep aynı kalır
                    if (step > 0 && pastName.Contains(".encoder.", StringComparison.Ordinal)) continue;
                    past[pastName] = Copy(o.AsTensor<T>());
                }
            }
            if (next < 0 || next == _eos) break;
            output.Add(next);
            last = next;
            // Kısır döngü koruması: aynı 3 parça art arda 3 kez
            if (output.Count >= 9 && output.Skip(output.Count - 9).Take(3).SequenceEqual(output.Skip(output.Count - 6).Take(3))
                                  && output.Skip(output.Count - 6).Take(3).SequenceEqual(output.Skip(output.Count - 3)))
            {
                output.RemoveRange(output.Count - 6, 6);
                break;
            }
        }
        return Decode(output);
    }

    /// <summary>Son konumdaki en olası parça (&lt;pad&gt; hariç: Marian onu asla üretmez).</summary>
    private int ArgMax(DisposableNamedOnnxValue o)
    {
        int best = -1;
        float bestV = float.NegativeInfinity;
        if (o.Value is Tensor<Float16> h)
        {
            int vocab = h.Dimensions[2], s = h.Dimensions[1];
            var span = h is DenseTensor<Float16> d ? d.Buffer.Span : h.ToArray().AsSpan();
            int off = (s - 1) * vocab;
            for (int v = 0; v < vocab; v++)
            {
                if (v == _pad) continue;
                float x = (float)span[off + v];
                if (x > bestV) { bestV = x; best = v; }
            }
        }
        else
        {
            var lg = o.AsTensor<float>();
            int vocab = lg.Dimensions[2], s = lg.Dimensions[1];
            var span = lg is DenseTensor<float> d ? d.Buffer.Span : lg.ToArray().AsSpan();
            int off = (s - 1) * vocab;
            for (int v = 0; v < vocab; v++)
            {
                if (v == _pad) continue;
                float x = span[off + v];
                if (x > bestV) { bestV = x; best = v; }
            }
        }
        return best;
    }

    public void Dispose()
    {
        _enc.Dispose();
        _decFirst.Dispose();
        _decPast?.Dispose();
    }
}
