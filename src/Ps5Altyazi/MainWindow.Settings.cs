using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using Ps5Altyazi.Logging;
using Ps5Altyazi.Ps5;

namespace Ps5Altyazi;

/// <summary>Ayarlar ve Tema pencereleri.</summary>
public partial class MainWindow
{
    private async void GeneralSettings_Click(object sender, RoutedEventArgs e)
    {
        bool direct0 = _settings.DirectConnect, tv0 = _settings.TvServerEnabled, sub0 = _settings.SubtitleWindowEnabled;
        var w = new GeneralSettingsWindow(_settings, Ps5Store.IsReady(_settings), async () =>
        {
            await CheckUpdateAsync();
            if (_update == null) { Notify($"Altyazı+ güncel (v{Updater.Current})"); Log.Info($"Altyazı+ güncel (v{Updater.Current})."); }
        });
        await Pages.ShowAsync(w);

        int fi = Array.IndexOf(new[] { 5, 10, 15, 30, 60 }, _settings.Fps);
        if (fi >= 0 && FpsCombo.SelectedIndex != fi) FpsCombo.SelectedIndex = fi;
        if (tv0 != _settings.TvServerEnabled) TvServerCheck.IsChecked = _settings.TvServerEnabled;
        if (sub0 != _settings.SubtitleWindowEnabled) SubtitleWindowCheck.IsChecked = _settings.SubtitleWindowEnabled;
        _subWin?.SetMuted(!PcSubtitleOn);
        SaveSettings();
        if (direct0 != _settings.DirectConnect)
        {
            bool running = _capture != null;
            if (running) StopCapture("Görüntü kaynağı değişti.");
            UpdateSourceUi();
            if (!_settings.DirectConnect) RefreshWindowList();
            if (running) StartCapture();
        }
        Log.Info("Ayarlar kaydedildi.");
        if (w.RequestSetup) RunSetupWizard();
    }

    private async void ThemeSettings_Click(object sender, RoutedEventArgs e)
    {
        var w = new ThemeWindow(_settings.ThemeName);
        if (await Pages.ShowAsync(w) != true || w.Chosen == null || w.Chosen == _settings.ThemeName) return;
        _settings.ThemeName = w.Chosen;
        SaveSettings();
        Log.Info($"Tema: {ThemePalettes.Get(w.Chosen).Name}. Program yeniden açılıyor…");
        try
        {
            string? exe = Environment.ProcessPath;
            // Eski pencere kapanıp bağlantı noktaları boşalınca açılsın (2 sn sonra).
            if (exe != null)
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & start \"\" \"{exe}\"")
                { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex) { Log.Warn("Program yeniden açılamadı: " + ex.Message); return; }
        Application.Current.Shutdown();
    }
}
