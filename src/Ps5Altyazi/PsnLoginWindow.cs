using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Ps5Altyazi.Ps5;

namespace Ps5Altyazi;

/// <summary>
/// Sony'nin PSN giriş sayfasını program içinde (Microsoft Edge WebView2) gösterir. Giriş bitince Sony'nin gönderdiği
/// yönlendirme adresi yakalanır ve pencere kapanır. Şifre yalnız Sony'nin sayfasına yazılır.
/// </summary>
public sealed class PsnLoginWindow : Window
{
    private readonly WebView2 _web = new();
    private string? _result;
    private string? _error;

    public static bool WebViewInstalled
    {
        get
        {
            try { return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
            catch { return false; }
        }
    }

    /// <summary>Girişi açar. Dönüş: yönlendirme adresi (başarılı) ya da null ve hata metni.</summary>
    public static (string? RedirectUrl, string? Error) Run(Window owner)
    {
        var w = new PsnLoginWindow { Owner = owner };
        w.ShowDialog();
        return (w._result, w._result == null ? w._error : null);
    }

    private PsnLoginWindow()
    {
        Title = "PSN girişi – ikinci hesabınızla giriş yapın";
        Width = 560;
        Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var hint = new TextBlock
        {
            Text = "Bu sayfa Sony'nin kendi giriş sayfasıdır; şifreniz Altyazı+'a gelmez. Giriş bitince pencere kendiliğinden kapanır.",
            TextWrapping = TextWrapping.Wrap, Foreground = Theme.Dim, Margin = new Thickness(12, 8, 12, 8)
        };
        grid.Children.Add(hint);
        Grid.SetRow(_web, 1);
        grid.Children.Add(_web);
        Content = grid;
        Theme.Apply(this, canResize: true);

        Loaded += async (_, _) =>
        {
            try
            {
                // Her girişte boş bir tarayıcı profili: daha önce açılmış (ana) hesap otomatik girilmesin.
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS5TurkceAltyazi", "psn-giris");
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                var env = await CoreWebView2Environment.CreateAsync(null, dir);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.NavigationStarting += (_, e) =>
                {
                    if (!PsnLogin.IsRedirect(e.Uri)) return;
                    e.Cancel = true;
                    _result = e.Uri;
                    Close();
                };
                _web.CoreWebView2.NavigationCompleted += (_, e) =>
                {
                    if (_result == null && !e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                        _error = "Sayfa açılamadı: " + e.WebErrorStatus;
                };
                _web.CoreWebView2.Navigate(PsnLogin.LoginUrl);
            }
            catch (Exception ex)
            {
                _error = "Tarayıcı başlatılamadı: " + ex.Message;
                Close();
            }
        };
        Closed += (_, _) => { try { _web.Dispose(); } catch { } };
    }
}
