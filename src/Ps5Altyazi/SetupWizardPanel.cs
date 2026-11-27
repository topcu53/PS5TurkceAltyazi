using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ps5Altyazi.Ps5;
using Ps5Altyazi.Settings;
using Ps5Altyazi.Tv;

namespace Ps5Altyazi;

/// <summary>
/// İlk açılış kurulumu – ana pencerenin içinde, solda adım çubuğu ve ilerleme yüzdesi ile gösterilir:
/// Hoş geldin → PS5 bağlantısı (PSN girişi + eşleme) → Çeviri → TV → Bitti. Tamamlanınca <see cref="Finished"/> tetiklenir.
/// </summary>
public sealed class SetupWizardPanel : UserControl
{
    private readonly AppSettings _s;
    private readonly Action _save;
    private readonly Func<string> _translationSummary;
    private readonly Func<Task> _openTranslation;
    private readonly Func<SubtitleServer> _ensureServer;

    private readonly List<(string Title, string Sub, Func<UIElement> Build)> _pages = new();
    private readonly Dictionary<int, UIElement> _built = new();
    private int _index;

    private readonly StackPanel _steps = new();
    private readonly ProgressBar _progress = new() { Height = 6, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _percent = new() { FontSize = 12 };
    private readonly TextBlock _title = new() { FontSize = 24, FontWeight = FontWeights.Bold };
    private readonly TextBlock _subtitle = new() { FontSize = 13, Margin = new Thickness(0, 4, 0, 14), TextWrapping = TextWrapping.Wrap };
    private readonly ContentControl _host = new();
    private readonly Button _back = new() { Content = "← Geri", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 10, 0) };
    private readonly Button _next = new() { Content = "İleri →", Padding = new Thickness(26, 8, 26, 8), FontWeight = FontWeights.Bold };
    private readonly Button _skip = new() { Content = "Şimdilik geç", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock _hint = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };

    /// <summary>true: kurulum tamamlandı · false: kullanıcı "Şimdilik geç" dedi.</summary>
    public event Action<bool>? Finished;

    public SetupWizardPanel(AppSettings settings, Action save, Func<SubtitleServer> ensureServer,
                            Func<Task> openTranslation, Func<string> translationSummary, Action sendTest)
    {
        _s = settings;
        _save = save;
        _ensureServer = ensureServer;
        _openTranslation = openTranslation;
        _translationSummary = translationSummary;

        _pages.Add(("Hoş geldin", "Nasıl çalıştığı ve hazırlıklar", BuildWelcome));
        _pages.Add(("PS5 bağlantısı", "PSN girişi ve PS5 eşleme", () => Wrap(new Ps5SetupPanel(_s, _save))));
        _pages.Add(("Çeviri", "Çeviri servisi ve anahtarlar", BuildTranslation));
        _pages.Add(("TV", "TV uygulaması ve test altyazısı", () => Wrap(new TvSetupPanel(_s, _ensureServer, _save, sendTest))));
        _pages.Add(("Bitti", "Özet ve ilk kullanım", BuildDone));

        Brush card = TryFindBrush("Card") ?? Theme.Bar;
        _percent.Foreground = Theme.Dim;
        _subtitle.Foreground = Theme.Dim;
        _hint.Foreground = Theme.Dim;
        _title.Foreground = Theme.Text;
        _progress.Foreground = Theme.Accent;
        _progress.Background = Theme.Line;
        _progress.BorderThickness = new Thickness(0);
        if (TryFindResource("Primary") is Style primary) _next.Style = primary;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Sol: adımlar + ilerleme
        var left = new DockPanel();
        var leftTop = new StackPanel();
        leftTop.Children.Add(new TextBlock { Text = "İLK KURULUM", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Theme.Accent, Margin = new Thickness(0, 0, 0, 4) });
        leftTop.Children.Add(new TextBlock { Text = "Her şeyi birlikte hazırlayalım", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 22) });
        leftTop.Children.Add(_steps);
        DockPanel.SetDock(leftTop, Dock.Top);
        left.Children.Add(leftTop);
        var leftBottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        leftBottom.Children.Add(_percent);
        leftBottom.Children.Add(_progress);
        left.Children.Add(leftBottom);
        var leftCard = new Border
        {
            Background = card, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18),
            Padding = new Thickness(22, 22, 22, 20), Margin = new Thickness(0, 0, 18, 0), Child = left
        };
        grid.Children.Add(leftCard);

        // Sağ: sayfa içeriği
        var right = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(_title);
        top.Children.Add(_subtitle);
        DockPanel.SetDock(top, Dock.Top);
        right.Children.Add(top);
        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_skip);
        buttons.Children.Add(_back);
        buttons.Children.Add(_next);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(_hint);
        DockPanel.SetDock(bottom, Dock.Bottom);
        right.Children.Add(bottom);
        right.Children.Add(_host);
        var rightCard = new Border
        {
            Background = card, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18),
            Padding = new Thickness(28, 24, 28, 20), Child = right
        };
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);
        Content = grid;

        _back.Click += (_, _) => Go(_index - 1);
        _skip.Click += (_, _) => Finished?.Invoke(false);
        _next.Click += (_, _) =>
        {
            if (_index == _pages.Count - 1) { _s.SetupCompleted = true; _save(); Finished?.Invoke(true); return; }
            Go(_index + 1);
        };
        Go(0);
    }

    private Brush? TryFindBrush(string key) => TryFindResource(key) as Brush ?? Application.Current?.TryFindResource(key) as Brush;

    private static UIElement Wrap(UIElement e) =>
        new ScrollViewer { Content = e, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) };

    private void Go(int i)
    {
        _index = Math.Clamp(i, 0, _pages.Count - 1);
        if (!_built.TryGetValue(_index, out var page) || _index == _pages.Count - 1 || _index == 2)
            _built[_index] = page = _pages[_index].Build();
        _host.Content = page;
        _title.Text = _pages[_index].Title;
        _subtitle.Text = $"Adım {_index + 1} / {_pages.Count} · {_pages[_index].Sub}";
        _back.Visibility = _index > 0 ? Visibility.Visible : Visibility.Collapsed;
        _skip.Visibility = _index < _pages.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
        _next.Content = _index == _pages.Count - 1 ? "Ana sayfaya geç ✔" : _index == 0 ? "Başlayalım →" : "İleri →";
        _hint.Text = _index switch
        {
            1 => Ps5Store.IsReady(_s) ? "✔ PS5 hazır." : "PS5'i şimdi kurmak istemezsen 'İleri' ile geçebilirsin.",
            3 => "TV kullanmayacaksan 'İleri' ile geç; Türkçe altyazı PC ekranında da gösterilebilir.",
            _ => "",
        };
        int pct = (int)Math.Round(100.0 * _index / (_pages.Count - 1));
        _progress.Value = pct;
        _percent.Text = $"İlerleme  %{pct}";
        DrawSteps();
    }

    private void DrawSteps()
    {
        _steps.Children.Clear();
        for (int i = 0; i < _pages.Count; i++)
        {
            bool done = i < _index, cur = i == _index;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(i == _pages.Count - 1 ? 0 : 22) });

            var circle = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Left,
                Background = done ? Theme.Good : cur ? Theme.Accent : Brushes.Transparent,
                BorderBrush = done ? Theme.Good : cur ? Theme.Accent : Theme.Line, BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = done ? "✔" : (i + 1).ToString(), FontSize = 12, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = done || cur ? Brushes.Black : Theme.Dim
                }
            };
            row.Children.Add(circle);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            texts.Children.Add(new TextBlock { Text = _pages[i].Title, FontSize = 14, FontWeight = cur ? FontWeights.Bold : FontWeights.Normal, Foreground = cur || done ? Theme.Text : Theme.Dim });
            texts.Children.Add(new TextBlock { Text = done ? "Tamamlandı" : cur ? "Şu an buradasın" : _pages[i].Sub, FontSize = 11, Foreground = done ? Theme.Good : Theme.Dim, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);

            if (i < _pages.Count - 1)
            {
                var line = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(13, 2, 0, 2), Background = done ? Theme.Good : Theme.Line };
                Grid.SetRow(line, 1);
                row.Children.Add(line);
            }
            int target = i;
            if (done) { row.Cursor = System.Windows.Input.Cursors.Hand; row.Background = Brushes.Transparent; row.MouseLeftButtonUp += (_, _) => Go(target); }
            _steps.Children.Add(row);
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
        btn.Click += async (_, _) => { await _openTranslation(); Refresh(); };
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
        p.Children.Add(Status(ps5, ps5 ? $"PS5 eşlendi – {Ps5Store.Pairing(_s)?.Name}" : "PS5 kurulmadı (Ayarlar → Bağlantı)"));
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
                            "Bu kurulumu Ayarlar → Bağlantı → '🧭 Kurulum Adımlarını Aç' ile istediğin zaman yeniden açabilirsin."));
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
