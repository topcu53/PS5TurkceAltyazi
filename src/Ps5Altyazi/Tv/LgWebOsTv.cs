using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace Ps5Altyazi.Tv;

/// <summary>
/// LG webOS TV ile geliştirici modu üzerinden çalışma (LG'nin resmi webOS CLI'ının yaptığının aynısı):
/// - Anahtar: http://TV:9991/webos_rsa (Developer Mode uygulamasında "Key Server" açıkken), TV'de görünen Passphrase ile şifreli.
/// - Bağlantı: SSH, port 9922, kullanıcı "prisoner".
/// - Kurulum: .ipk dosyası /media/developer/temp'e yüklenir, luna://com.webos.appInstallService/dev/install çağrılır.
/// - Başlatma: luna://com.webos.applicationManager/launch (parametrelerle: PC adresi, HDMI girişi).
/// - Geliştirici modu süresini uzatma: LG'nin önerdiği gibi com.palmdts.devmode uygulaması extend=true ile başlatılır.
/// </summary>
public sealed class LgWebOsTv
{
    public const string AppId = "tr.ps5altyazi.tv";
    public const string AppVersion = "1.0.7";
    private const string LunaSend = "/usr/bin/luna-send-pub";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    public string Ip { get; }
    private readonly string _keyPem;
    private readonly string _passphrase;

    /// <summary>
    /// SSH sunucu anahtarı sabitleme (ilk bağlantıda öğrenilir, sonra her bağlantıda karşılaştırılır):
    /// ağdaki başka bir cihaz TV gibi davranıp bağlantıyı ele geçiremesin. (ip → SHA256 parmak izi)
    /// </summary>
    public static Func<string, string?>? GetPinnedHostKey;
    public static Action<string, string>? SavePinnedHostKey;

    public LgWebOsTv(string ip, string keyPem, string passphrase)
    {
        if (!IsValidIPv4(ip)) throw new ArgumentException("TV IP adresi geçersiz (ör. 192.168.1.34).");
        Ip = ip.Trim();
        _keyPem = keyPem;
        _passphrase = passphrase.Trim();
    }

    public static bool IsValidIPv4(string? ip) =>
        ip != null && IPAddress.TryParse(ip.Trim(), out var a) && a.AddressFamily == AddressFamily.InterNetwork &&
        ip.Trim().Count(ch => ch == '.') == 3;

    /// <summary>TV'deki Key Server'dan şifreli SSH anahtarını alır.</summary>
    public static async Task<string> FetchKeyAsync(string ip, CancellationToken ct)
    {
        if (!IsValidIPv4(ip)) throw new InvalidOperationException("TV IP adresi geçersiz (ör. 192.168.1.34).");
        try
        {
            string pem = await Http.GetStringAsync($"http://{ip.Trim()}:9991/webos_rsa", ct);
            if (!pem.Contains("PRIVATE KEY")) throw new InvalidOperationException("TV geçerli bir anahtar göndermedi.");
            return pem;
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(
                "TV'ye ulaşılamadı. TV'de Developer Mode uygulamasını açıp 'Key Server'ı AÇIN ve uygulamayı ekranda bırakın; " +
                "TV ile bilgisayarın aynı ağda olduğunu ve IP adresini kontrol edin.");
        }
    }

    /// <summary>Ağdaki LG webOS TV'leri SSDP ile bulur (ip → ad).</summary>
    public static async Task<List<(string Ip, string Name)>> DiscoverAsync(TimeSpan timeout)
    {
        var found = new Dictionary<string, string>();
        const string req = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n" +
                           "ST: urn:lge-com:service:webos-second-screen:1\r\n\r\n";
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.ReceiveTimeout = 500;
        var data = Encoding.ASCII.GetBytes(req);
        var ep = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        for (int i = 0; i < 2; i++) await udp.SendAsync(data, data.Length, ep);

        var end = DateTime.Now + timeout;
        while (DateTime.Now < end)
        {
            var recvTask = udp.ReceiveAsync();
            // Süre dolunca yarım kalan alma işlemi, UdpClient kapanınca hata verir: sessizce gözlemle.
            _ = recvTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            var done = await Task.WhenAny(recvTask, Task.Delay(end - DateTime.Now > TimeSpan.Zero ? end - DateTime.Now : TimeSpan.Zero));
            if (done != recvTask) break;
            var r = recvTask.Result;
            string text = Encoding.UTF8.GetString(r.Buffer);
            string ip = r.RemoteEndPoint.Address.ToString();
            string name = text.Split("\r\n")
                .FirstOrDefault(l => l.StartsWith("DLNADeviceName.lge.com:", StringComparison.OrdinalIgnoreCase))?
                .Split(':', 2)[1].Trim() ?? "LG TV";
            found[ip] = Uri.UnescapeDataString(name);
        }
        return found.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private ConnectionInfo CreateConnectionInfo()
    {
        var key = new PrivateKeyFile(new MemoryStream(Encoding.ASCII.GetBytes(_keyPem)), _passphrase);
        return new ConnectionInfo(Ip, 9922, "prisoner", new PrivateKeyAuthenticationMethod("prisoner", key))
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
    }

    private SshClient NewSsh() => Pin(new SshClient(CreateConnectionInfo()));

    private T Pin<T>(T client) where T : BaseClient
    {
        client.HostKeyReceived += (_, e) =>
        {
            string fp = e.FingerPrintSHA256;
            string? known = GetPinnedHostKey?.Invoke(Ip);
            if (string.IsNullOrEmpty(known)) { SavePinnedHostKey?.Invoke(Ip, fp); e.CanTrust = true; }
            else e.CanTrust = string.Equals(known, fp, StringComparison.Ordinal);
            if (!e.CanTrust)
                Logging.Log.Warn($"TV'nin SSH anahtarı değişmiş ({Ip}). TV sıfırlandıysa TV Modu'nda 'Bağlan' ile yeniden eşleştirin; " +
                                 "değilse ağınızda TV gibi davranan başka bir cihaz olabilir – bağlantı reddedildi.");
        };
        return client;
    }

    private string Run(SshClient ssh, string command)
    {
        var cmd = ssh.RunCommand(command);
        return (cmd.Result ?? "") + (cmd.Error ?? "");
    }

    /// <summary>Bağlantıyı dener; TV modeli / webOS sürümü bilgisini döndürür.</summary>
    public Task<string> TestAsync() => Task.Run(() =>
    {
        try
        {
            using var ssh = NewSsh();
            ssh.Connect();
            string info = Run(ssh, $"{LunaSend} -n 1 luna://com.webos.service.tv.systemproperty/getSystemInfo '{{\"keys\":[\"modelName\",\"sdkVersion\"]}}'");
            ssh.Disconnect();
            string model = Extract(info, "modelName"), sdk = Extract(info, "sdkVersion");
            return model.Length > 0 ? $"{model} (webOS SDK {sdk})" : "bağlandı";
        }
        catch (Exception ex) when (ex.Message.Contains("passphrase", StringComparison.OrdinalIgnoreCase) || ex is Renci.SshNet.Common.SshException && ex.Message.Contains("key", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Passphrase yanlış görünüyor. TV'deki 6 karakteri büyük/küçük harfe dikkat ederek girin.");
        }
    });

    /// <summary>Programın içindeki TV uygulamasını (.ipk) TV'ye kurar.</summary>
    public Task InstallAsync(IProgress<string> progress, CancellationToken ct) => Task.Run(() =>
    {
        using var ipk = Assembly.GetExecutingAssembly().GetManifestResourceStream("tvapp.webos.ipk")
                        ?? throw new InvalidOperationException("TV uygulama paketi programın içinde bulunamadı.");
        var ci = CreateConnectionInfo();
        const string remote = "/media/developer/temp/ps5altyazi-tv.ipk";

        progress.Report("TV'ye bağlanılıyor…");
        using var ssh = Pin(new SshClient(ci));
        ssh.Connect();
        Run(ssh, "/usr/bin/test -d /media/developer/temp || /bin/mkdir -p /media/developer/temp");

        progress.Report("Uygulama TV'ye yükleniyor…");
        using (var sftp = Pin(new SftpClient(ci)))
        {
            sftp.Connect();
            sftp.UploadFile(ipk, remote, true);
            sftp.Disconnect();
        }

        progress.Report("TV'de kuruluyor…");
        string payload = JsonSerializer.Serialize(new { id = "com.ares.defaultName", ipkUrl = remote, subscribe = true });
        Run(ssh, $"{LunaSend} -n 1 luna://com.webos.appInstallService/dev/install '{payload}'");

        // Kurulum arka planda sürer: uygulama listede görünene kadar bekle
        var deadline = DateTime.Now.AddSeconds(90);
        while (DateTime.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            Thread.Sleep(1500);
            string apps = Run(ssh, $"{LunaSend} -n 1 luna://com.webos.applicationManager/dev/listApps '{{}}'");
            if (apps.Contains($"\"{AppId}\"") && apps.Contains($"\"{AppVersion}\""))
            {
                Run(ssh, $"/bin/rm -f {remote}");
                ssh.Disconnect();
                progress.Report("Kuruldu.");
                return;
            }
        }
        ssh.Disconnect();
        throw new TimeoutException("Kurulum 90 sn içinde tamamlanmadı. Geliştirici modunun açık olduğunu kontrol edip tekrar deneyin.");
    }, ct);

    /// <summary>TV uygulaması yoksa veya eski sürümse kurar. Kurulum yaptıysa true.</summary>
    public async Task<bool> EnsureInstalledAsync(IProgress<string> progress, CancellationToken ct)
    {
        bool current = await Task.Run(() =>
        {
            using var ssh = NewSsh();
            ssh.Connect();
            string apps = Run(ssh, $"{LunaSend} -n 1 luna://com.webos.applicationManager/dev/listApps '{{}}'");
            ssh.Disconnect();
            return apps.Contains($"\"{AppId}\"") && apps.Contains($"\"{AppVersion}\"");
        }, ct);
        if (current) return false;
        progress.Report("TV uygulaması güncelleniyor…");
        await InstallAsync(progress, ct);
        return true;
    }

    /// <summary>TV uygulamasını PC adresi ve HDMI girişiyle başlatır (açıksa yeniden yapılandırır).</summary>
    public Task LaunchAsync(string pcIp, int hdmi) => Task.Run(() =>
    {
        if (!IsValidIPv4(pcIp)) throw new InvalidOperationException("Bilgisayarın ağ adresi bulunamadı.");
        hdmi = Math.Clamp(hdmi, 1, 4);
        using var ssh = NewSsh();
        ssh.Connect();
        string payload = JsonSerializer.Serialize(new { id = AppId, @params = new { server = pcIp, hdmi } });
        string res = Run(ssh, $"{LunaSend} -n 1 luna://com.webos.applicationManager/launch '{payload}'");
        ssh.Disconnect();
        if (!res.Contains("\"returnValue\":true") && !res.Contains("\"returnValue\": true"))
        {
            if (res.Contains("not found", StringComparison.OrdinalIgnoreCase) || res.Contains("NOT_EXIST", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("TV uygulaması kurulu değil. Önce 'TV'ye Kur'a basın.");
            throw new InvalidOperationException("TV uygulaması başlatılamadı: " + Trim(res));
        }
    });

    /// <summary>Geliştirici modu süresini (en çok 1000 saat) baştan başlatır.</summary>
    public Task ExtendDevModeAsync() => Task.Run(() =>
    {
        using var ssh = NewSsh();
        ssh.Connect();
        string payload = JsonSerializer.Serialize(new { id = "com.palmdts.devmode", @params = new { extend = true } });
        Run(ssh, $"{LunaSend} -n 1 luna://com.webos.applicationManager/launch '{payload}'");
        ssh.Disconnect();
    });

    private static string Extract(string json, string key)
    {
        int i = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (i < 0) return "";
        int c = json.IndexOf(':', i);
        int q1 = json.IndexOf('"', c + 1);
        int q2 = q1 >= 0 ? json.IndexOf('"', q1 + 1) : -1;
        return q1 >= 0 && q2 > q1 ? json[(q1 + 1)..q2] : "";
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s.Trim();
}
