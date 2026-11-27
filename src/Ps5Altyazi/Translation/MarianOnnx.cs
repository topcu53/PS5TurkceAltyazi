using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Ps5Altyazi.Translation;

/// <summary>
/// İnternetsiz, ekran kartı gerektirmeyen İngilizce→Türkçe çeviri: Helsinki-NLP "opus-mt-tc-big-en-tr" (Marian)
/// modelinin ONNX (int8) sürümü, ONNX Runtime ile işlemcide çalışır. Kodlayıcı bir kez, kod çözücü (geçmiş
/// önbellekli, "merged") her yeni kelime parçası için çalıştırılır; açgözlü (greedy) arama.
/// Bu dosya Windows'a bağlı değildir (CI'da Linux'ta gerçek modelle test edilir: tests/OfflineTest).
/// </summary>
public sealed class MarianOnnx : IDisposable
{
    public const string EncoderFile = "encoder_model_quantized.onnx";
    public const string DecoderFile = "decoder_model_merged_quantized.onnx";
    public static readonly string[] Files =
        { "config.json", "generation_config.json", "tokenizer.json", "vocab.json", "onnx/" + EncoderFile, "onnx/" + DecoderFile };

    private readonly InferenceSession _enc, _dec;
    private readonly Dictionary<string, int> _vocab;
    private readonly string[] _idToPiece;
    private readonly Dictionary<string, double> _scores = new(StringComparer.Ordinal);
    private readonly int _maxPieceLen;
    private readonly int _eos, _pad, _unk, _start, _layers, _heads, _headDim;
    private readonly HashSet<int> _special = new();
    private readonly List<string> _pastNames;
    private readonly object _lock = new();

    public MarianOnnx(string dir, int threads = 0)
    {
        using (var cfg = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "config.json"))))
        {
            var r = cfg.RootElement;
            _eos = r.GetProperty("eos_token_id").GetInt32();
            _pad = r.GetProperty("pad_token_id").GetInt32();
            _start = r.TryGetProperty("decoder_start_token_id", out var ds) ? ds.GetInt32() : _pad;
            _layers = r.TryGetProperty("decoder_layers", out var dl) ? dl.GetInt32() : 6;
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

        var so = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = threads > 0 ? threads : Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
            InterOpNumThreads = 1,
        };
        _enc = new InferenceSession(Path.Combine(dir, "onnx", EncoderFile), so);
        _dec = new InferenceSession(Path.Combine(dir, "onnx", DecoderFile), so);
        _pastNames = _dec.InputMetadata.Keys.Where(k => k.StartsWith("past_key_values.", StringComparison.Ordinal)).ToList();
    }

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
        lock (_lock) return TranslateCore(text, maxNewTokens);
    }

    private string TranslateCore(string text, int maxNewTokens)
    {
        var src = Encode(text);
        if (src.Count > 256) src = src.Take(255).Append(_eos).ToList();
        int n = src.Count;
        var ids = new DenseTensor<long>(src.Select(x => (long)x).ToArray(), new[] { 1, n });
        var mask = new DenseTensor<long>(Enumerable.Repeat(1L, n).ToArray(), new[] { 1, n });

        DenseTensor<float> encOut;
        using (var r = _enc.Run(new[] { NamedOnnxValue.CreateFromTensor("input_ids", ids), NamedOnnxValue.CreateFromTensor("attention_mask", mask) }))
        {
            var t = r.First().AsTensor<float>();
            encOut = new DenseTensor<float>(t.ToArray(), t.Dimensions.ToArray());
        }

        int limit = maxNewTokens > 0 ? maxNewTokens : Math.Min(256, n * 2 + 16);
        var past = new Dictionary<string, DenseTensor<float>>();
        var empty = new DenseTensor<float>(Array.Empty<float>(), new[] { 1, _heads, 0, _headDim });
        foreach (var name in _pastNames) past[name] = empty;

        var output = new List<int>();
        int last = _start;
        for (int step = 0; step < limit; step++)
        {
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", mask),
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(new long[] { last }, new[] { 1, 1 })),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encOut),
                NamedOnnxValue.CreateFromTensor("use_cache_branch", new DenseTensor<bool>(new[] { step > 0 }, new[] { 1 })),
            };
            foreach (var name in _pastNames) inputs.Add(NamedOnnxValue.CreateFromTensor(name, past[name]));

            using var res = _dec.Run(inputs);
            int next = -1;
            foreach (var o in res)
            {
                if (o.Name == "logits")
                {
                    var lg = o.AsTensor<float>();
                    int vocab = lg.Dimensions[2], s = lg.Dimensions[1];
                    float bestV = float.NegativeInfinity;
                    var arr = lg as DenseTensor<float>;
                    var span = arr != null ? arr.Buffer.Span : lg.ToArray().AsSpan();
                    int off = (s - 1) * vocab;
                    for (int v = 0; v < vocab; v++)
                    {
                        if (v == _pad) continue; // Marian: <pad> asla üretilmez (bad_words_ids)
                        float x = span[off + v];
                        if (x > bestV) { bestV = x; next = v; }
                    }
                }
                else if (o.Name.StartsWith("present.", StringComparison.Ordinal))
                {
                    string pastName = "past_key_values." + o.Name["present.".Length..];
                    if (!past.ContainsKey(pastName)) continue;
                    // Kodlayıcı (çapraz dikkat) önbelleği ilk adımda hesaplanır, sonra hep aynı kalır
                    if (step > 0 && pastName.Contains(".encoder.", StringComparison.Ordinal)) continue;
                    var t = o.AsTensor<float>();
                    past[pastName] = new DenseTensor<float>(t.ToArray(), t.Dimensions.ToArray());
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

    public void Dispose()
    {
        _enc.Dispose();
        _dec.Dispose();
    }
}
