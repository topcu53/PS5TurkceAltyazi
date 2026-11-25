# Üçüncü taraf bileşenler

## PS5 bağlantı yardımcısı – `ps5-baglanti.exe` (ayrı program)

Altyazı+ bu yardımcıyı alt süreç olarak çalıştırır ve onunla yalnız komut satırı / standart giriş-çıkış üzerinden konuşur.
Yardımcı, chiaki-ng kütüphanesine bağlandığı için **GNU AGPL-3.0** lisanslıdır ([tools/ps5-baglanti/LICENSE](tools/ps5-baglanti/LICENSE)).
Kaynak kodu ve derleme adımları bu depodadır: `tools/ps5-baglanti/`, `third-party/chiaki-ng` (git alt modülü),
`.github/workflows/build.yml` ("ps5-baglanti" işi).

| Bileşen | Lisans | Kaynak |
|---|---|---|
| chiaki-ng (`libchiaki`, Remote Play protokolü) | AGPL-3.0 + OpenSSL istisnası | https://github.com/chiaki-ng/chiaki-ng |
| FFmpeg (libavcodec H.264 çözücü, libswscale, libavutil; statik) | LGPL-2.1+ | https://ffmpeg.org |
| nanopb | zlib | https://github.com/nanopb/nanopb |
| Jerasure / GF-Complete | BSD-3-Clause | https://github.com/ceph/jerasure |
| curl | curl lisansı | https://curl.se |
| OpenSSL 3 | Apache-2.0 | https://www.openssl.org |
| json-c | MIT | https://github.com/json-c/json-c |
| libevent | BSD-3-Clause | https://libevent.org |
| miniupnpc | BSD-3-Clause | https://miniupnp.tuxfamily.org |

## Windows programı

| Bileşen | Lisans | Kaynak |
|---|---|---|
| .NET çalışma ortamı, WPF | MIT | https://github.com/dotnet |
| Microsoft.Data.Sqlite / SQLite | MIT / Public Domain | https://github.com/dotnet/efcore |
| SSH.NET | MIT | https://github.com/sshnet/SSH.NET |
| Microsoft Edge WebView2 SDK | BSD-3-Clause (SDK) | https://www.nuget.org/packages/Microsoft.Web.WebView2 |
| Yazı tipleri (`src/Ps5Altyazi/Resources/Fonts`) | SIL Open Font License 1.1 / Apache-2.0 (ayrıntı: Fonts/LISANS.txt) | Google Fonts vb. |

"PlayStation", "PS5" ve "PS Remote Play" Sony Interactive Entertainment Inc.'in ticari markalarıdır. Altyazı+ Sony ile
bağlantılı değildir; Sony'nin yazılımını değiştirmez, kopya korumasını aşmaz, PSN girişini Sony'nin kendi sayfasında yaptırır.
