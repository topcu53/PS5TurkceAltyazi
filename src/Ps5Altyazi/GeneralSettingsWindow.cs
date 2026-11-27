using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Ps5Altyazi.Settings;

namespace Ps5Altyazi;

/// <summary>Genel ayarlar: başlangıç, bağlantı, okuma hızı, TV, PC altyazısı, güncelleme.</summary>
public sealed class GeneralSettingsWindow : PageWindow
{
    private static readonly int[] FpsValues = { 5, 10, 15, 30, 60 };

    /// <summary>Kullanıcı "Kurulumu yeniden aç"a bastı (pencere kapanınca sihirbaz açılır).</summary>
    public bool RequestSetup { get; private set; }

    public GeneralSettingsWindow(AppSettings s, bool ps5Ready, Action checkUpdate)
    {
        Title = "Ayarlar";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(20, 14, 20, 16) };

        root.Children.Add(Head("Başlangıç ve bağlantı"));
        root.Children.Add(Check("Program açılınca çeviriyi kendiliğinden başlat", s.AutoStart, v => s.AutoStart = v));
        var direct = Check("PS5'e doğrudan bağlan (PS Remote Play uygulaması gerekmez)", s.DirectConnect && ps5Ready, v => s.DirectConnect = v);
        direct.IsEnabled = ps5Ready;
        root.Children.Add(direct);
        if (!ps5Ready) root.Children.Add(Note("PS5 bağlantısı henüz kurulmadı: '🧭 Kurulum' ya da '🎮 PS5 Bağlantısı'."));

        root.Children.Add(Head("Okuma"));
        var fpsRow = new StackPanel { Orientation = Orientation.Horizontal };
        fpsRow.Children.Add(new TextBlock { Text = "Saniyede okunan kare:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var fps = new ComboBox { Width = 80 };
        foreach (var f in FpsValues) fps.Items.Add(f.ToString());
        fps.SelectedIndex = Math.Max(0, Array.IndexOf(FpsValues, s.Fps));
        fps.SelectionChanged += (_, _) => { if (fps.SelectedIndex >= 0) s.Fps = FpsValues[fps.SelectedIndex]; };
        fpsRow.Children.Add(fps);
        root.Children.Add(fpsRow);
        root.Children.Add(Note("PS5'e doğrudan bağlantıda en çok 15 kullanılır. Düşük değer işlemciyi daha az yorar, altyazı biraz geç gelir."));

        root.Children.Add(Head("TV"));
        root.Children.Add(Check("TV / telefon yayını açık", s.TvServerEnabled, v => s.TvServerEnabled = v));
        root.Children.Add(Check("Çeviri başlayınca TV uygulamasını da aç (LG)", s.TvAutoLaunch, v => s.TvAutoLaunch = v));
        root.Children.Add(Note("Çevirinin TV'deki yeri ve renkleri: '✎ Altyazı Görünümü'."));

        root.Children.Add(Head("Bu bilgisayar"));
        root.Children.Add(Check("Türkçe altyazıyı bu bilgisayarın ekranında da göster (oyun görüntüsünün üstünde)", s.PcSubtitleVisible, v => s.PcSubtitleVisible = v));
        root.Children.Add(Note("Kapalıyken altyazı yalnız TV'de / telefonda görünür; ana penceredeki 'Canlı çeviri' kartı yine gösterir."));

        root.Children.Add(Head("Diğer"));
        var row = new WrapPanel();
        row.Children.Add(Btn("⟳ Güncellemeleri denetle", checkUpdate));
        row.Children.Add(Btn("🧭 Kurulumu yeniden aç", () => { RequestSetup = true; Finish(); }));
        row.Children.Add(Btn("📁 Günlük klasörü", () =>
        {
            string dir = System.IO.Path.GetDirectoryName(Logging.Log.CurrentFile) ?? "";
            try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch { }
        }));
        root.Children.Add(row);
        root.Children.Add(Note($"Altyazı+ v{Updater.Current}"));

        var ok = new Button { Content = "Tamam", Padding = new Thickness(22, 6, 22, 6), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => Finish();
        root.Children.Add(ok);
        Content = root;
        Theme.Apply(this);
    }

    private static CheckBox Check(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
        c.Checked += (_, _) => set(true);
        c.Unchecked += (_, _) => set(false);
        return c;
    }

    private static Button Btn(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 4, 8, 4) };
        b.Click += (_, _) => click();
        return b;
    }

    private static TextBlock Head(string t) => new() { Text = t, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 12, 0, 4) };
    private static TextBlock Note(string t) => new() { Text = t, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Dim, Margin = new Thickness(0, 2, 0, 0), FontSize = 12 };
}
