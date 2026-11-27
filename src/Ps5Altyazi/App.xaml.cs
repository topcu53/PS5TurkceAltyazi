using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ps5Altyazi.Logging;

namespace Ps5Altyazi;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error($"Beklenmeyen hata (AppDomain): {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error($"Gözlemlenmeyen görev hatası: {args.Exception}");
            args.SetObserved();
        };
        try { ThemePalettes.Apply(this, Settings.AppSettings.Load().ThemeName); }
        catch (Exception ex)
        {
            Log.Error("Tema yüklenemedi, eski tema kullanılıyor: " + ex.Message);
            ThemePalettes.Apply(this, "mavi");
        }
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error($"Beklenmeyen arayüz hatası: {e.Exception}");
        MessageBox.Show(
            $"Beklenmeyen bir hata oluştu:\n\n{e.Exception.Message}\n\nAyrıntılar günlük dosyasında:\n{Log.CurrentFile}",
            "Altyazı+ – Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
