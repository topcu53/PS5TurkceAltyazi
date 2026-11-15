# Altyazı+ – Geliştirme geçmişi

## v1.2 – En düşük gecikme

- Yakalama 60 FPS (önceden 30): altyazı en geç ~17 ms içinde görülür. Kare başına bir tam görüntü kopyası daha az.
- Altyazı işleme, arayüzdeki diğer işlerin önüne alındı (en yüksek öncelik); önizleme görüntüleri saniyede en çok 8 kez
  çiziliyor (önceden her karede 1080p çizim altyazıyı bekletiyordu).
- Altyazı belirirken (fade-in) ilk kare okunamazsa sonraki karede hemen yeniden okunuyor (önceden aynı altyazı tekrar
  okunmuyor, Türkçe gecikiyor / hiç gelmiyordu).
- Çeviri bağlantısı sürekli açık / sıcak tutuluyor (ilk çeviride ~150-300 ms'lik bağlantı kurma süresi yok).
- Yedekli çeviri: DeepL 0,7 sn içinde yanıt vermezse yedek servis aynı anda denenir, önce gelen kullanılır.

## v1.1 – Oyunun yazı tipiyle altyazı

- Türkçe altyazı, **oyunun altyazı yazısına en benzer yazı tipiyle**, orijinal harflerle **aynı boyda** ve **aynı renkte** yazılır
  (PC ve TV). Program altyazının harf kalınlığını, genişliğini ve x-yüksekliğini ölçer; programla gelen 30 açık lisanslı,
  Türkçe karakterli yazı tipi arasından en benzerini seçer.
- Her oyun için ayrı hatırlanır (Altyazı Görünümü → Oyun). Font elle de seçilebilir ya da oyunun kendi font dosyası
  (sizde varsa, .ttf/.otf) "Dosyadan ekle" ile kullanılabilir.
- TV, yazı tipini PC'den indirir (yalnızca fonts klasöründeki dosyalar sunulur). TV uygulaması 1.0.7.

## v1.0 – İlk genel sürüm

- Ad: **Altyazı+** (yapımcı: Burak Topcu). Ayarlar ve çeviri hafızası eski sürümlerle uyumlu.
- **Tüm TV'ler için ortak TV uygulaması** (`tv/web`): LG webOS, Samsung Tizen (tizen.tvwindow ile HDMI), Android TV / Google TV
  (TvView ile HDMI; olmazsa katman modu). Android TV, PC'yi ağda kendiliğinden bulur.
- **Güvenlik:** yayın sunucusu yalnızca ev ağından bağlantı kabul eder, en çok 16 ekran, yarım bağlantılar 5 sn'de kapanır;
  yavaş bir ekran artık programı bekletmez (ekran başına gönderim kuyruğu). LG TV SSH kimliği ilk bağlantıda sabitlenir.
  TV IP adresleri doğrulanır.
- LG geliştirici modu süresi 12 saatte bir uzatılır (önceden haftada bir).
- GitHub Actions: her yüklemede Windows .exe + Android .apk derlenir ve Releases'e konur (sürüm: VERSION dosyası).
- Kurulum kılavuzu: KURULUM.md.

## v0.13 – Yalnızca oynanış ekranı, tuş simgeleri korunuyor

- **Menüler çevrilmiyor:** duraklatma menüsü / harita algılanınca (çok sayıda yazı) hiçbir şey gösterilmez.
  Yalnızca konuşma altyazıları + oynanıştaki tuş ipuçları (Greet, Antagonize, Press Ⓧ to …), görev / ipucu yazıları.
- **Ekranın ortası okunmuyor** (oyun dünyası: tabela, doku → yanlış okuma); daha hızlı ve daha temiz.
- **Tuş simgesi asla örtülmez:** "Press Ⓧ to skip" → "Atlamak için Ⓧ tuşuna basın"; Ⓧ yerinde kalır, Türkçe parçalar
  simgenin soluna / sağına yazılır. "Greet Ⓛ2" gibi sağda simgeli ipuçlarında Türkçe sola doğru büyür.
  Çok satırlı ipucu kutularında simgenin birebir kopyası Türkçe cümlenin içinde gösterilir.
- **Eski kutu kalmıyor:** kutudaki beyaz yazı deseni her karede kontrol edilir; yazı değişince (Greet → Defuse) Türkçe
  o anda kalkar ve hemen yeniden okunur. Üst üste binen kutular temizlenir. Tek satırlık ipuçları bölünmez ("KIZDIRMA / K" yok).
- TV uygulaması 1.0.5 (F6'da kendiliğinden güncellenir).

## v0.12 – Ekran çevirisi düzeltmeleri (ilk TV testinden)

- **Hizalama:** buton simgeleri ("(m", "@", Ⓧ) artık yazının parçası sayılmıyor → kutu simgeye göre büyümüyor/kaymıyor;
  Türkçe kutu İngilizce yazının dikey ortasına oturuyor. Yazı boyu BÜYÜK HARF / küçük harf farkına göre hesaplanıyor.
- **Çift kutu yok:** aynı yerde okunan farklı metinler ("SHOW INFO" / "SHOW INFO (m") tek öğe sayılıyor.
- **Yanlış okuma düzeltmesi:** gömülü İngilizce kelime listesiyle tek harf hataları düzeltiliyor ("UNLINE" → "ONLINE",
  "SETTINCS" → "SETTINGS"); sözlükte karşılığı olmayan çöp okumalar eleniyor.
- **Daha iyi okuma:** renk kanalı (kırmızı zemin üstü koyu yazı gibi) + kontrast germe; tek geçişte okuma.
- **Hız:** menü açılıp ekran durulunca hemen okunuyor (sabit 0,7 sn beklemiyor), tam ekran OCR tek geçiş;
  bekleyen bütün yazılar tek DeepL isteğinde çevriliyor (önceden en fazla 4 tane, kalanlar sonraki taramaya kalıyordu).
- TV uygulaması 1.0.4 (F6'da kendiliğinden güncellenir).
- Kelime listesi wordfreq verisinden (CC BY-SA 4.0) üretilmiştir.

## v0.11 – Ekran çevirisi (menüler, HUD, görev yazıları) – F7

- **F7** (veya üstteki "Ekran çevirisi – menü/HUD (F7)" kutusu) altyazı dışındaki bütün İngilizce yazıları çevirir:
  ana menü, duraklatma menüsü, ayarlar, harita, envanter, görev hedefleri, ipuçları, kenardaki HUD yazıları.
- Türkçe, İngilizce yazının **tam üstüne, aynı boyda, opak siyah kutuda** çıkar: PC'de Remote Play'in üstünde,
  TV'de HDMI görüntüsünün üstünde (TV uygulaması 1.0.3, F6'da kendiliğinden güncellenir).
- Ekranın tamamı ~0,7 sn'de bir okunur (küçük yazılar için 4 parçaya bölünüp büyütülerek). Menü kapanınca / değişince
  Türkçe kutular hemen kalkar. Aynı menü tekrar açıldığında çeviri hafızadan anında gelir (kota harcamaz).
- Arka plan dokusundan çıkan anlamsız okumalar elenir; şüpheli bir yazı ancak iki taramada aynı yerde görülürse çevrilir.
- Altyazı alanı bu moddan hariçtir (altyazılar eskisi gibi ayrı ve daha hızlı işlenir).
- Not: menüde gezinirken Türkçe ~0,5–1 sn gecikmeyle gelir; DeepL kotasını altyazıdan biraz daha hızlı kullanır.

## v0.10

- **Türkçe = orijinal boyut, tam üstünde, opak:** TV'de Türkçe altyazı İngilizce altyazının satır yüksekliğiyle aynı boyda yazılır,
  aynı kutuya birebir oturur ve zemini tam siyahtır (İngilizce alttan görünmez). PC'de "örtme modu"nda da aynı.
  (Altyazı Görünümü → "Örtme modunda yazı boyutu orijinal altyazıyla aynı olsun", varsayılan açık.)
- **OCR çöpü filtresi:** arka plan dokularından gelen "IIVO II", "Idåi" gibi okumalar elenir (gerçek 137 altyazının hepsi geçer).
- Siyah örtü kutusu yalnızca gerçek bir altyazı okunduktan sonra konur (arka plana yanlış kutu düşmez).
- TV uygulaması 1.0.2 (F6 / "TV'de Başlat" ile kendiliğinden güncellenir).

## v0.9 – TV testinden gelen düzeltmeler

- **İngilizce altyazı TV'de görünmesin:** İngilizce metin PC'de ilk görüldüğü karede TV'de üstüne siyah bir kutu konur,
  Türkçe çeviri aynı kutunun yerine gelir. (TV Modu → "İngilizce altyazıyı siyah kutuyla ört", varsayılan açık.)
  Not: TV görüntüyü HDMI'dan doğrudan, PC ise Remote Play'den biraz geç aldığı için İngilizce yazının ilk anı kısa bir süre görünebilir.
- **Daha düşük gecikme:** altyazı ilk görüldüğü karede okunur (2. kare beklenmez) ve hedef FPS 30'a çıkarıldı.
- **Kısa altyazılar ("Hello."):** algılama şartı gevşetildi; İngilizcesi Türkçe gelmeden kaybolsa bile çeviri gösterilir ve
  Türkçe en az 1,5 sn ekranda kalır.
- TV uygulaması 1.0.1: F6'da (veya "TV'de Başlat"ta) eski sürüm kendiliğinden güncellenir.

**Durum:** Aşama 1–6 ✅ • v0.8: Türkçe altyazıyı **TV'de, PS5 görüntüsünün üstünde** gösterme (LG webOS 4+). Samsung Tizen ve Android TV sonraki sürümde.

## TV Modu nasıl çalışır?

PS5 → HDMI → TV bağlantısı **değişmez**. TV'ye küçük bir uygulama ("Altyazı+") kurulur:
- TV uygulaması PS5'in bağlı olduğu HDMI girişini tam ekran gösterir (webOS'un harici giriş gömme özelliği),
- PC'deki program Türkçe altyazıyı ev ağı üzerinden (WebSocket, port 47800) TV'ye yollar, TV uygulaması üstüne çizer.
- PC'deki altyazı ayarları (yazı boyutu, zemin, genişlik, İngilizce altyazıyı örtme) TV'ye de uygulanır.
- Aynı yayın **telefon/tablet tarayıcısında** da açılabilir: `http://PC-IP:47800/`

**Dikkat:** Görüntü TV'nin kendi HDMI ekranı yerine bir uygulamanın içinde gösterildiği için TV'nin oyun modu / ALLM / HDR'ı
devreye girmeyebilir; oyun gecikmesini kendiniz deneyin.

## Kurulum (LG)

1. **Geliştirici modu (bir kez):** webostv.developer.lge.com'da ücretsiz hesap → TV'de LG Content Store'dan **Developer Mode** →
   giriş yap → **Dev Mode Status: ON** (TV yeniden başlar) → uygulamayı tekrar aç → **Key Server: ON**, ekranda bırak.
2. PC programında **📺 TV Modu…** → **Ağda Bul** (veya TV IP'sini yazın) → TV'de görünen 6 karakterli **Passphrase** → **Bağlan**.
3. **TV'ye Kur** → PS5'in HDMI girişini seçin → **TV'de Başlat**.
4. Windows güvenlik duvarı sorarsa **Özel ağlar** için izin verin.
5. Sonraki seferlerde: Remote Play'e bağlanın, **F6** – TV uygulaması da kendiliğinden açılır (ayar: "F6 ile ... otomatik aç").
   Geliştirici modu en çok 1000 saat açık kalır; program haftada bir süreyi kendiliğinden yeniler.

TV kumandası (uygulamada): Sol/Sağ = HDMI girişi • Yukarı/Aşağı = yazı boyutu • OK = bilgi • Kırmızı = PC IP ayarı • Geri = çıkış.

TV uygulamasının kaynak kodu: `tv/webos/` (paket: `tv/tr.ps5altyazi.tv_1.0.0_all.ipk`, programın içine gömülüdür).

---

# Aşama 6: Kısayollar ve Tek Tuşla Kullanım

**Durum:** Aşama 1–4 ✅ • Aşama 5 ✅ (altyazı penceresi; metin değişiminden Türkçeye ort. 355 ms) • Aşama 6 bu sürümde.

## Aşama 6 – yenilikler

- **F6:** Çeviri sistemini başlat / durdur (yakalama → OCR → çeviri → altyazı). **F8:** Türkçe altyazıyı göster / gizle.
  Kısayollar geneldir: Remote Play tam ekranken de çalışır. Basınca altyazı yerinde kısa bir sarı bildirim görünür.
- **Tek tuşla başlatma:** Remote Play bağlıyken F6 her şeyi başlatır (Remote Play penceresini kendisi bulur, OCR ve çeviriyi açar).
  Alan seçilmemişse veya çeviri servisi yoksa bildirimde uyarır.
- **Açılışta:** Remote Play hazır değilse açık uyarı; PS Remote Play kuruluysa "Şimdi açılsın mı?" sorar (resmi uygulama açılır;
  PS5 bağlantısı ve PSN girişi kullanıcıya aittir, atlatılmaz). Remote Play bağlanınca çeviri kendiliğinden başlar.
- **"PS Remote Play'i Aç"** düğmesi.
- **Örtme modu (Altyazı Görünümü):** Türkçe kutu oyunun İngilizce altyazısının tam üstüne yerleşir ve onu örter
  (zemin koyuluğu %85+ önerilir).
- **İki satırlık altyazı düzeltmesi:** otomatik alan altta da 2. satıra yer bırakır; eski kayıtlı alan bir kez kendiliğinden
  aşağı genişletilir. Altyazı sonradan uzarsa (2. satır geç gelirse) yeniden çevrilir.

### Aşama 6 test adımları (kabul kriteri 5)

1. Remote Play tam ekran, oyun açık. **F6** → "Çeviri durduruldu (F6)" bildirimi; altyazılar artık çevrilmez.
2. Tekrar **F6** → "Çeviri başladı (F6)"; diyalogda Türkçe altyazı gelir.
3. **F8** → "Türkçe altyazı gizlendi (F8)"; tekrar **F8** → açılır.
4. (İsteğe bağlı) Altyazı Görünümü → "İngilizce altyazının üstüne yerleştir" + koyuluk %85 → İngilizce yazı görünmemeli.
5. Logda `[F6]` / `[F8]` satırları olmalı. Log + fotoğraf gönderin.

---

# Aşama 5: Türkçe Altyazı Penceresi

**Durum:** Aşama 1–3 ✅ • Aşama 4 ✅ (DeepL ort. 172 ms; metin değişiminden Türkçeye tipik 250–360 ms) • Aşama 5 bu sürümde.

## Aşama 5 – yenilikler

- **Ayrı Türkçe altyazı penceresi:** yalnızca Türkçe metin; büyük, kalın, beyaz yazı; yarı saydam siyah zemin; ekranın alt ortası.
- **Tek / iki satır:** metin pencereye göre kaydırılır; 2 satıra sığmazsa yazı boyutu en fazla %40 küçülür. Alt kenar sabit, yukarı doğru büyür.
- **Otomatik gizlenme:** altyazı bitince (veya yeni altyazı gelince, çevirisi gelene kadar) pencere boşalır – eski çeviri yeni altyazının üzerinde kalmaz.
- **Altyazı Görünümü…:** yazı boyutu (20–96), zemin saydamlığı (%0–100), genişlik (ekranın %30–100'ü), her zaman üstte, konum düzenleme (sürükle), varsayılan konum, örnek altyazı. Hepsi anında uygulanır ve kaydedilir.
- **Remote Play'i engellemez:** altyazı penceresi fare tıklamalarını geçirir, odak çalmaz, görev çubuğunda görünmez. Taşımak için "Konumu düzenle".
- **Göster/gizle:** üst çubuktaki "Türkçe altyazı penceresi" kutusu (Aşama 6'da F8'e bağlanacak).
- **Yanlış algılama azaltıldı:** altyazı satırının alan ortasına yakın olması şartı; parmak izi yalnızca satır kutusundan (arka plan hareketi OCR'ı geciktirmez); BELİRDİ/KAYBOLDU log gürültüsü kaldırıldı.

### Aşama 5 test adımları

1. Remote Play tam ekranken programı açın; diyalogda Türkçe altyazı ekranın alt ortasında görünmeli, altyazı bitince kaybolmalı.
2. **Altyazı Görünümü…** → yazı boyutu / saydamlık / genişliği değiştirin; "Konumu düzenle" ile taşıyın. Programı kapatıp açın → ayarlar korunmalı.
3. Altyazı penceresi açıkken Remote Play penceresine tıklayabildiğinizi kontrol edin.
4. Uzun bir altyazıda (2 satır) metnin taşmadığını kontrol edin. Bir ekran fotoğrafı + log gönderin.

---

# Aşama 4: Türkçe Çeviri (DeepL)

**Durum:** Aşama 1 ✅ • Aşama 2 ✅ (ayarlar kalıcı) • Aşama 3 ✅ (en-US OCR ort. 5 ms, metin değişiminden ~110 ms) • Aşama 4 bu sürümde.

## Aşama 4 – yenilikler

- **DeepL API (Free/Pro)** ile İngilizce → Türkçe. Önceki 3 altyazı DeepL'e "bağlam" olarak gönderilir (ücretlendirilmez) → diyaloğa uygun, tutarlı çeviri.
- **Çeviri Ayarları…** penceresi: anahtarı gir → bağlantı, bu ayki kota ve örnek çeviri test edilir.
- **Anahtar güvenli saklanır:** Windows DPAPI ile şifreli (`settings.json` içinde düz metin yok).
- **SQLite önbellek:** `%LOCALAPPDATA%\PS5TurkceAltyazi\ceviri-onbellek.db`. Aynı cümle bir daha API'ye gitmez; OCR farkı olan %92+ benzer cümleler de önbellekten gelir.
- **Eski çeviri yeni altyazının üzerine yazılmaz:** altyazı değişince/kaybolunca bekleyen istek iptal edilir, geç gelen sonuç atılır.
- **Hata yönetimi:** geçersiz anahtar (403), kota doldu (456), çok istek (429, 1 kez yeniden dener), sunucu hatası, zaman aşımı (6 sn), internet yok – hepsi Türkçe mesajla gösterilir; program çökmez.
- **Gecikme ölçümü:** logda `ÇEVİRİ (TR): "..." [DeepL Free x ms, metin değişiminden Türkçeye y ms]` ve 30 sn'de bir çeviri özeti.
- OCR: satır başındaki tek harfli kelimeler ("I will.") için yan pay artırıldı.
- Modüler yapı: `ITranslator` arayüzü – ileride çevrimdışı motor eklenebilir.

### Otomatik yedek servis (v0.4.1)

Sıra: **DeepL → Azure Translator (anahtar girildiyse) → MyMemory (anahtarsız)**.

- DeepL kotası dolunca (hata 456, ya da açılışta kullanım = sınır) program **kendiliğinden** sıradaki servise geçer ve logda/panelde yazar.
- Kotası dolan servis 6 saatte bir (MyMemory için 1 saatte bir) yeniden denenir; kota yenilenince **DeepL'e kendiliğinden döner**. Bu bekleme süreleri `settings.json`'da saklanır (program kapanıp açılsa da gereksiz istek atılmaz).
- Geçici hata (zaman aşımı 3 sn, 429, sunucu hatası, internet) → yalnızca o altyazı için sıradaki servis denenir; art arda 3 hatada servis 1 dk atlanır.
- Geçersiz anahtar → o servis anahtar düzeltilene kadar atlanır.
- Ücretsiz sınırlar: DeepL 500.000 kr/ay • Azure F0 2.000.000 kr/ay (kayıt için kart gerekir) • MyMemory ~5.000 kr/gün, e-posta ile ~50.000 kr/gün.
- Önbellek tüm servisler için ortaktır: bir kez çevrilen cümle hiçbir servise tekrar gitmez.

### Aşama 4 test adımları

1. https://www.deepl.com/pro-api → **DeepL API Free** hesabı açın, API anahtarını kopyalayın (":fx" ile biter).
2. Uygulamada **Çeviri Ayarları…** → anahtarı yapıştırın → **Kaydet ve Test Et**. "✔ Bağlantı başarılı" ve örnek çeviri görünmeli.
3. Diyaloglu bir sahne oynayın; sağ panelde TÜRKÇE ÇEVİRİ altyazıyla birlikte değişmeli, altyazı bitince silinmeli.
4. Aynı diyaloğu tekrar dinleyin (ör. kamp sohbetleri) → logda `önbellek` görünmeli.
5. Log dosyasını gönderin (gecikme ölçümleri oradan okunacak).

Not: Türkçe altyazı şu an ana penceredeki test panelinde görünür. Büyük, ayrı altyazı penceresi Aşama 5'te gelecek.

---

# Aşama 3: İngilizce Altyazı Okuma (OCR)

## Aşama 3 – yenilikler

- **OCR motoru:** Windows'un yerleşik OCR'ı (Windows.Media.Ocr) – ek kütüphane yok, CPU'da hızlı. Modüler yapı: ileride PaddleOCR/Tesseract eklenebilir.
- **İngilizce dil paketi:** Yoksa uygulama uyarır ve "İngilizce OCR Paketini Kur" düğmesini gösterir (Windows'un resmi `Add-WindowsCapability` komutu, yönetici onayı ister). Paket yokken Türkçe motorla yedek olarak çalışır.
- **Ön işleme:** Yalnızca bulunan altyazı satırları kırpılır, griye çevrilir, kontrast gerilir, ters çevrilir (siyah yazı/beyaz zemin) ve satır yüksekliği ~40 px'e büyütülür. (RDR2 örnekleriyle doğrulandı.)
- **Tekrarsız okuma:** OCR yalnızca altyazı varken, metin görüntüsü yerine oturduğunda (2 kare aynı) ve son okunandan farklıysa çalışır. Aynı altyazı tekrar okunmaz.
- **Kararlılık:** OCR farkları (%85+ benzerlik) yeni altyazı sayılmaz; geç gelen eski sonuç yeni altyazının üzerine yazılmaz; altyazı kaybolunca metin temizlenir; anlamsız sonuçlar atlanır.
- **Ölçüm:** Logda her altyazı `ALTYAZI (EN): "..." [OCR x ms, metin değişiminden y ms sonra]` olarak yazılır; 30 sn'de bir OCR özeti.
- **Görüntü dosyası testinde** de OCR çalışır. "Kareyi PNG Kaydet" artık OCR girdisini de kaydeder (`ocr-girdisi-*.png`).

### Aşama 3 test adımları

1. Uygulamayı açın; logda "OCR motoru: Windows OCR (en-US)" görünmeli. "İngilizce OCR dil paketi YOK" derse düğmeyle kurun ve uygulamayı yeniden başlatın.
2. Logda "Kayıtlı altyazı alanı yüklendi" olmalı (Aşama 2 ayar kalıcılığı).
3. 2–3 dakika diyaloglu bir sahne oynayın. Sağ panelde İngilizce metin değişmeli, logda `ALTYAZI (EN)` satırları çıkmalı.
4. Logdaki metinleri ekrandaki altyazılarla karşılaştırın; hatalı okunan varsa o anda "Kareyi PNG Kaydet"e basın.
5. Log dosyasını ve (varsa) hatalı okunan karelerin PNG'lerini gönderin.

---

# Aşama 2: Altyazı Alanı Seçimi

## Aşama 2 – yenilikler

- **Oyun alanı kırpma:** Başlık çubuğu ve Remote Play'in siyah bantları otomatik atılır; önizleme ve tüm işlemler yalnızca 16:9 oyun görüntüsü üzerinde yapılır (kutucukla kapatılabilir).
- **Elle seçim:** "▭ Elle Seç" → önizlemede altyazı bölgesini fareyle çizin (Esc = iptal). Alan oyun alanına göre oransal kaydedilir; pencere boyutu değişse de geçerli kalır.
- **Otomatik bulma:** "✦ Otomatik Bul" → 30 sn içinde ekranda ortalanmış altyazı görülürse alanı kendisi belirler (2 satırlık altyazıya pay bırakır). Mini harita ve tuş ipuçları gibi HUD öğelerini ayırt eder.
- **Canlı görünüm:** Seçilen alan büyütülmüş olarak ve "metin maskesi" (OCR'ın göreceği) olarak gösterilir; bulunan metin satırları önizlemede yeşil çerçeveyle işaretlenir.
- **Altyazı VAR / YOK** göstergesi ve logda "Altyazı BELİRDİ / KAYBOLDU" kayıtları (3. aşamada OCR yalnızca altyazı varken çalışacak).
- **Görüntü dosyasıyla test:** PS5 açık olmadan, kaydedilmiş bir PNG üzerinde alan seçimi ve otomatik bulma denenebilir.
- **Ayarlar kalıcı:** `%LOCALAPPDATA%\PS5TurkceAltyazi\settings.json` (alan, yöntem, FPS, kırpma).

### Aşama 2 test adımları

1. Remote Play'i tam ekran / büyütülmüş yapın, Remote Play ayarlarında çözünürlüğü **1080p** seçin.
2. Uygulamayı açın; önizlemede başlık çubuğu ve siyah bantlar olmamalı.
3. Biri konuşurken **Otomatik Bul**'a basın → sarı kesikli çerçeve altyazının çevresine yerleşmeli.
4. Birkaç diyalog boyunca sağ panelde **ALTYAZI ALGILAMA: VAR / YOK** doğru değişmeli; logda BELİRDİ/KAYBOLDU satırları görünmeli.
5. Uygulamayı kapatıp açın → alan korunmuş olmalı.
6. Altyazı varken **Kareyi PNG Kaydet** → `kare-*.png` ve `altyazi-alani-*.png` oluşur. Bunları ve logu gönderin.

---

# Aşama 1: Remote Play Görüntü Yakalama Testi

Bu sürüm, spesifikasyondaki **yalnızca 1. aşamayı** içerir: PS Remote Play penceresinin görüntüsünün gerçekten yakalanabildiğini kanıtlamak.
OCR, çeviri ve altyazı penceresi bu aşama sizin bilgisayarınızda **BAŞARILI** sonucu verdikten sonra eklenecek.

## Ne yapar?

| Gereksinim | Karşılığı |
|---|---|
| Remote Play penceresini tespit et | `RemotePlay.exe` süreci / "PS Remote Play" başlıklı pencereyi otomatik bulur (birden fazlaysa en büyüğünü seçer). Açık değilse uyarı verir ve 2 sn'de bir arar; bulunca yakalamayı kendisi başlatır. |
| Pencerenin görüntüsünü yakala | **Windows Graphics Capture** (önerilen; pencere arkada kalsa da çalışır). Yedek/teşhis yöntemi: **PrintWindow**. |
| Görüntüyü programda göster | Sol tarafta canlı önizleme. |
| Siyah / boş kare / koruma / erişim hatası | Her kare analiz edilir: SİYAH EKRAN, BOŞ / TEK RENK, DEĞİŞMİYOR, GEÇERLİ. Pencerede ekran yakalama koruması (display affinity) varsa raporlanır, **aşılmaz**. Küçültülmüş pencere ve kare gelmemesi ayrıca uyarılır. |
| Gerçek oyun görüntüsü mü? | **10 sn Doğrulama Testi**: kareler geçerli mi (≥%80), ayrıntı var mı, hareket var mı ölçülür; sonuç BAŞARILI / KISMEN / BAŞARISIZ olarak loglanır ve örnek kare PNG olarak kaydedilir (gözle kontrol için). |
| Başarılı / başarısız durumları logla | `%LOCALAPPDATA%\PS5TurkceAltyazi\logs\yakalama-YYYYMMDD.log` |

## Gereksinimler

- Windows 10 (2004 / 19041 veya üzeri) ya da Windows 11, 64 bit
- [.NET 8 SDK (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)
- Sony PS Remote Play (resmi uygulama), PS5'e bağlı

## Derleme ve çalıştırma

1. Klasörü bir yere çıkarın.
2. `build.bat` dosyasına çift tıklayın. Derleme bitince uygulama `publish\PS5TurkceAltyazi.exe` olarak açılır.

Visual Studio 2022 kullanıyorsanız `src\Ps5Altyazi\Ps5Altyazi.csproj` dosyasını açıp F5 ile de çalıştırabilirsiniz.

## Test adımları

1. PS Remote Play'i açın, PS5'e bağlanın, bir oyuna girin (İngilizce altyazılı bir sahne idealdir).
2. Uygulamayı açın. Remote Play penceresi bulunursa yakalama otomatik başlar ve önizlemede oyun görüntüsü çıkar.
3. Oyunda karakteri/kamerayı hareket ettirirken **"✔ 10 sn Doğrulama Testi"**'ne basın.
4. Sağ panelde ve logda `AŞAMA 1 DOĞRULAMA: BAŞARILI` görmelisiniz.
5. Siyah/boş gelirse yöntemi **PrintWindow** yapıp testi tekrarlayın; iki sonucu da karşılaştırın.

**Bana geri gönderin:** log dosyası (`Log / Görüntü Klasörünü Aç` düğmesi) ve `captures\dogrulama-*.png`. Bunlarla 1. aşamayı kapatıp 2. aşamaya (altyazı alanı seçimi) geçeceğim.

## Bilinen notlar

- **Remote Play penceresini simge durumuna küçültmeyin.** Windows küçültülmüş pencereden görüntü veremez. Pencere başka pencerelerin arkasında kalabilir.
- Windows 10'da yakalanan pencerenin çevresinde sarı çerçeve görünebilir; bu Windows'un gizlilik göstergesidir. Windows 11'de program bunu kapatmayı dener.
- PS5, bazı içerikleri (ör. video/film uygulamaları) Remote Play'e göndermez; bu durumda siyah ekran doğal sonuçtur ve aşılmaz.
- Windows Graphics Capture yalnızca pencere içeriği değiştiğinde kare gönderir. Oyun duraklatılmışken "KARE GELMİYOR" / "DEĞİŞMİYOR" uyarısı normaldir.

## TV'de altyazı hakkında

Mevcut bağlantı şartlarında (PS5 doğrudan TV'ye bağlı, capture card yok, PC–TV arasında HDMI yok) **Türkçe altyazıyı TV görüntüsünün üzerine bindirmek mümkün değildir.** Altyazı yalnızca PC ekranında gösterilecek. Bu özellik tamamlanmış gibi gösterilmeyecek.

## Proje yapısı

```
build.bat
src/Ps5Altyazi/
  Ps5Altyazi.csproj        .NET 8 WPF, net8.0-windows10.0.22621.0
  app.manifest             Per-Monitor V2 DPI farkındalığı
  App.xaml(.cs)            Genel hata yakalama
  MainWindow.xaml(.cs)     Test arayüzü, doğrulama testi, durum paneli
  Capture/
    WindowFinder.cs        Pencere listeleme, Remote Play tespiti, koruma kontrolü
    WgcCaptureSource.cs    Windows Graphics Capture (+ D3D11 aygıtı, HWND interop)
    PrintWindowCaptureSource.cs  GDI yedek yöntem
    FrameAnalyzer.cs       Siyah / boş / donmuş kare ve hareket analizi
    NativeMethods.cs       Win32 P/Invoke
  Logging/Log.cs           Dosya + arayüz logu
```
