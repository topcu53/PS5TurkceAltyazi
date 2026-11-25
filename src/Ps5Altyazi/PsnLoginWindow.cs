// SPDX-License-Identifier: AGPL-3.0-only
// CouchSubs'ın PsnLoginWindow'undan (github.com/yorulmazsinan/couchsubs, AGPL-3.0) uyarlandı.
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CouchSubs.RemotePlay;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Ps5Altyazi;

/// <summary>
/// Sony'nin kendi PSN giriş sayfasını program içinde açar; giriş bitince Sony'nin yönlendirdiği adresi yakalar.
/// Şifre yalnız Sony'ye gider, programa gelmez ve hiçbir yere kaydedilmez.
/// </summary>
public sealed class PsnLoginWindow : Window
{
    private readonly WebView2 _browser = new();
    private Uri? _redirect;
    private string? _problem;
    private bool _useBrowser;
    private static CoreWebView2Environment? _env; // tarayıcı süreci kapanana kadar canlı kalsın

    private PsnLoginWindow()
    {
        Title = "PlayStation Network girişi";
        Width = 540;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var dock = new DockPanel();
        var top = new DockPanel { Margin = new Thickness(12, 8, 12, 8) };
        var cancel = new Button { Content = "Vazgeç", Padding = new Thickness(12, 4, 12, 4) };
        cancel.Click += (_, _) => Close();
        DockPanel.SetDock(cancel, Dock.Right);
        top.Children.Add(cancel);
        top.Children.Add(new TextBlock
        {
            Text = "İkinci PSN hesabınızla giriş yapın (şifreniz yalnız Sony'ye gider).",
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Dim
        });
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(_browser);
        Content = dock;
        Theme.Apply(this, canResize: true);

        Loaded += async (_, _) =>
        {
            try { await OpenAsync(); }
            catch (Exception ex)
            {
                _problem = "WebView2: " + ex.Message;
                Close();
            }
        };
        Closed += (_, _) => { try { _browser.Dispose(); } catch { } };
    }

    /// <summary>Giriş tamamlanırsa yönlendirme adresi; değilse null ve (varsa) sorun. UseBrowser: giriş tarayıcıda yapılmalı.</summary>
    public static (Uri? Redirect, string? Problem, bool UseBrowser) Show(Window? owner)
    {
        var w = new PsnLoginWindow { Owner = owner };
        w.ShowDialog();
        return w._redirect != null ? (w._redirect, null, false) : (null, w._problem, w._useBrowser);
    }

    /// <summary>WebView2 çalışma ortamı kurulu mu (Windows 11'de ve güncel Windows 10'da hazır gelir).</summary>
    public static bool IsBrowserAvailable()
    {
        try { return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
        catch { return false; }
    }

    private const string PageTextWatcher = """
        (() => {
          if (window.top !== window || !window.chrome || !window.chrome.webview) return;
          let last = '', timer = 0;
          const send = () => {
            timer = 0;
            const text = document.body ? (document.body.innerText || '').slice(0, 4000) : '';
            if (text === last) return;
            last = text;
            window.chrome.webview.postMessage('altyazi-page-text:' + text);
          };
          const schedule = () => { if (!timer) timer = setTimeout(send, 500); };
          const start = () => {
            schedule();
            new MutationObserver(schedule).observe(document.documentElement, { subtree: true, childList: true, characterData: true });
          };
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
        })();
        """;

    private async Task OpenAsync()
    {
        // Her girişte temiz oturum: önceki çerezler (ör. ana hesabınız) karışmasın.
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS5TurkceAltyazi", "webview");
        if (Directory.Exists(root))
            foreach (var d in Directory.EnumerateDirectories(root)) TryDelete(d);
        string folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        var env = await CoreWebView2Environment.CreateAsync(null, folder);
        _env = env;
        env.BrowserProcessExited += (_, _) => TryDelete(folder);
        await _browser.EnsureCoreWebView2Async(env);
        var core = _browser.CoreWebView2;
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(PsnAccount.RedirectPrefix, StringComparison.Ordinal)) return;
            e.Cancel = true;
            _redirect = new Uri(e.Uri);
            Close();
        };
        core.NavigationCompleted += (_, e) =>
        {
            if (_redirect != null) return;
            if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                _problem = e.HttpStatusCode >= 400 ? $"HTTP {e.HttpStatusCode}" : e.WebErrorStatus.ToString();
            else if (e.IsSuccess) _problem = e.HttpStatusCode >= 400 ? $"HTTP {e.HttpStatusCode}" : null;
        };
        core.WebMessageReceived += (_, e) =>
        {
            if (_redirect != null || _useBrowser) return;
            string msg;
            try { msg = e.TryGetWebMessageAsString(); } catch { return; }
            const string prefix = "altyazi-page-text:";
            if (!msg.StartsWith(prefix, StringComparison.Ordinal) || !PsnAccount.LooksServerUnreachable(msg[prefix.Length..])) return;
            // Sony'nin sayfası program içinde sunucuya bağlanamadı: giriş tarayıcıda sürdürülür.
            _useBrowser = true;
            _problem = "Sony: Can't connect to the server";
            Close();
        };
        await core.AddScriptToExecuteOnDocumentCreatedAsync(PageTextWatcher);
        core.Navigate(PsnAccount.AuthorizeUrl.AbsoluteUri);
    }

    private static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, recursive: true); } catch { }
    }
}
