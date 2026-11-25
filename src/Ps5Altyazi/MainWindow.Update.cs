using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ps5Altyazi.Logging;

namespace Ps5Altyazi;

/// <summary>Güncelleme: yeni sürüm çıkınca üst bantta "Şimdi güncelle" düğmesi görünür; basınca kendiliğinden kurulur.</summary>
public partial class MainWindow
{
    private Updater.Release? _update;
    private bool _updating;
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };

    private void InitUpdater()
    {
        _updateTimer.Tick += async (_, _) => await CheckUpdateAsync();
        _updateTimer.Start();
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(4000);
            await CheckUpdateAsync();
        });
    }

    private async Task CheckUpdateAsync()
    {
        if (_updating) return;
        try
        {
            var r = await Updater.CheckAsync();
            if (r == null) return;
            bool isNew = _update == null || r.Version != _update.Version;
            _update = r;
            UpdateButton.Content = $"⬇ Şimdi güncelle (v{r.Version})";
            UpdateButton.ToolTip = $"Yeni sürüm: v{r.Version} (kurulu: v{Updater.Current}). Tıklayınca indirilip kurulur, program yeniden açılır; ayarlarınız korunur.";
            UpdateButton.Visibility = Visibility.Visible;
            if (isNew)
            {
                Log.Success($"Yeni sürüm var: v{r.Version} (kurulu: v{Updater.Current}). Üstteki 'Şimdi güncelle' düğmesine basın.");
                Notify($"Yeni sürüm v{r.Version} hazır – 'Şimdi güncelle'");
            }
        }
        catch (Exception ex)
        {
            Log.Info("Güncelleme denetlenemedi: " + ex.Message);
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_update == null || _updating) return;
        _updating = true;
        UpdateButton.IsEnabled = false;
        var r = _update;
        try
        {
            Log.Info($"v{r.Version} indiriliyor…");
            var progress = new Progress<double>(p => UpdateButton.Content = $"İndiriliyor… %{p * 100:F0}");
            await Updater.DownloadAndRunAsync(r, progress);
            UpdateButton.Content = "Kuruluyor…";
            Log.Success($"v{r.Version} kuruluyor; program kapanıp yeni sürümle yeniden açılacak.");
            await Task.Delay(800);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("Güncelleme yapılamadı: " + ex.Message);
            UpdateButton.Content = $"⬇ Şimdi güncelle (v{r.Version})";
            UpdateButton.IsEnabled = true;
            _updating = false;
        }
    }
}
