using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ps5Altyazi.Ps5;
using Ps5Altyazi.Settings;
using Ps5Altyazi.Tv;

namespace Ps5Altyazi;

/// <summary>
/// İlk açılış kurulum sihirbazı: Hoş geldin → PS5 bağlantısı (PSN girişi + eşleme) → Çeviri → TV → Bitti.
/// Her adım sonradan ana penceredeki düğmelerden yeniden açılabilir.
/// </summary>
public sealed class SetupWizardWindow : Window
{
    private readonly AppSettings _s;
    private readonly Action _save;
    private readonly Func<string> _translationSummary;
    private readonly Action _openTranslation;
    private readonly Func<SubtitleServer> _ensureServer;

    private readonly List<(string Title, Func<UIElement> Build)> _pages = new();
    private readonly Dictionary<int, UIElement> _built = new();
    private int _index;

    private readonly StackPanel _stepsBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock _title = new() { FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) };
    private readonly ContentControl _host = new();
    private readonly Button _back = new() { Content = "← Geri", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _next = new() { Content = "İleri →", Padding = new Thickness(22, 6, 22, 6), FontWeight = FontWeights.Bold };
    private readonly TextBlock _hint = new() { Foreground = Theme.Dim, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };

    public SetupWizardWindow(AppSettings settings, Action save, Func<SubtitleServer> ensureServer,
                             Action openTranslation, Func<string> translationSummary, Action sendTest)
    {
        _s = settings;
        _save = save;
        _ensureServer = ensureServer;
        _openTranslation = openTranslation;
        _translationSummary = translationSummary;

        Title = "Altyazı+ – Kurulum";
        Width = 820;
        Height = Math.Min(860, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _pages.Add(("Hoş geldin", BuildWelcome));
        _pages.Add(("PS5 bağlantısı", () => Wrap(new Ps5SetupPanel(_s, _save))));
        _pages.Add(("Çeviri", BuildTranslation));
        _pages.Add(("TV", () => Wrap(new TvSetupPanel(_s, _ensureServer, _save, sendTest))));
        _pages.Add(("Bitti", BuildDone));

        var root = new DockPanel { Margin = new Thickness(20, 14, 20, 16) };
        var top = new StackPanel();
        top.Children.Add(_stepsBar);
        top.Children.Add(_title);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_back);
        buttons.Children.Add(_next);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(_hint);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(_host);
        Content = root;
        Theme.Apply(this, canResize: true);

        _back.Click += (_, _) => Go(_index - 1);
        _next.Click += (_, _) =>
        {
            if (_index == _pages.Count - 1) { _s.SetupCompleted = true; _save(); DialogResult = true; return; }
            Go(_index + 1);
        };
        Go(0);
    }

    private static UIElement Wrap(UIElement e) =>
        new ScrollViewer { Content = e, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) };

    private void Go(int i)
    {
        _index = Math.Clamp(i, 0, _pages.Count - 1);
        if (!_built.TryGetValue(_index, out var page) || _index == _pages.Count - 1 || _index == 2)
            _built[_index] = page = _pages[_index].Build();
        _host.Content = page;
        _title.Text = $"{_index + 1}. {_pages[_index].Title}";
        _back.IsEnabled = _index > 0;
        _next.Content = _index == _pages.Count - 1 ? "Ana sayfaya geç ✔" : _index == 0 ? "Başlayalım →" : "İleri →";
        _hint.Text = _index switch
        {
            1 => Ps5Store.IsReady(_s) ? "✔ PS5 hazır." : "PS5'i şimdi kurmak istemezseniz 'İleri' ile geçebilirsiniz (sonra '🎮 PS5 Bağlantısı').",
            3 => "TV kullanmayacaksanız 'İleri' ile geçin; Türkçe altyazı PC ekranında da görünür.",
            _ => "",
        };
        DrawSteps();
    }

    private void DrawSteps()
    {
        _stepsBar.Children.Clear();
        for (int i = 0; i < _pages.Count; i++)
        {
            bool done = i < _index, cur = i == _index;
            var b = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0),
                Background = cur ? Theme.Accent : done ? Theme.Good : Theme.Line,
                Child = new TextBlock
                {
                    Text = (done ? "✔ " : $"{i + 1}. ") + _pages[i].Title, FontSize = 12,
                    Foreground = cur || done ? Brushes.Black : Theme.Dim, FontWeight = cur ? FontWeights.Bold : FontWeights.Normal
                }
            };
            _stepsBar.Children.Add(b);
        }
    }

    // ───────── Sayfalar ─────────

    private UIElement BuildWelcome()
    {
        var p = new StackPanel();
        p.Children.Add(Para("Altyazı+'a hoş geldin! Birkaç adımda her şeyi hazırlayalım; sonra ana sayfada ▶ Başlat'a basman yeterli olacak.", 15));
        p.Children.Add(Head("Nasıl çalışır?"));
        p.Children.Add(Para("• PS5 HDMI kablosuyla TV'ye bağlı kalır; sen oyunu her zamanki gibi TV'de, kendi kolunla oynarsın.\n" +
                            "• Bu bilgisayar PS5'e ağ üzerinden, arka planda bağlanır (PS Remote Play uygulaması gerekmez, ekranda pencere açılmaz).\n" +
                            "• Oyundaki İngilizce altyazıyı okur, Türkçeye çevirir ve TV'deki Altyazı+ uygulamasına (ya da PC ekranına) gönderir."));
        p.Children.Add(Head("Hazırlaman gerekenler"));
        p.Children.Add(Para("• Ücretsiz İKİNCİ bir PSN hesabı, PS5'e kullanıcı olarak eklenmiş (kendi hesabınla bağlanılırsa kolun oyundan kopar).\n" +
                            "• PS5, bilgisayar ve TV aynı ev ağında.\n" +
                            "• (İsteğe bağlı) Ücretsiz DeepL ya da Google Gemini anahtarı – daha iyi çeviri için.\n" +
                            "• (İsteğe bağlı) LG / Samsung / Android TV – altyazıyı TV'de görmek için."));
        return Wrap(p);
    }

    private UIElement BuildTranslation()
    {
        var p = new StackPanel();
        p.Children.Add(Para("Çeviri servisleri sırayla denenir: DeepL → Google Gemini → Azure → MyMemory. Anahtar girmezsen ücretsiz MyMemory " +
                            "kullanılır (günlük sınırı düşüktür). En iyi sonuç için ücretsiz bir DeepL ya da Gemini anahtarı gir."));
        var state = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10), FontWeight = FontWeights.Bold };
        void Refresh()
        {
            string sum = _translationSummary();
            bool good = !sum.StartsWith("Yalnız MyMemory", StringComparison.Ordinal);
            state.Text = (good ? "● " : "● ") + sum;
            state.Foreground = good ? Theme.Good : Theme.Warn;
        }
        Refresh();
        p.Children.Add(state);
        var btn = new Button { Content = "🌐 Çeviri anahtarlarını gir", Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left };
        btn.Click += (_, _) => { _openTranslation(); Refresh(); };
        p.Children.Add(btn);
        p.Children.Add(Para("• DeepL API Free: https://www.deepl.com/pro-api (aylık 500.000 karakter ücretsiz)\n" +
                            "• Google Gemini: https://aistudio.google.com/apikey (kart gerekmez)\n" +
                            "Anahtarlar bu bilgisayarda şifreli saklanır."));
        return Wrap(p);
    }

    private UIElement BuildDone()
    {
        var p = new StackPanel();
        p.Children.Add(Para("Kurulum tamam! Durum:", 15));
        bool ps5 = Ps5Store.IsReady(_s);
        p.Children.Add(Status(ps5, ps5 ? $"PS5 eşlendi – {Ps5Store.Pairing(_s)?.Name}" : "PS5 kurulmadı (ana sayfada '🎮 PS5 Bağlantısı')"));
        string tr = _translationSummary();
        p.Children.Add(Status(!tr.StartsWith("Yalnız MyMemory", StringComparison.Ordinal), "Çeviri: " + tr, warnInsteadOfBad: true));
        int tv = _ensureServer().ClientCount;
        bool tvSet = _s.TvPlatform == "webos" ? _s.TvKeyProtected != null : tv > 0;
        p.Children.Add(Status(tv > 0, tv > 0 ? $"TV bağlı ({tv} ekran)" : tvSet ? "TV kurulu (çeviri başlayınca açılır)" : "TV kurulmadı – altyazı PC ekranında görünecek",
                              warnInsteadOfBad: true));
        p.Children.Add(Head("Şimdi ne yapacağım?"));
        p.Children.Add(Para("1) Oyunu aç ve oyunun ayarlarından İngilizce altyazıyı aç.\n" +
                            "2) Ana sayfada ▶ Başlat'a (ya da F6'ya) bas: program PS5'e bağlanır, durum yeşile döner.\n" +
                            "3) Biri konuşurken '✦ Otomatik Bul' ile altyazının yerini bir kez tanıt.\n" +
                            "Bu kurulumu ana sayfadaki '🧭 Kurulum' düğmesinden istediğin zaman yeniden açabilirsin."));
        return Wrap(p);
    }

    // ───────── Yardımcılar ─────────

    private static UIElement Status(bool ok, string text, bool warnInsteadOfBad = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(new TextBlock { Text = "●", FontSize = 18, Margin = new Thickness(0, 0, 8, 0), Foreground = ok ? Theme.Good : warnInsteadOfBad ? Theme.Warn : Theme.Bad, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        return row;
    }

    private static TextBlock Head(string t) => new() { Text = t, FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 16, 0, 4) };
    private static TextBlock Para(string t, double size = 13) => new() { Text = t, TextWrapping = TextWrapping.Wrap, FontSize = size, Margin = new Thickness(0, 4, 0, 0), LineHeight = size * 1.45 };
}
