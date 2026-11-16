# Altyazı+ Kurulum Kılavuzu

**Altyazı+**, PS5 oyunlarının İngilizce yazılarını Türkçe olarak gösterir: konuşma altyazılarını ve oyun sırasındaki tuş ipuçlarını ("Greet", "Press Ⓧ to …" gibi), görev ve ipucu yazılarını çevirir. Menüleri çevirmez.

Yapımcı: **Burak Topcu**

---

## Nasıl çalışır?

```
PS5 ──HDMI──▶ TV                 (kablo bağlantısı değişmez)
 │
 └─ PS Remote Play ─▶ Windows PC  (Altyazı+ ekrandaki İngilizce yazıyı okur → DeepL ile Türkçeye çevirir)
                         │
                         └─ ev ağı ─▶ TV'deki Altyazı+ uygulaması (PS5 görüntüsünün üstüne Türkçeyi çizer)
```

- PS5 TV'ye bağlı kalır. Capture card gerekmez.
- PC, oyunu **PS Remote Play** penceresinden okur.
- Türkçe yazı üç yerden birinde görünür:
  - **TV'de**: LG, Samsung ve Android TV / Google TV'de. TV'ye küçük bir uygulama kurulur.
  - **PC ekranında**: Remote Play penceresinin üstünde.
  - **Telefon ya da tablette**: tarayıcıdan açılır.

> Türkçe yazı, İngilizce yazının **ekranda görünmesinden yaklaşık 0,3–0,6 saniye sonra** gelir. Bunun bir kısmı PS Remote Play'in kendi gecikmesidir.

---

## 1. Gerekenler

| | |
|---|---|
| **PS5** | Ayarlar → Sistem → Uzaktan Oynatma → **Uzaktan Oynatmayı Etkinleştir** açık olmalı. |
| **Windows 10 / 11 bilgisayar** | PS5 ve TV ile **aynı ev ağında** olmalı. PS5 ve PC'yi kabloyla bağlamak gecikmeyi azaltır. |
| **PS Remote Play** | Sony'nin ücretsiz uygulaması: https://remoteplay.dl.playstation.net/remoteplay/ |
| **DeepL API Free anahtarı** | Ücretsiz, aylık karakter kotası var (program kullanımı gösterir): https://www.deepl.com/pro-api (Kayıt sırasında kart bilgisi istenebilir. Ücretsiz planda para çekilmez.) |
| **TV** (isteğe bağlı) | LG webOS 4+ (2018 ve sonrası) • Samsung Tizen (2016 ve sonrası) • Android TV / Google TV |

---

## 2. Bilgisayar kurulumu

1. **Programı indirin:** GitHub sayfasındaki **Releases** bölümünden `AltyaziPlus.exe` dosyasını indirin. Kurulum gerekmez; dosyayı istediğiniz klasöre koyun.
2. **Windows uyarısı çıkarsa:**
   - *"Windows bilgisayarınızı korudu"* uyarısında → **Ek bilgi** → **Yine de çalıştır**.
   - *Akıllı Uygulama Denetimi (Smart App Control)* programı engellerse: Windows Güvenliği → Uygulama ve tarayıcı denetimi → Akıllı Uygulama Denetimi ayarları → **Kapalı**.
3. **PS Remote Play'i açın:** PS5'e bağlanın ve pencereyi **büyütün (tam ekran)**. Remote Play ayarlarında *Çözünürlük: 1080p*, *Kare hızı: Yüksek* önerilir.
4. **`AltyaziPlus.exe`'yi açın:** Program Remote Play penceresini kendisi bulur.
5. **İngilizce okuma paketini kurun (bir kez):** Program "İngilizce OCR paketi yok" derse **İngilizce OCR Paketini Kur** düğmesine basın, yönetici iznini onaylayın. Kurulum bitince programı kapatıp yeniden açın.
6. **Çeviri ayarlarını girin:** **Çeviri Ayarları…** → DeepL anahtarınızı yapıştırın → **Kaydet ve Test Et**.
   - Anahtarınız bilgisayarınızda **şifreli** saklanır, başka yere gönderilmez.
   - DeepL kotası dolarsa program kendiliğinden yedek servise geçer: isteğe bağlı **Google Gemini** (ücretsiz anahtar: https://aistudio.google.com/apikey, kart gerekmez), isteğe bağlı Azure, son olarak MyMemory.
7. **Oyunda altyazıları açın:** Oyunun kendi ayarlarından altyazıları açın.
8. **Altyazı yerini tanıtın (bir kez):** Biri konuşurken **Otomatik Bul**'a basın. Ya da **Elle Seç** ile altyazının çıktığı bölgeyi fareyle çizin.

9. **Yazı tipi:** Ana penceredeki **🔤 Yazı Tipi** düğmesinden listeden seçin ya da **＋ Yazı tipi ekle** ile kendi
   `.ttf` / `.otf` dosyanızı ekleyin. **Kendi yazı tipiniz (başka yol):** Bir `.ttf` / `.otf` dosyasını `AltyaziPlus.exe` ile aynı klasöre koyun; program
   açılışta onu altyazı yazı tipi yapar (boyut yine oyundan ölçülür). Ya da: Altyazı Görünümü → Sabit yazı tipi.
   **Otomatik yazı tipi: hiçbir şey yapmanız gerekmez.** Program ilk birkaç altyazıda oyunun yazısını ölçer; Türkçeyi en benzer
   ücretsiz yazı tipiyle (49 font, tırnaklı/tırnaksız dahil), aynı harf boyunda ve renkte yazar. Başka oyuna geçtiğinizde
   yazının değiştiğini kendisi fark eder ve yeniden öğrenir. Tutmadıysa **Altyazı Görünümü… → Baştan öğren**.

### Kısayollar (her pencerede çalışır)

- **F9** – çevirili ekran görüntüsü (TV'de görünen Türkçe yazıyla birlikte; *captures* klasörüne `ceviri-*.png`)

| Tuş | İşlev |
|---|---|
| **F6** | Çeviriyi başlat / durdur |
| **F8** | Türkçe yazıyı göster / gizle |

---

## 3. TV kurulumu

Önce programda **📺 TV Modu…** penceresini açın ve TV markanızı seçin.

### 3A. LG TV (webOS) — en kolayı, program kendisi kurar

1. **LG geliştirici hesabı açın (ücretsiz):** https://webostv.developer.lge.com → *Sign In* → *Create Account*.
2. **TV'de geliştirici modunu açın:**
   - TV'de **LG Content Store**'dan **Developer Mode** uygulamasını kurun, açın ve hesabınızla giriş yapın.
   - **Dev Mode Status**'u açın. TV yeniden başlar.
   - Developer Mode uygulamasını tekrar açın, **Key Server**'ı açın ve uygulamayı ekranda bırakın.
3. **TV Modu → 2. adım:** **Ağda Bul**'a basın (ya da TV'nin IP adresini yazın).
4. **TV Modu → 3. adım:** Developer Mode ekranının sol altındaki 6 karakterli **Passphrase**'i yazın → **Bağlan**.
5. **TV Modu → 4. adım:** PS5'in takılı olduğu HDMI girişini seçin → **TV'ye Kur** → **TV'de Başlat**.
6. **Bundan sonra:** **F6**'ya basınca TV uygulaması kendiliğinden açılır.

> LG'nin geliştirici modu sürelidir; süre dolarsa kurulan uygulama kaldırılır. Program F6 ile başlatıldığında süreyi (en fazla 12 saatte bir) kendiliğinden uzatır. Elle uzatmak için: TV Modu → **Geliştirici Süresini Uzat**. Uygulama silinmişse **TV'ye Kur** ile yeniden kurun.

### 3B. Android TV / Google TV

Sony, Philips, TCL, Hisense (Google TV), Xiaomi, Nvidia Shield, Chromecast with Google TV vb.

1. **Uygulamayı indirin:** **Releases** bölümünden `AltyaziPlus-AndroidTV.apk` dosyasını indirin.
2. **TV'ye kurun.** İki yoldan birini seçin:
   - **Downloader uygulamasıyla:** TV'de Play Store'dan **Downloader**'ı kurun. Ayarlar → Uygulamalar → Güvenlik → **Bilinmeyen kaynaklar** bölümünde Downloader'a izin verin. Downloader'da APK'nın indirme adresini açın.
   - **USB bellekle:** APK'yı belleğe kopyalayın. TV'de bir dosya yöneticisiyle (ör. *File Commander*) açıp kurun.
3. **Uygulamayı açın:** TV'de **Altyazı+**'yı açın. Aynı ağdaki bilgisayarı **kendiliğinden bulur**. Bulamazsa **Menü** tuşuyla PC adresini girin (adres TV Modu penceresinde yazar).
4. **HDMI girişini seçin:** Kumandada **Sol / Sağ** ile PS5'in HDMI girişini seçin.
5. **TV uygulama içinde HDMI göstermiyorsa** (bazı markalar engelliyor):
   - **Mavi** tuşa basın. Uygulama *katman moduna* geçer.
   - Sonra TV'nin kendi kumandasıyla PS5'in HDMI girişine geçin. Türkçe yazı onun üstünde görünür.
   - TV "diğer uygulamaların üzerinde gösterme" iznini soramıyorsa izni bilgisayardan bir kez verin:
     ```
     adb connect TV_IP_ADRESI
     adb shell appops set tr.altyaziplus.tv SYSTEM_ALERT_WINDOW allow
     ```
     (`adb`, Google'ın "SDK Platform Tools" paketindedir. TV'de: Ayarlar → Cihaz Tercihleri → Hakkında → Yapı'ya 7 kez basın → Geliştirici seçenekleri → **USB / Ağ hata ayıklama** açık.)

### 3C. Samsung TV (Tizen)

Samsung, TV'ye uygulama yüklemeyi yalnızca **Tizen Studio** ve **Samsung sertifikası** ile izin verir. Bu işlem bir kez yapılır, yaklaşık 30 dakika sürer.

1. **Tizen Studio'yu kurun:** https://developer.samsung.com/smarttv/develop/getting-started/setting-up-sdk/installing-tv-sdk.html
   - Kurulumdan sonra **Package Manager**'ı açın.
   - **Extension SDK** sekmesinden **TV Extensions** ve **Samsung Certificate Extension**'ı kurun.
2. **TV'de geliştirici modunu açın:**
   - TV'de **Uygulamalar** ekranına gidin. Kumandada sırayla **1 2 3 4 5** tuşlarına basın.
   - **Developer mode: On** yapın. **Host PC IP** kısmına bilgisayarınızın IP adresini yazın (TV Modu penceresinde yazar).
   - TV'yi kapatıp açın.
3. **Tizen Studio'yu TV'ye bağlayın:** **Tools → Device Manager** → *Remote Device Manager* → **+** → TV'nin IP adresini girin → bağlanın.
4. **Sertifikayı oluşturun:** **Tools → Certificate Manager** → **+** → **Samsung** → **TV**. Samsung hesabınızla giriş yapın. Yazar ve dağıtıcı sertifikasını oluşturun; bağlı TV'nin kimliği (DUID) kendiliğinden eklenir.
5. **Projeyi hazırlayın:**
   - **File → New → Tizen Project** → *Template* → *TV* → *Web Application* → **Basic Project** seçin. Ad olarak `AltyaziPlus` verin.
   - **Releases** bölümünden `AltyaziPlus-Samsung-Tizen.zip` dosyasını indirip açın.
   - İçindeki `index.html`, `app.js`, `config.xml` ve `icon.png` dosyalarını projenin klasörüne kopyalayın (var olanların üzerine yazın).
6. **Kurun:** Projeye sağ tıklayın → **Run As → Tizen Web Application**. Uygulama TV'ye kurulur ve açılır.
7. **Ayarlayın:** Uygulamada **Kırmızı** tuşla PC adresini girin. **Sol / Sağ** ile PS5'in HDMI girişini seçin.

> Uygulama TV'de kalıcıdır. Sertifika yalnızca bu TV için geçerlidir; başka bir Samsung TV için 4. ve 6. adımları tekrarlayın.

### TV kumandası (TV uygulamasında)

| Tuş | İşlev |
|---|---|
| Sol / Sağ | HDMI girişi |
| Yukarı / Aşağı | Yazı boyutu |
| OK | Bilgi |
| Kırmızı (Android'de Menü) | PC adresi |
| Mavi (yalnızca Android) | Katman modu |
| Geri | Çık |

> **TV markanız listede yoksa** (Hisense VIDAA, Roku, Panasonic vb.) bu TV'ler başka uygulamaların HDMI görüntüsünün üstüne yazmasına izin vermez. Türkçe yazıyı **PC ekranında** ya da **telefon / tablette** görebilirsiniz.

---

## 4. Telefon / tablet

TV Modu penceresinde yazan adresi (`http://PC_IP:47800/`) aynı Wi-Fi'deki telefonun tarayıcısında açın. Türkçe altyazı orada görünür.

---

## 5. Sorun giderme

| Sorun | Çözüm |
|---|---|
| "PS Remote Play bulunamadı" | Remote Play açık ve PS5'e bağlı olmalı. Pencereyi simge durumuna küçültmeyin. |
| Altyazı okunmuyor | Remote Play'i tam ekran ve 1080p yapın. Altyazı yerini **Otomatik Bul** ile yeniden tanıtın. |
| Türkçe gelmiyor | **Çeviri Ayarları → Kaydet ve Test Et** ile anahtarı kontrol edin. İnternet bağlantınızı kontrol edin. |
| TV uygulaması bağlanmıyor | PC ve TV aynı ağda olmalı. Windows Güvenlik Duvarı sorarsa **Özel ağlar**a izin verin (port 47800). |
| LG: "TV'ye ulaşılamadı" | Developer Mode uygulamasında **Key Server** açık olmalı ve uygulama ekranda kalmalı. |
| LG: "SSH anahtarı değişmiş" | TV sıfırlandıysa TV Modu'nda **Bağlan** ile yeniden eşleştirin. |
| Türkçe geç geliyor | PS5 ve PC'yi **kabloyla** (Ethernet) bağlayın. Remote Play ayarları: çözünürlük **1080p**, kare hızı **Yüksek**. Programda **Hedef FPS: 60**. Windows'ta güç modunu **En iyi performans** yapın. Dizüstünde şarj kablosu takılı olsun. |
| Kayıtlar (log) | Programdaki **Klasörü Aç** düğmesi → `logs` klasörü. Sorun bildirirken bu dosyayı ekleyin. |

---

## 6. Gizlilik ve güvenlik

- **Okunan yazılar:** Ekrandan okunan İngilizce yazılar çeviri için yalnızca **DeepL**'e (kota dolunca seçtiğiniz yedek servise) gönderilir. Görüntü hiçbir yere gönderilmez.
- **Şifreli saklanan bilgiler:** API anahtarları ve TV eşleştirme anahtarı bilgisayarınızda Windows'un şifrelemesiyle (DPAPI) saklanır. Yalnızca sizin Windows hesabınız açabilir.
- **TV yayını:** Yayın (port 47800) yalnızca **ev ağınızdaki** cihazlardan gelen bağlantıları kabul eder; internetten gelen bağlantıları reddeder.
- **LG TV doğrulaması:** Program, LG TV'nin SSH kimliğini ilk bağlantıda kaydeder. Ağda TV gibi davranan başka bir cihaz çıkarsa bağlantıyı reddeder.
- **Sony'nin kuralları:** PS5'e ve PSN'ye hiçbir müdahale yapılmaz; yalnızca Sony'nin kendi Remote Play uygulaması kullanılır. İçerik koruması (HDCP) aşılmaz.

---

## 7. Kaynaktan derlemek (geliştiriciler için)

- **Windows programı:** .NET 8 SDK kuruluyken `build.bat` dosyasını çalıştırın. Çıktı: `publish\AltyaziPlus.exe`.
- **TV uygulamaları:**
  - LG paketi: `tv/build-webos.sh` (gerekli araç: `npm i -g @webos-tools/cli`).
  - Android TV: `gradle -p tv/android assembleDebug`.
  - Samsung: yukarıdaki 3C adımları.
- **GitHub üzerinden:** `main` dalına her yüklemede GitHub Actions bunları kendiliğinden derler ve **Releases** sayfasına koyar. Sürüm numarası `VERSION` dosyasındadır.
