using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ps5Altyazi;

/// <summary>Üst sekmeler (Ana Ekran / Geçmiş / Ayarlar), ayarlar menüsü ve çeviri geçmişi.</summary>
public partial class MainWindow
{
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (HomePage == null || HistoryPage == null || SettingsPage == null) return; // XAML yüklenirken
        HomePage.Visibility = NavHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = NavHistory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (NavSettings.IsChecked == true) RefreshSettingsTexts();
        if (NavHistory.IsChecked == true && LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
    }

    private void SettingsNav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageAdvanced == null) return; // XAML yüklenirken
        var map = new (RadioButton? Item, FrameworkElement Page)[]
        {
            (SetConn, PageConn), (SetSub, PageSub), (SetTr, PageTr), (SetTv, PageTv),
            (SetTheme, PageTheme), (SetGeneral, PageGeneral), (SetAdvanced, PageAdvanced),
        };
        foreach (var (item, page) in map)
            page.Visibility = item?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RefreshSettingsTexts();
    }

    private bool _modeLoading;

    private void TrMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_modeLoading || !_settingsReady || ModeFast == null) return;
        string mode = ModeFast.IsChecked == true ? "fast" : ModeNatural.IsChecked == true ? "natural" : ModeOffline.IsChecked == true ? "pc" : "balanced";
        if (mode == _settings.TranslationMode) return;
        _settings.TranslationMode = mode;
        SaveSettings();
        ApplyTranslators(checkUsage: false);
        RefreshSettingsTexts();
    }

    private void RefreshSettingsTexts()
    {
        if (TranslationSummaryText == null || !_settingsReady) return;
        _modeLoading = true;
        (_settings.TranslationMode switch { "fast" => ModeFast, "natural" => ModeNatural, "pc" => ModeOffline, _ => ModeBalanced }).IsChecked = true;
        _modeLoading = false;
        bool both = _deepLKey != null && _geminiKey != null;
        TrModeHint.Text = _settings.TranslationMode switch
        {
            "fast" => "DeepL önce sorulur (genelde 200–400 ms). Geç kalırsa Gemini devreye girer.",
            "natural" => "Gemini önceki satırları da okuyarak en doğal Türkçeyi yazar; biraz daha yavaş olabilir. Geç kalırsa DeepL devreye girer.",
            "pc" => (Translation.OfflineTranslator.IsInstalled ? "✔ Model hazır. " : _offlineDownloading ? "Model indiriliyor… " : "Model henüz yok, otomatik indirilecek. ") +
                    "Çeviri bu bilgisayarın işlemcisinde yapılır: internet, kota, ekran kartı gerekmez, hiç durmaz (cümle başına ~0,2–0,5 sn). " +
                    "Kalitesi DeepL'den biraz düşük; 1,5 sn'de bitmezse internet servisi de devreye girer.",
            _ => "DeepL ve Gemini aynı anda sorulur, hangisi önce gelirse o gösterilir: en düşük gecikme. (Gemini'nin ücretsiz kotası daha çabuk dolabilir.)",
        } + (both || _settings.TranslationMode == "pc" ? "" : "  Not: Bu modların tam çalışması için hem DeepL hem Gemini anahtarı önerilir. İnternet servisleri dursa da bilgisayardaki model çeviriye devam eder.");
        TranslationSummaryText.Text = TranslationSummary();
        ThemeNameText.Text = "Şu anki tema: " + ThemePalettes.Get(_settings.ThemeName).Name;
    }

    /// <summary>Çeviri geçmişine (en yeni üstte) bir satır ekler.</summary>
    private void AddHistory(string english, string turkish, string engine)
    {
        if (HistoryList == null || string.IsNullOrWhiteSpace(turkish)) return;
        var panel = new StackPanel { Margin = new Thickness(2, 6, 2, 6) };
        panel.Children.Add(new TextBlock { Text = turkish, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = english, FontSize = 12, Foreground = Theme.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        panel.Children.Add(new TextBlock { Text = $"{DateTime.Now:HH:mm:ss} • {engine}", FontSize = 11, Foreground = Theme.Dim, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0) });
        HistoryList.Items.Insert(0, new ListBoxItem { Content = panel, Focusable = false });
        while (HistoryList.Items.Count > 300) HistoryList.Items.RemoveAt(HistoryList.Items.Count - 1);
        HistoryEmpty.Visibility = Visibility.Collapsed;
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        HistoryList.Items.Clear();
        HistoryEmpty.Visibility = Visibility.Visible;
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string text = string.Join(Environment.NewLine, LogList.Items.OfType<ListBoxItem>().Select(i => i.Content?.ToString()));
            Clipboard.SetText(text);
            Notify("Günlük panoya kopyalandı");
        }
        catch (Exception ex) { Logging.Log.Warn("Günlük kopyalanamadı: " + ex.Message); }
    }
}
