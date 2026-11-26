using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ps5Altyazi.Logging;
using Ps5Altyazi.Ps5;
using Ps5Altyazi.Settings;
using Ps5Altyazi.Vision;

namespace Ps5Altyazi;

/// <summary>Oyun seçimi: oyuna özel altyazı alanı ve yazı tipi; PS5'te açık oyunu kendiliğinden algılama.</summary>
public partial class MainWindow
{
    private string? _ps5RunningGame;
    private readonly DispatcherTimer _gameTimer = new() { Interval = TimeSpan.FromSeconds(45) };
    private bool _gameChecking;

    private void InitGamePicker()
    {
        UpdateGameTile();
        _gameTimer.Tick += async (_, _) => await DetectRunningGameAsync();
        _gameTimer.Start();
    }

    private void UpdateGameTile()
    {
        bool none = string.IsNullOrWhiteSpace(_settings.ActiveGame) || _settings.ActiveGame == "Genel";
        GameTileText.Text = none ? "Seçilmedi (Genel)" : _settings.ActiveGame;
        GameTileLabel.Text = _ps5RunningGame != null && _settings.AutoGame ? "Oyun • PS5'ten algılandı" : "Oyun";
        GameTileText.ToolTip = GameTileText.Text;
    }

    private async void PickGame_Click(object sender, RoutedEventArgs e)
    {
        string? running = _ps5RunningGame;
        if (running == null && DirectRunning) running = await ReadRunningGameAsync();
        var w = new GamePickerWindow(_settings, running) { Owner = this };
        bool ok = w.ShowDialog() == true;
        SaveSettings();
        if (ok && w.Chosen != null) SwitchActiveGame(w.Chosen, "elle seçildi");
        UpdateGameTile();
    }

    /// <summary>Etkin oyunu değiştirir: eski oyunun alanını saklar, yenisinin alanını ve yazı tipini geri yükler.</summary>
    private void SwitchActiveGame(string name, string why)
    {
        name = name.Trim();
        if (name.Length == 0 || string.Equals(name, _settings.ActiveGame, StringComparison.Ordinal)) { UpdateGameTile(); return; }
        // Eski oyunun alanını kendi profiline yaz
        ActiveFontProfile.Region = _settings.SubtitleRegion;
        string old = _settings.ActiveGame;
        _settings.ActiveGame = name;
        var p = ActiveFontProfile; // yoksa oluşur
        p.LastPlayed = DateTime.Now;
        if (p.Region is { Length: 4 } r)
        {
            var rect = new NormRect(r[0], r[1], r[2], r[3]);
            if (rect.IsValid) ApplyRegion(rect, "Oyun profili");
        }
        else if (_settings.SubtitleRegion != null)
        {
            p.Region = _settings.SubtitleRegion; // ilk kez: şimdiki alanla başlar
            Log.Info($"\"{name}\" için kayıtlı altyazı alanı yok; şimdiki alan kullanılıyor. Tutmazsa '✦ Altyazıyı Bul'.");
        }
        OnFontSettingChanged(true);
        SaveSettings();
        UpdateGameTile();
        Log.Success($"Oyun: \"{old}\" → \"{name}\" ({why}). Altyazı alanı ve yazı tipi bu oyuna göre ayarlandı.");
        Notify($"Oyun: {name}");
    }

    /// <summary>PS5'te açık oyunun adı (doğrudan bağlantı kuruluysa); bulunamazsa null.</summary>
    private async Task<string?> ReadRunningGameAsync()
    {
        var pairing = Ps5Store.Pairing(_settings);
        if (pairing == null || !Ps5Helper.Available) return null;
        try
        {
            var found = await Ps5Helper.DiscoverAsync(new[] { pairing.Host }, 1200);
            string? app = found.FirstOrDefault()?.App;
            return string.IsNullOrWhiteSpace(app) ? null : app.Trim();
        }
        catch { return null; }
    }

    private async Task DetectRunningGameAsync()
    {
        if (_gameChecking || !DirectRunning) return;
        _gameChecking = true;
        try
        {
            string? app = await ReadRunningGameAsync();
            if (app != _ps5RunningGame)
            {
                _ps5RunningGame = app;
                if (app != null) Log.Info($"PS5'te açık oyun: {app}");
            }
            if (app != null && _settings.AutoGame) SwitchActiveGame(app, "PS5'te açık");
            UpdateGameTile();
        }
        finally { _gameChecking = false; }
    }
}
