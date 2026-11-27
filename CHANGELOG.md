# Altyazı+ – Geliştirme geçmişi

## v2.31 – Tek pencere, düzgün yerleşim, Türkçe yazım

- **Ayar sayfaları artık ayrı pencere değil:** Altyazı görünümü, Çeviri, PS5 bağlantısı, TV kurulumu, Tema, Genel
  ayarlar, oyun seçimi ve PSN girişi ana pencerenin içinde, tıklanan yerde açılır (Ayarlar'da soldaki menü yerinde
  kalır). Üstte "← Geri" ve sayfanın adı durur; Esc ya da başka bir sekmeye geçmek de sayfayı kapatır.
- **Kurulum tek pencerede:** ilk açılışta ana sayfa hiç görünmeden önce kurulum adımları gelir; adımlardaki çeviri
  anahtarları ve PSN girişi de aynı pencerede açılır. "PS5 kurulmadı, kurulum açılsın mı?" sorusu yerine kurulum
  doğrudan açılır; "Altyazı+ güncel" bilgisi de pencere yerine bildirim olarak gösterilir.
- **Pencere küçülüp büyüyünce kaymayan yerleşim:**
  - PS5 / TV / Okuma / Çeviri kutucukları genişliğe göre yazıyı kaydırır; "↻ Yeniden bağlan" düğmesi kutunun
    altında tam genişlikte durur (önceden kesiliyordu).
  - Oyun kutusunda uzun oyun adı "…" ile kısalır, "Değiştir" düğmesi yerinden oynamaz.
  - Dar pencerede büyük düğmeler iki satıra geçer, sağdaki durum sütunu daralır, üst banttaki durum yazısı kısalır.
- **Türkçe yazım:** düğme, başlık ve durum yazıları Türkçe büyük / küçük harf kurallarına göre düzenlendi
  ("Altyazı Görünümünü Düzenle" → "Altyazı görünümünü düzenle", "GÖRÜNTÜ DEĞİŞMİYOR" → "Görüntü sabit" vb.).
  Artık olmayan düğme adlarına yapılan göndermeler güncellendi ("Elle Seç", "Otomatik Bul", "Çeviri Ayarları…",
  "TV Modu" → bugünkü adları); "log" yerine "günlük".
- TV uygulaması 1.0.18 (kurulum ekranındaki yazılar güncellendi).

## v2.30 – "Yeniden bağlan" düğmeleri

- Ana sayfadaki PS5 ve TV kartlarında, bağlantı yokken ya da koptuğunda "↻ Yeniden bağlan" düğmesi çıkar.
  - PS5: bağlantı kapatılıp baştan kurulur.
  - TV: yayın yeniden açılır; LG'de TV uygulaması yeniden başlatılır (gerekirse güncellenir). Bağlanana kadar
    düğme "Bağlanıyor…" gösterir; ~15 sn içinde bağlanmazsa logda ne kontrol edileceği yazar.
- Bağlıyken düğmeler görünmez.

## v2.29 – Saydam zemin gerçekten saydam

- Altyazı zemini %0 seçildiğinde TV'de ve bilgisayarda artık hiçbir yerde siyah kutu çizilmiyor. Önceden "orijinalin
  yerine" yerleşiminde İngilizce altyazıyı örtmek için zemin seçimden bağımsız olarak tam siyah yapılıyor, ayrıca
  İngilizcenin üstüne siyah örtü konuyordu.
- Saydam zeminde orijinal örtülemeyeceği için Türkçe, iki yazı üst üste binmesin diye İngilizcenin hemen üstünde,
  gölgeli yazıyla gösterilir. Zemin %5 ve üzeri seçilirse eski davranış (İngilizceyi örtme) sürer.
- TV uygulaması 1.0.17 (program açılınca TV'deki uygulama kendiliğinden güncellenir).

## v2.28 – Bilgisayarda çeviri: işlemci + ekran kartı (tüm kartlar)

- Bilgisayarda çeviri artık **ekran kartında da** çalışıyor: NVIDIA, AMD ve Intel; DirectX 12 destekli tüm kartlar
  (DirectML). Ekran kartında FP16 model kullanılır; işlemcide INT8 model. Python / CUDA kurulumu gerekmez.
- **Donanım profili** (Ayarlar → Çeviri → 5): işlemci modeli, fiziksel çekirdek / mantıksal işlemci, RAM, ekran kartları
  (VRAM, dahili / ayrık), NVIDIA için sürücü sürümü.
- **Çeviri cihazı:** Otomatik (önerilen) / Yalnız işlemci / Yalnız ekran kartı; birden çok kart varsa kart seçimi;
  işlemci iş parçacığı sayısı (otomatik = fiziksel çekirdek, en çok 8); "ekran kartı hata verirse işlemciye dön".
- **Otomatik mod** iki cihazı ölçer (yükleme, ilk çeviri, ortalama, P50 / P95, CPU %, RAM) ve gerçek gecikmesi en düşük
  olanı seçer. Ekran kartı açılamazsa ya da çalışırken hata verirse (bellek yetersiz vb.) o altyazı hemen işlemcide
  çevrilir, çeviri kesilmez; program hiçbir durumda çökmez.
- **⚡ Hız testi** düğmesi ve **canlı izleme**: programın işlemci % / RAM kullanımı; NVIDIA kartlarda sıcaklık,
  kullanım ve VRAM.
- Uygun ayrık ekran kartı (≥ 2 GB VRAM) varsa ekran kartı modeli (~890 MB) bir kez, arka planda kendiliğinden iner;
  bu sırada çeviri işlemcide sürer. Ekran kartı yoksa hiçbir şey değişmez, işlemci kullanılır.
- Kabul testleri (Windows + Linux): işlemci modu, elle iş parçacığı, ekran kartı yokken Otomatik → işlemci, Yalnız
  ekran kartı + kart yok → açık hata, model yok → çökmeden hata, ekran kartı yükleme hatası → işlemciye dönüş,
  ekran kartı kod yolu (FP16) çıktısı INT8 ile karşılaştırma (7/8 birebir aynı). Gerçek bir ekran kartında DirectML
  testi henüz yapılmadı (test makinelerinde DirectX 12 aygıtı yok).

## v2.27 – Bilgisayarda çeviri kendiliğinden kurulur

- Bilgisayarda (işlemcide, internetsiz) çeviri modeli artık **kendiliğinden** indiriliyor: program açılınca model yoksa
  arka planda indirilir (~530 MB, bir kez), bitince çeviri zincirine eklenir. Düğmeye basmak gerekmez; ilerleme çeviri
  durum satırında ve günlükte görünür. Yarıda kalırsa kaldığı yerden devam eder, hata olursa 2 dk sonra yeniden dener.
- Yeni çeviri modu **"💻 Bilgisayar"** (Ayarlar → Çeviri): çeviri önce bu bilgisayarda yapılır; internet, kota, ekran
  kartı gerekmez, hiç durmaz. 1,5 sn'de bitmezse internet servisi de devreye girer.
- Diğer modlarda da internet servisleri durunca (kota / hız sınırı / zaman aşımı) çeviri bilgisayarda devam eder.

## v2.26 – Altyazı yerinde sabit duruyor

- Türkçe altyazı artık yukarı aşağı oynamıyor: konumu o anki algılanan İngilizce kutusuna göre değil, oyunun altyazı
  yerinin **sabit alt kenarına** göre belirleniyor. Kutunun alt kenarı hep aynı yerde; yazı 2 satıra çıkarsa yalnızca
  yukarı doğru büyüyor. Oyun altyazıyı gerçekten başka yere taşırsa (art arda 4 kez) konum bir kez güncelleniyor.
- Yatayda hep ekranın ortası, genişlik sabit (satır kırılımı her altyazıda değişip kutuyu oynatmıyor).
- TV uygulaması 1.0.16.

## v2.25 – Kesintisiz çeviri: bilgisayarda (internetsiz) çeviri

- **Çeviri durmasının asıl sebebi düzeltildi:** DeepL'in aylık kotası dolmamıştı (%18); servislere aynı anda çok fazla
  istek gidiyordu ("çok sık istek – 429"), DeepL 1 dk, Gemini 2 dk, MyMemory 1 saat devre dışı kalıyordu.
  - Aynı metin için ikinci istek gönderilmiyor (titrek OCR okumaları tek istekte birleşiyor).
  - Servis başına aynı anda en fazla 2 istek, iki istek arasında en az 120 ms.
  - "Çok sık istek" artık kota sayılmıyor: servis yalnızca 10 sn dinlendiriliyor.
  - "Dengeli" mod artık her altyazıyı DeepL ve Gemini'ye birlikte göndermiyor; Gemini yalnız DeepL 600 ms'de
    yanıt vermezse devreye giriyor (Gemini'nin dakikalık sınırı dolmuyor).
- **Bilgisayarda çeviri (yeni):** Ayarlar → Çeviri → "5) Bilgisayarda çeviri" → "⬇ Modeli İndir" (bir kez, ~530 MB).
  Helsinki-NLP opus-mt-tc-big-en-tr modeli işlemcide çalışır: ekran kartı, internet, kota yok; cümle başına ~0,15–0,35 sn.
  Çeviri zincirinin sonunda her zaman hazır bekler → internet servisleri dursa da çeviri devam eder.
- **Eski Türkçe yeni altyazının üstünde kalmıyor:** bambaşka bir altyazı gelip çevirisi 0,8 sn içinde gelmezse ya da
  çevrilemezse önceki Türkçe kaldırılıyor.
- OCR düzeltmeleri: harf yerine rakam okunmuş kelimeler düzeltiliyor ("100k" → "look", "c0me" → "come");
  Türkçede kekelemeden kalan "f- " gibi parçalar siliniyor.

## v2.24 – Üst üste iki altyazı, temiz metin, yabancı dil rengi

- **Üst üste iki altyazı artık birlikte okunuyor:** iki kişi aynı anda konuşup satırlar birbirine değince algılayıcı
  bunu tek ve "fazla yüksek" bir parça sanıp tamamen atıyordu; yalnızca tek satırlık altyazı okunuyordu. Artık blok
  satır satır bölünüyor ve her satır okunuyor.
- **Garip işaretler temizlendi:** •, „, €, ¯, «» gibi simgeler, tek başına duran noktalama, konuşma tireleri ("-Fuck off!"),
  cümle sonundaki fazlalıklar ("die!l", "you?.'"), kelime içine düşmüş nokta/virgül ve eşi olmayan tırnaklar hem
  İngilizceden hem Türkçe çeviriden siliniyor.
- **Başka dilde konuşulan satırlar:** "In Spanish: …" gibi etiketli satırlar "(İspanyolca) …" olarak çevriliyor ve TV'de
  **sarı** renkte gösteriliyor (İspanyolca, Fransızca, Portekizce, Rusça, Çince, Arapça, Kreol… 30'a yakın dil).
- **Altyazı oyunun önüne çıkmıyor:** Türkçe kutu orijinalin alt kenarına hizalanıyor, uzarsa aşağıda kalıyor ve hiçbir
  zaman ekranın üst %35'ine çıkmıyor; "üstte" seçeneğinde yer yoksa orijinalin üzerine (opak zeminle) iniyor.
- TV uygulaması 1.0.15.

## v2.23 – Okunan her altyazı çevrilir

- **Okunup çevrilmeyen altyazılar düzeltildi:** OCR aynı altyazıyı art arda biraz farklı okuyunca (GTA'da aynı anda konuşan
  kişiler, arkadan geçen araba) her yeni okuma bir öncekinin çevirisini iptal ediyordu; çeviri hiç yetişmiyordu.
  Artık aynı altyazının farklı okumaları birbirini iptal etmiyor – hangisi önce gelirse gösteriliyor.
- **Cümle cümle çeviri:** her cümle önbelleğe ayrı girer; "A! B?" okunup sonra "B? C." okununca B tekrar çevrilmez,
  anında gelir. Uzun, çok konuşmacılı altyazılar çok daha hızlı Türkçeye döner.
- Yarım okumalar ("the", "do", "on our", "okay?") yeni altyazı sayılmıyor; asıl çeviriyi bozmuyor.
- Algılama bir an kesilip aynı altyazı yeniden görülünce baştan başlanmıyor (ekran boşalıp dolmuyor).

## v2.22 – Takılı kalan altyazı düzeltildi

- **Türkçe artık ekranda takılı kalmıyor:** İngilizce altyazı tamamen kalktıktan **2 saniye sonra** Türkçe de kesin kalkar.
  (Hata: altyazı kalkıp yerinde bozuk okunan bir şey kalınca gizleme zamanlayıcısı duruyor, Türkçe ekranda kalıyordu.)
- Alanda okunur altyazı kalmadıysa (üst üste iki kez okunamadı) altyazı bitmiş sayılır.
- TV güvenliği: PC'den "sil" mesajı herhangi bir nedenle gelmezse altyazı 15 sn sonra kendiliğinden kalkar.
- Arka arkaya gelen konuşmalarda Türkçe artık yanıp sönmüyor (2 sn içinde yeni altyazı gelirse doğrudan değişir).
- Oyunun etkileşim istemleri ("@ APPLE", "O WARDROBE O DRESS", "@ TURN OFF" gibi tuş simgeli yazılar) artık altyazı
  sanılıp çevrilmiyor ve yazı boyu ölçümünü bozmuyor.
- Günlükteki gecikme ölçümü düzeltildi (önceki boş okumalardan kalan eski zaman sayılmıyor; gerçek gecikme görünür).
- TV uygulaması 1.0.14.

## v2.21 – Doğru boyda altyazı, daha temiz okuma

- **Dev altyazı düzeltildi:** bazı karelerde birden çok satır tek satır sanılıp yazı boyu 136–141 px ölçülüyordu
  (olması gereken ~34 px). Artık ölçüm mantık sınırlarının dışındaysa kullanılmaz; eski hatalı ölçümler silindi.
- **Yeni boy seçimi:** Küçük (38 px) · **Orta (46 px, önerilen)** · Büyük (56 px) · Çok büyük (68 px) – 1080p'ye göre,
  TV'nin çözünürlüğüne göre kendiliğinden ölçeklenir. (Oyun erişilebilirlik kılavuzları ve Channel 4/BBC kuralı:
  TV'de altyazı 1080p'de en az 46 px.)
- **TV yerleşimi standartlara göre:** satır başına ~42 karakter (Netflix/BBC), en fazla 2 satır (sığmazsa yazı biraz
  küçülür), en alt satır ekran yüksekliğinin 1/12'si kadar yukarıda (güvenli alan), kutu ekran dışına taşmaz.
- "Orijinal altyazıyla aynı boy" artık isteğe bağlı (varsayılan kapalı) ve yalnızca ölçüm akla yatkınsa kullanılır.
- **Daha temiz OCR:** okuma zayıfsa ikinci bir yöntem (yalnız kenar çizgili parlak yazı) devreye girer; gökyüzü, beyaz
  araba, ışık arkasındaki altyazılar daha az bozuk okunur → daha az "anlamsız sonuç", Türkçe daha erken gelir.
- TV uygulaması 1.0.13 (program kendisi günceller).

## v2.20 – Kurulum ana pencerede, daha şık pencereler

- İlk açılışta kurulum artık ayrı bir pencerede değil, **doğrudan ana pencerede** açılıyor: solda adımlar
  (Hoş geldin → PS5 → Çeviri → TV → Bitti) ve **ilerleme yüzdesi**; hepsi bitince normal ana sayfa açılır.
  "Şimdilik geç" ile sonraya bırakılabilir (Ayarlar → Bağlantı → Kurulum Adımlarını Aç).
- Ayrı açılan pencereler (TV kurulumu, PS5 bağlantısı, altyazı, çeviri, tema…) yenilendi: büyük başlık, ince ayraç,
  Windows 11'de yuvarlak köşeler.
- Oyun listesine **Grand Theft Auto VI** eklendi.

## v2.19 – Güncelleme kartı, düzgün üst bant

- "Şimdi güncelle" artık üst bantta sekmelerin üstüne binmiyor: sağ sütunun en üstünde ayrı bir **yeni sürüm kartı**
  (sürüm numarası, indirme çubuğu, "Neler değişti?" listesi).
- Üst bant yeniden düzenlendi: sekmeler ortada, hiçbir şey üst üste gelmiyor.
- Durum kutucuklarında uzun yazılar artık kesilmiyor (iki satıra iniyor).

## v2.18 – Oyun seçimi

- Ana ekranda **🎯 Oyun** kutucuğu ve "Değiştir": listeden (son oynananlar üstte + sık oynanan PS5 oyunları) seç ya da adını yaz.
- Her oyunun **kendi altyazı alanı ve yazı tipi** saklanır; oyunu seçince kendiliğinden geri gelir.
- PS5'e doğrudan bağlıyken **PS5'te açık oyun kendiliğinden seçilir** (kapatılabilir).

## v2.17 – Daha hızlı ve daha doğal çeviri

- Ayarlar → Çeviri → **Hız ve kalite**: **Hızlı** (DeepL önde), **Dengeli** (varsayılan: DeepL ve Gemini aynı anda sorulur,
  önce gelen gösterilir – en düşük gecikme), **En doğal** (Gemini önceki satırları da okuyarak yazar, DeepL yedekte).
- Konuşan adı olan satırlarda ("ARTHUR: Let's go.") ad olduğu gibi kalır, yalnız cümle çevrilir.
- Gemini'ye profesyonel Türkçe yerelleştirme talimatı: kelimesi kelimesine değil, Türkçe dublajdaki gibi; sen/siz, deyimler,
  argo ve noktalama kurallı.

## v2.16 – Tek parça pencere

- Ana penceredeki ayrı başlık çubuğu (logo + başlık şeridi) kaldırıldı: uygulama tek parça. Küçült / büyüt / kapat düğmeleri
  üst bandın sağ köşesinde; pencere üst banttan sürüklenir, çift tıklayınca büyür.

## v2.15 – Yeni arayüz

- Üstte sekmeler: **Ana Ekran · Geçmiş · Ayarlar**.
- Ana Ekran: büyük önizleme, altında büyük düğmeler (▶ Çeviriyi Başlat, ✦ Altyazıyı Bul, 🧪 TV'de Dene), canlı çeviri kartı;
  sağda PS5 / TV / Okuma / Çeviri durum kutucukları (renkli simge ve ışıkla) ve hızlı ayarlar.
- Geçmiş: okunan her altyazı İngilizcesi ve Türkçesiyle; yanında günlük ve "Günlüğü kopyala".
- Ayarlar: solda menü (Bağlantı, Altyazı Görünümü, Çeviri, TV, Tema, Genel, Gelişmiş), sağda ilgili kart.

## v2.14 – TV'de çevirinin yeri seçilebilir, kutu yazı kadar

- TV'deki Türkçe kutu artık yazı kadar; orijinal altyazının alanı kadar büyütülmüyor.
- ✎ Altyazı Görünümü → **Çevirinin yeri (TV)**: orijinalin yerine (İngilizce siyah zeminle tamamen örtülür) • çeviri üstte,
  İngilizce altta görünür • İngilizce üstte, çeviri altta • ekranın altında sabit.
- TV uygulaması 1.0.12.

## v2.13 – Altyazı renkleri, zemin seçenekleri; PC ekranında altyazı kapalı

- Türkçe altyazı artık varsayılan olarak yalnız TV'de / telefonda görünür; bilgisayar ekranında (oyun üstünde) gösterilmez.
  İstenirse: ⚙ Ayarlar → "Türkçe altyazıyı bu bilgisayarın ekranında da göster".
- ✎ Altyazı Görünümü'ne **yazı rengi** (otomatik / beyaz / sarı / açık mor / camgöbeği / açık yeşil), **zemin rengi**
  (siyah / koyu mor / gece mavisi / koyu gri), zemin koyuluğu, **köşe yuvarlaklığı** ve **yazı gölgesi** eklendi; TV'de ve
  PC'de anında uygulanır. "Örnek Altyazı Göster" örneği TV'ye de gönderir.
- TV uygulaması 1.0.11 (yeni görünüm seçenekleri; LG'de kendiliğinden güncellenir).

## v2.12 – Yeni tema, ayrı ayar pencereleri, daha temiz altyazı

- Yeni varsayılan tema **Gece Moru** (siyaha yakın zemin, yumuşak mor vurgular); ayrıca **Grafit** ve eski **Okyanus Mavisi**.
  Seçim: **🎨 Tema**.
- Ayrı pencereler: **⚙ Ayarlar** (başlangıç, doğrudan bağlantı, okuma hızı, TV, PC altyazısı, güncelleme denetimi),
  **🌐 Çeviri**, **🎨 Tema**.
- Altyazı alanında altyazıdan başka yazılar (simge, tabela, arayüz) artık okunmuyor: boyu farklı ya da altyazı bloğundan uzak
  satırlar atılıyor (en çok 4 satır). TV'deki siyah kutu da bu yüzden gereksiz büyümüyor.
- Anlamsız okumalar sözlükle de eleniyor (kelimelerin çoğu İngilizce değilse çevrilmez).
- Önizleme küçüldü (en çok 800×450), pencere varsayılan olarak daha küçük açılıyor.

## v2.11 – Kurulum sihirbazı, durum ışıkları, test altyazısı

- İlk açılışta **kurulum sihirbazı**: Hoş geldin → PS5 bağlantısı (ikinci PSN hesabıyla giriş, PS5 eşleme, deneme) → Çeviri
  anahtarları → TV kurulumu (test altyazısıyla) → Bitti. Sonra ana sayfadan ▶ Başlat. Üstteki **🧭 Kurulum** ile yeniden açılır.
- Üst bantta **PS5** ve **TV** durum ışıkları: yeşil bağlı, turuncu bağlanıyor / bekleniyor, kırmızı bağlı değil; durum yazıyla da gösterilir.
- **🧪 Test altyazısı**: TV'ye ve PC'ye "Bu bir test altyazısıdır" gönderir (TV Modu'nda ve sihirbazda da var; LG'de TV uygulaması
  kapalıysa önce açılır).
- Açılışta ve önizlemede artık PS Remote Play'den hiç söz edilmiyor.

## v2.10 – Kurulum dosyası ve "Şimdi güncelle"

- Yeni **AltyaziPlus-Setup.exe**: yönetici izni istemeden kurulur (Başlat menüsü ve masaüstü kısayolu, Program Ekle/Kaldır'dan
  kaldırılabilir).
- Program yeni sürümü kendisi denetler (açılışta ve 6 saatte bir). Yeni sürüm çıkınca üst bantta **⬇ Şimdi güncelle**
  düğmesi görünür; basınca indirilir, sessizce kurulur ve program yeni sürümle yeniden açılır. Ayarlar, PS5 eşlemesi ve
  çeviri önbelleği korunur.

## v2.9 – PS Remote Play uygulaması olmadan PS5'e doğrudan bağlantı (kendi yardımcımız)

- PS5 bağlantısı baştan kendi kodumuzla yazıldı: programın içindeki ayrı yardımcı `ps5-baglanti.exe` (tools/ps5-baglanti),
  chiaki-ng kütüphanesiyle PS5'i bulur, uyandırır, eşleştirir ve görüntüyü Altyazı+'a aktarır. v2.8'deki başka projeden
  alınmış kod ve lisans değişikliği kaldırıldı.
- Açılışta artık "PS Remote Play'i açın" denmiyor: PS5 bağlantısı kurulmamışsa **🎮 PS5 Bağlantısı** kurulumu öneriliyor;
  kuruluysa program PS5'e arka planda kendisi bağlanıyor. PS Remote Play penceresinden okuma yalnız KAYNAK'ta
  "Doğrudan bağlan" kapatılırsa (eski yöntem) kullanılıyor.
- Bağlantı günlüğü: %LOCALAPPDATA%\PS5TurkceAltyazi\ps5-baglanti.log

## v2.8 – (geri çekildi)

## v2.7.1 – Düzeltme

- v2.7'de programın içine eski TV paketi (1.0.9) gömülü kalmıştı; LG'de TV uygulaması her başlatmada yeniden
  kuruluyordu. Paket 1.0.10'a güncellendi.

## v2.7 – LG geliştirici modu kendiliğinden açık kalır

- LG'de **Key Server** artık yalnızca ilk eşleştirmede gerekir. Eşleştirmede TV'nin geliştirici oturum jetonu okunup
  şifreli saklanır; program açıkken geliştirici süresi 6 saatte bir **LG sunucusu üzerinden** yenilenir (TV kapalı olsa
  bile, TV'de Developer Mode ekranı açılmadan).
- TV uygulaması (1.0.10) da açıldığında ve açık kaldıkça süreyi kendisi yeniler; PC kapalıyken bile oturum düşmez.
- Oturum yine de düşerse (LG tarafında kapanırsa) log ne yapılacağını açıkça yazar: bir kereliğine Key Server + 'Bağlan'.

## v2.6 – Yazı tipi yöneticisi

- Ana pencerede "🔤 Yazı Tipi" düğmesi. Altyazı Görünümü'nde tüm yazı tipleri tek listede (her biri kendi görünümüyle;
  eklediğiniz fontlar ★ ile en üstte), "＋ Yazı tipi ekle" ve "🗑 Eklenen fontu kaldır" düğmeleri, seçili fontla canlı
  Türkçe örnek ve eksik Türkçe harf uyarısı. Seçim anında PC ve TV altyazısına uygulanır.

## v2.5 – Varsayılan yazı tipi: Arimo Bold

- Altyazı artık varsayılan olarak her oyunda sabit **Arimo Bold** ile yazılır (programın içinde, Türkçe harfler tam;
  boyut yine oyundaki altyazıdan ölçülür). İstenirse Altyazı Görünümü'nden "Otomatik"e dönülebilir.

## v2.4.1 – Programın yanındaki yazı tipi

- AltyaziPlus.exe ile aynı klasöre bir .ttf/.otf konursa, program açılışta onu kendiliğinden sabit yazı tipi yapar
  (PC ve TV). Altyazı Görünümü'nden istenirse "Otomatik"e dönülebilir.

## v2.4 – Sabit yazı tipi

- Altyazı Görünümü'nde "Otomatik" / "Sabit yazı tipi" seçimi: kendi .ttf/.otf dosyanızı (ör. Mirisch) seçin, her oyunda o
  yazı tipi kullanılır (PC ve TV); boyut yine oyundaki altyazıdan ölçülür. Dosya yalnızca bilgisayarınızda kalır.
- Seçilen fontta eksik Türkçe harf (ğ, ş, ı, İ…) varsa bildirilir; eksik harfler yedek yazı tipiyle gösterilir.

## v2.3 – Modern tema

- Logoya uygun yeni renkler: gece laciverti zemin, elektrik mavisi → camgöbeği vurgular.
- Aç/kapa anahtarları (onay kutuları yerine), koyu açılır listeler, yuvarlak seçenek düğmeleri, ok işaretli katlanır
  bölümler, ince kaydırma çubukları; Başlat düğmesinde mavi parlama. Bütün pencerelerde geçerli.

## v2.2 – Her ekrana sığar

- Ana pencere açılışta ekranın çalışma alanına göre boyutlanır; küçük ekranda (1320×900'den küçük) tam ekran açılır.
- Pencere daraldıkça / kısaldıkça yerleşim kendini ayarlar: sağ panel, çeviri yazısı, logo ve günlük küçülür.
- Ayar pencereleri ekrandan taşmaz; sığmayan kısım kaydırılır.

## v2.1 – Uygulamaya ait başlık çubuğu, bütün pencereler yeni temada

- Windows başlık çubuğu yerine uygulamanın kendi koyu başlık çubuğu (logo, başlık, küçült / büyüt / kapat; sürükleyerek
  taşınır, çift tıkla büyür).
- Altyazı Görünümü, Çeviri Ayarları ve TV Modu pencereleri de aynı koyu temaya geçti (düğmeler, yazı kutuları, bağlantılar).

## v2.0.1 – Yeni logo

- Altyazı Plus logosu (uygulama simgesi, pencere, başlık, GitHub sayfası).

## v2.0 – Yeni arayüz ve logo

- Yeni logo (uygulama simgesi, pencere, görev çubuğu).
- Arayüz baştan düzenlendi: üstte logo, durum ve büyük Başlat düğmesi; sağda "Canlı çeviri" kartı;
  teknik ayarlar ve teşhis görüntüleri "Gelişmiş ve teşhis" bölümünde (kapalı başlar); günlük katlanabilir.
- Oyun içi yazı (tuş ipucu) çevirisi şimdilik rafta: arayüzden ve F7'den kaldırıldı, kod duruyor. Odak altyazıda.

## v1.13 – Ekran çevirisi: çift kutular, kırmızı menüler, at adı

- Aynı yazının iki kez okunması (biri iki satırı kapsayan büyük kutu) artık iki ayrı Türkçe kutu çizmiyor.
- Ekran değişimi, yazının rengine bakmadan algılanıyor: RDR2'nin kırmızı üstüne koyu menüsü kaydırılınca eski
  çeviriler kalkıyor (önceden yalnızca beyaz yazılar izleniyordu).
- Sözlükte olmayan okumalar (oyuncunun ata verdiği ad "Ay Parçası" → "PARGASİ@)") çevrilmiyor.
- Geniş harf aralıklı yazılarda kelime boşluğu tuş simgesi sanılmıyor ("PHOTO MODE"un yarısı açıkta kalmıyor).

## v1.12 – Ekran çevirisi: ekran değişince kalkar, yazılar orijinal boyda

- F7 çevirisi ekrandayken menü kaydırılır ya da ipucu değişirse, yazısı değişen kutular kendiliğinden kalkar;
  ekranın çoğu değiştiyse çevirinin tamamı kalkar (eski çeviri yanlış satırın üstünde kalmaz). Yeniden F7.
- Türkçe yazı ve siyah kutu küçültüldü: İngilizceyle aynı boyda, komşu satırları kapatmıyor / üst üste binmiyor.

## v1.11 – F7: "Ekranı çevir" (anlık, sabit)

- F7 artık ekranı **bir kez** okuyup çevirir: tuş ipuçları, görev ve bilgi yazıları kendi yerlerinde Türkçe görünür ve
  yazı miktarına göre 8–40 sn **sabit** kalır; tekrar F7'ye basınca hemen kalkar. Okuma tek kareden yapıldığı için
  titreme / gidip gelme olmaz; zaman kısıtı olmadığından yazı daha büyütülerek (daha az OCR hatasıyla) okunur.
- Sürekli takip eden eski mod ana penceredeki kutuyla açılabilir (deneysel, varsayılan kapalı).
- Fikir: benzer açık kaynak proje CouchSubs'ın "Ekranı çevir" yaklaşımı (kod alınmadı, kendi uygulamamız).

## v1.10 – Sabit yazı tipi ve boy, sağdaki tuş listesi düzgün

- Yazı tipi/boy artık bir altyazıdan yalnızca bir kez ölçülüyor (kararan kareler tekrar tekrar ölçülmüyor).
- Kararan / soluk ve boyu çok farklı tek tük satırlar (bölüm başlığı vb.) ölçüme girmiyor. Önceki sürümde bir anda
  "Oswald 54 px" seçilmesinin sebebi buydu. Gerçekten başka oyuna geçilirse (5 tutarlı farklı ölçüm) yeniden öğrenilir.
- Benzer fontlar arasında gidip gelme bitti: değişim için en az 8 yeni ölçüm ve belirgin üstünlük gerekir;
  boy %10'a, renk küçük farklara kadar sabit kalır.
- Tuş listesi satırları (yanında tuş simgesi olan) artık iki satırlık tek blok olarak birleştirilmiyor: dev boyutlu
  "BİLGİ GÖSTER / EYER'İ KALDIR" kutuları biter.
- Liste değişince eski Türkçe yazı yanlış satırda kalmıyor (yazının değiştiği daha hassas algılanıyor).

## v1.9 – Tuş ipuçları: kararlı çeviri, tabela yazıları yok

- Yarım / hatalı okumalar artık çevrilmiyor: bir yazı iki taramada birebir aynı okunmadan çeviriye gitmez
  ("FROM HORSE → HORSE'dan", "REMOVES → KALDIRILANLAR" gibi saçma ara çeviriler ve gidip gelmeler biter).
- Aynı yazının farklı okunuşları çeviriyi değiştirmez; yalnızca daha tam bir okuma gelirse güncellenir. Yarım okuma
  Türkçe kutuyu küçültüp İngilizceyi açığa çıkarmaz.
- Alt alta dizili kısa tuş listeleri (RDR2 at menüsü) artık menü sanılmıyor (menü/oyun arasında gidip gelme bitti).
- Üst orta şerit taranmıyor: L2 ile ata bakarken karşıdaki tabelalar ("BOX", "OFFICE") çevrilmez.
- Türkçe kutu İngilizce yazının üst/alt kenarını da örter (küçük İngilizce harfler kutunun dışında görünmez).
- Oyun içi yazılar biraz daha büyütülerek okunuyor (küçük, süslü harflerde daha az OCR hatası).

## v1.8 – Tuş ipuçları: titreme, uzun listeler, büyük harf çevirisi

- Titreme giderildi: arkada at / ışık hareket ettiğinde Türkçe kutu hemen kaldırılmıyor; yazı ~0,1 sn boyunca
  gerçekten değişik görünürse kaldırılır. OCR bir taramada satırı okuyamasa da yazı ekranda duruyorsa kutu kalır.
- Uzun tuş listeleri (Bilgi göster, Eyeri çıkar, Fırçala, Besle, Yönlendir, Okşa, Kaç…) artık menü sanılmıyor:
  yanında tuş simgesi olan satırlar menü sayımına girmez.
- BÜYÜK HARF / Her Kelimesi Büyük yazılar (RDR2) çeviriye cümle düzeninde gönderiliyor ("Bolt Action" → "Ilt Eylem"
  gibi saçma çeviriler biter); sonuç Türkçe kurallarıyla geri büyütülür (I → İ doğru).

## v1.7 – Çevirili ekran görüntüsü, ortalı altyazı

- **F9 = çevirili ekran görüntüsü**: oyunun karesi + Türkçe altyazı (ve oyun içi yazılar), TV'de görünen şekliyle
  (aynı yazı tipi, boy, renk, siyah kutu). "Kare kaydet" düğmesi de çevirili hâlini ayrıca kaydeder
  (captures klasörü, ceviri-*.png). Windows'un ekran görüntüsü TV'deki yazıyı göremediği için.
- TV: Türkçe yazı siyah kutunun içinde dikeyde ortalanır (en üste yapışmaz). TV uygulaması 1.0.9.
- Yazı tipi: aynı oyunda yanlışlıkla "oyun değişti" sanılması düzeltildi (kısa satırlar artık sıfırlatmaz;
  en az iki ölçü birlikte değişmeli, en fazla 3 dakikada bir).

## v1.6 – Yazı tipi tamamen otomatik

- Oyun adı yazma ve yazı tipi seçme kaldırıldı: program fontu, boyu ve rengi her zaman ekrandaki altyazıdan bulur.
- Oyun değişimi kendiliğinden algılanır: son altyazıların yazısı (boy, kalınlık, genişlik, tırnak) öncekilerden belirgin
  farklıysa eski ölçümler silinir ve yeni oyunun fontu birkaç altyazıda öğrenilir.
- Kayıtlı font yeni yazıya hiç benzemiyorsa beklemeden en yakın fonta geçilir.
- Altyazı Görünümü'nde yalnızca "Baştan öğren" düğmesi var.

## v1.5 – Oyunlara en yakın ücretsiz fontlar

- 18 yeni açık lisanslı yazı tipi dosyası (Türkçe karakterli): Arimo, Inter, Libre Franklin, EB Garamond, Crimson Pro,
  Libre Baskerville, Cormorant Garamond. Katalog artık 49 font.
- Tırnaklı (serif) fontlar da otomatik eşleşmeye katıldı: yeni "çizgi kontrastı" ölçümü (ince yatay / kalın dikey
  çizgi) serif ve sans yazıyı ayırt eder. RDR2 gibi oyunlarda en yakın font ölçümle bulunur.
- Hazır oyun eşleşmeleri: GTA V → Arimo (Chalet 1960 yerine), The Last of Us → Barlow (FF DIN yerine),
  Elden Ring → EB Garamond (Agmena yerine). Kalınlık ve boy yine ekrandan ölçülür.
- Tüm fontların ölçüleri oyundaki ölçümle aynı yöntemle yeniden hesaplandı (boy daha isabetli).

## v1.4 – Altyazı her zaman orijinal boyunda

- Yazı artık hiç küçültülmüyor: uzun çeviriler orijinal boyda kalır ve **aşağıya doğru satır satır** iner
  (satır sınırı yok). Ekranın altından taşarsa kutu yalnızca gerektiği kadar yukarı kayar. PC ve TV.
- "Orijinalle aynı boy" artık örtme modu kapalıyken de oyunun ölçülen yazı boyunu kullanır.
- Yazı boyu kararlı: ölçüm büyük/küçük harf satırları arasında gidip gelmiyor; yeterli ölçümden sonra
  %7'den küçük oynamalarda boy sabit kalır (bir altyazı büyük, öteki küçük olmuyor).
- TV uygulaması 1.0.8 (program açılışta LG TV'yi kendiliğinden günceller).

## v1.3 – Google Gemini çeviri servisi

- Yeni isteğe bağlı çeviri servisi: **Google Gemini** (Google AI Studio anahtarı, kart gerekmez). Sıra:
  DeepL → Gemini → Azure → MyMemory. Varsayılan model en hızlı Flash-Lite; model adı değişirse yedek modeller denenir.
- Gemini'ye önceki altyazılar bağlam olarak verilir (isimler ve hitap tutarlı kalır); yalnızca çeviri döndürmesi istenir.
- Ücretsiz katman sınırı dolunca (429) 2 dakika sonra yeniden denenir, bu arada sıradaki servis kullanılır.

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
