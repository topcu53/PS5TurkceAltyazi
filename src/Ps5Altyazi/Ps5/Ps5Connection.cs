using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ps5Altyazi.Ps5;

public enum Ps5State { Connecting, Waking, Streaming, Retrying }

/// <summary>
/// PS5'e süren bağlantı: PS5'i (gerekirse ağda yeniden) bulur, dinlenmedeyse uyandırır, yardımcı programla görüntü akışını
/// açar, koparsa bekleyip yeniden bağlanır. Ses ve kol girdisi kapalıdır; ekranda pencere açılmaz.
/// </summary>
public sealed class Ps5Connection : IDisposable
{
    private readonly byte[] _account;
    private readonly string? _passcode;
    private readonly int _fps;
    private Ps5Pairing _pairing;
    private CancellationTokenSource? _cts;
    private Process? _proc;
    private readonly object _procLock = new();

    /// <summary>Kare: BGRA pikseller, genişlik, yükseklik (satır = genişlik*4). Arka plan iş parçacığında.</summary>
    public event Action<byte[], int, int>? FrameReceived;
    public event Action<Ps5State, string>? StateChanged;
    /// <summary>PS5 ağda yeni adreste bulundu (kayıt güncellenmeli).</summary>
    public event Action<Ps5Pairing>? Moved;
    /// <summary>Yeniden denemek işe yaramayacak (eşleme geçersiz, giriş şifresi yanlış…): bağlantı durdu.</summary>
    public event Action<string>? GaveUp;

    public string ConsoleName => _pairing.Name;

    public Ps5Connection(Ps5Pairing pairing, byte[] accountId, string? passcode, int fps = 12)
    {
        _pairing = pairing;
        _account = accountId;
        _passcode = string.IsNullOrWhiteSpace(passcode) ? null : passcode.Trim();
        _fps = Math.Clamp(fps, 2, 30);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Task.Run(() => LoopAsync(ct));
    }

    /// <summary>Bloklamaz: yardımcı program arka planda kapatılır.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        Task.Run(StopProcess);
    }

    public void Dispose() => Stop();

    private void Set(Ps5State s, string text)
    {
        try { StateChanged?.Invoke(s, text); } catch { }
    }

    private static bool SameMac(string a, string b)
    {
        static string N(string s) => new string(s.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        return a.Length > 0 && b.Length > 0 && N(a) == N(b);
    }

    private async Task<Ps5Console?> LocateAsync()
    {
        var at = await Ps5Helper.DiscoverAsync(new[] { _pairing.Host }, 1500);
        var c = at.FirstOrDefault();
        if (c != null && (_pairing.Mac.Length == 0 || c.Id.Length == 0 || SameMac(c.Id, _pairing.Mac))) return c;
        // Kayıtlı adreste yok (ya da başka bir cihaz): PS5 ağda MAC adresiyle (yoksa adıyla) aranır.
        var all = await Ps5Helper.DiscoverAsync(null, 2500);
        var m = all.FirstOrDefault(x => SameMac(x.Id, _pairing.Mac))
                ?? (_pairing.Mac.Length == 0 ? all.FirstOrDefault(x => string.Equals(x.Name, _pairing.Name, StringComparison.OrdinalIgnoreCase)) : null);
        if (m != null && m.Host != _pairing.Host)
        {
            _pairing = new Ps5Pairing { Host = m.Host, Name = _pairing.Name, Key = _pairing.Key, Morning = _pairing.Morning, Mac = _pairing.Mac };
            Ps5Helper.WriteLog($"PS5 yeni adreste: {m.Host}");
            try { Moved?.Invoke(_pairing); } catch { }
        }
        return m;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            string problem;
            try
            {
                Set(Ps5State.Connecting, $"PS5 aranıyor – {_pairing.Name}");
                var found = await LocateAsync();
                if (ct.IsCancellationRequested) break;
                if (found == null)
                {
                    problem = $"PS5 ağda bulunamadı ({_pairing.Host}). PS5 açık ya da dinlenmede mi, aynı ağda mı?";
                }
                else
                {
                    if (!found.Awake)
                    {
                        Set(Ps5State.Waking, $"PS5 dinlenmede, uyandırılıyor – {_pairing.Name}");
                        await Ps5Helper.WakeAsync(_pairing);
                        var until = DateTime.Now.AddSeconds(45);
                        while (!ct.IsCancellationRequested && DateTime.Now < until)
                        {
                            await Task.Delay(3000, ct);
                            var again = await Ps5Helper.DiscoverAsync(new[] { _pairing.Host }, 1200);
                            if (again.Any(x => x.Awake)) { found = again.First(x => x.Awake); break; }
                        }
                    }
                    if (ct.IsCancellationRequested) break;
                    if (!found.Awake)
                        problem = "PS5 uyandırılamadı. PS5'te Ayarlar › Sistem › Güç Tasarrufu › Dinlenme Modunda Kullanılabilen Özellikler'de " +
                                  "'İnternete Bağlı Kal' ve 'PS5'in Ağ Üzerinden Açılmasını Etkinleştir' açık olmalı.";
                    else
                    {
                        Set(Ps5State.Connecting, $"PS5'e bağlanılıyor – {_pairing.Name}");
                        var r = await RunStreamAsync(ct);
                        if (ct.IsCancellationRequested) break;
                        if (r.Fatal != null)
                        {
                            Ps5Helper.WriteLog("Bağlantı bırakıldı: " + r.Fatal);
                            try { GaveUp?.Invoke(r.Fatal); } catch { }
                            return;
                        }
                        if (r.Streamed) failures = 0;
                        problem = r.Problem;
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                problem = ex.Message;
                Ps5Helper.WriteLog("Hata: " + ex);
            }

            failures++;
            if (failures >= 10)
            {
                try { GaveUp?.Invoke(problem + " (art arda 10 deneme tutmadı)"); } catch { }
                return;
            }
            int delay = Math.Min(30, 3 * (1 << Math.Min(failures - 1, 4)));
            Set(Ps5State.Retrying, $"{problem} – {delay} sn sonra yeniden denenecek");
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); } catch (OperationCanceledException) { break; }
        }
    }

    private sealed class RunResult
    {
        public bool Streamed;
        public string Problem = "Bağlantı koptu.";
        public string? Fatal;
    }

    private async Task<RunResult> RunStreamAsync(CancellationToken ct)
    {
        var result = new RunResult();
        var log = new List<string>();
        Process p;
        lock (_procLock)
        {
            p = Ps5Helper.StartProcess("stream", _pairing.Host, _pairing.Key, _pairing.Morning,
                Convert.ToHexString(_account).ToLowerInvariant(), _fps.ToString(), "1080");
            _proc = p;
        }
        long lastFrame = Environment.TickCount64;
        bool streaming = false;
        int quitReason = -1;
        string quitText = "";

        var errTask = Task.Run(async () =>
        {
            string? l;
            while ((l = await p.StandardError.ReadLineAsync()) != null)
            {
                Ps5Helper.WriteLog("  " + l);
                lock (log) { log.Add(l); if (log.Count > 80) log.RemoveAt(0); }
            }
        });

        var outTask = Task.Run(() =>
        {
            var stream = p.StandardOutput.BaseStream;
            var hdr = new byte[12];
            try
            {
                while (true)
                {
                    stream.ReadExactly(hdr);
                    uint type = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(0));
                    int a = (int)BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(4));
                    int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(8));
                    if (len < 0 || len > 64 * 1024 * 1024) break;
                    var data = new byte[len];
                    stream.ReadExactly(data);
                    if (type == 2)
                    {
                        lastFrame = Environment.TickCount64;
                        if (a > 0 && len % (a * 4) == 0)
                            try { FrameReceived?.Invoke(data, a, len / (a * 4)); } catch (Exception ex) { Ps5Helper.WriteLog("Kare işleyici: " + ex.Message); }
                    }
                    else if (type == 1)
                    {
                        string json = Encoding.UTF8.GetString(data);
                        Ps5Helper.WriteLog("  olay " + json);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        string ev = root.TryGetProperty("event", out var e) ? e.GetString() ?? "" : "";
                        if (ev == "connected")
                        {
                            streaming = true;
                            result.Streamed = true;
                            lastFrame = Environment.TickCount64;
                            Set(Ps5State.Streaming, $"Bağlı (doğrudan) – {_pairing.Name}");
                        }
                        else if (ev == "pin")
                        {
                            bool incorrect = root.TryGetProperty("incorrect", out var inc) && inc.ValueKind == JsonValueKind.True;
                            if (_passcode != null && !incorrect) Send(p, "pin " + _passcode);
                            else
                            {
                                result.Fatal = incorrect
                                    ? "PS5 giriş şifresini kabul etmedi. Ayarlar → Bağlantı → PS5 bağlantısı sayfasında doğru 4 haneli şifreyi girin."
                                    : "PS5 bu hesap için 4 haneli giriş şifresi istiyor. Ayarlar → Bağlantı → PS5 bağlantısı sayfasında şifreyi girin.";
                                Send(p, "stop");
                            }
                        }
                        else if (ev == "quit")
                        {
                            quitReason = root.TryGetProperty("reason", out var rr) && rr.ValueKind == JsonValueKind.Number ? rr.GetInt32() : -1;
                            quitText = (root.TryGetProperty("name", out var nm) ? nm.GetString() : "") +
                                       (root.TryGetProperty("text", out var tx) && tx.GetString() is { Length: > 0 } t ? " – " + t : "");
                        }
                    }
                }
            }
            catch (EndOfStreamException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { Ps5Helper.WriteLog("Akış okuma: " + ex.Message); }
        });

        // Bekle: süreç bitene ya da iptal edilene kadar; akarken görüntü 20 sn gelmezse yeniden kur.
        while (!p.HasExited)
        {
            if (ct.IsCancellationRequested) break;
            if (streaming && Environment.TickCount64 - lastFrame > 20_000)
            {
                Ps5Helper.WriteLog("20 sn'dir görüntü gelmiyor; bağlantı yeniden kuruluyor.");
                result.Problem = "PS5'ten görüntü gelmiyor";
                break;
            }
            await Task.Delay(250);
        }
        StopProcess();
        await Task.WhenAny(outTask, Task.Delay(3000));
        await Task.WhenAny(errTask, Task.Delay(1000));

        string all;
        lock (log) all = string.Join("\n", log);
        if (result.Fatal == null)
        {
            if (all.Contains("80108b09", StringComparison.OrdinalIgnoreCase) || all.Contains("80108b02", StringComparison.OrdinalIgnoreCase)
                || all.Contains("80108b26", StringComparison.OrdinalIgnoreCase) || quitReason == 13)
                result.Fatal = "PS5 bu bilgisayarın eşlemesini kabul etmedi. Ayarlar → Bağlantı → PS5 bağlantısı sayfasından yeniden eşleyin.";
            else if (quitReason == 5)
                result.Problem = "PS5'teki Uzaktan Oynatma hizmeti hata verdi (devam ederse PS5'i tamamen kapatıp açın)";
            else if (quitReason == 4 || all.Contains("80108b10", StringComparison.OrdinalIgnoreCase))
                result.Problem = "Uzaktan Oynatma başka bir cihazda kullanılıyor (PS Remote Play uygulaması açıksa kapatın)";
            else if (all.Contains("80108b12", StringComparison.OrdinalIgnoreCase))
                result.Problem = "PS5'te ikinci kullanıcıda Ayarlar › Sistem › Uzaktan Oynatma › 'Uzaktan Oynatmayı Etkinleştir' kapalı";
            else if (quitReason == 11 || quitReason == 12)
                result.Problem = "PS5 bağlantıyı kapattı" + (quitText.Length > 0 ? $" ({quitText})" : "");
            else if (quitReason >= 0 && result.Problem == "Bağlantı koptu.")
                result.Problem = "Bağlantı koptu" + (quitText.Length > 0 ? $" ({quitText})" : "");
        }
        return result;
    }

    private static void Send(Process p, string line)
    {
        try { p.StandardInput.WriteLine(line); p.StandardInput.Flush(); } catch { }
    }

    private void StopProcess()
    {
        Process? p;
        lock (_procLock) { p = _proc; _proc = null; }
        if (p == null) return;
        try
        {
            if (!p.HasExited)
            {
                Send(p, "stop");
                try { p.StandardInput.Close(); } catch { }
                if (!p.WaitForExit(3000)) p.Kill(true);
            }
        }
        catch { }
        finally { p.Dispose(); }
    }
}
