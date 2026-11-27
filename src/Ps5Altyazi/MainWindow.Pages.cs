using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ps5Altyazi;

/// <summary>
/// Ayar sayfalarını ayrı pencere yerine ana pencerenin içinde açar: Ayarlar'daysa sağdaki içerik alanında (soldaki
/// menü yerinde kalır), kurulum sırasında ya da ana ekrandaysa sekmelerin altındaki alanın tamamında.
/// Üstte "← Geri" ve sayfanın adı durur; sayfa kapanınca önceki görünüm geri gelir.
/// </summary>
public partial class MainWindow
{
    private sealed record OpenPage(PageWindow Page, FrameworkElement View, TaskCompletionSource<bool?> Done);

    private readonly List<OpenPage> _openPages = new();
    private Grid? _pageHost;
    private DateTime _lastPageClose;

    internal bool CanHostPages => MainGrid != null;

    /// <summary>Sayfayı ana pencerede gösterir; kapanınca sonucu döner.</summary>
    internal Task<bool?> ShowPageAsync(PageWindow page)
    {
        var done = new TaskCompletionSource<bool?>();
        var content = page.TakeContentForInline(r => ClosePage(page, r));
        var view = BuildPageView(page, content);
        if (_openPages.Count == 0) PlaceHost();
        foreach (var p in _openPages) p.View.Visibility = Visibility.Collapsed;
        _openPages.Add(new OpenPage(page, view, done));
        _pageHost!.Children.Add(view);
        _pageHost.Visibility = Visibility.Visible;
        // İçerik ağaca girip çizildikten sonra (odak verme vb. için)
        Dispatcher.InvokeAsync(page.RaiseOpened, System.Windows.Threading.DispatcherPriority.Loaded);
        return done.Task;
    }

    /// <summary>Kapsayıcıyı yerleştirir: Ayarlar açıksa sağ içerik sütununa, değilse sekmelerin altındaki alanın tamamına.</summary>
    private void PlaceHost()
    {
        _pageHost ??= new Grid { Background = (System.Windows.Media.Brush)FindResource("Bg") };
        if (_pageHost.Parent is Panel old) old.Children.Remove(_pageHost);
        Panel.SetZIndex(_pageHost, 50);
        if (SettingsPage.Visibility == Visibility.Visible && SetupPage.Visibility != Visibility.Visible)
        {
            Grid.SetRow(_pageHost, 0);
            Grid.SetColumn(_pageHost, 2);
            SettingsPage.Children.Add(_pageHost);
        }
        else
        {
            Grid.SetRow(_pageHost, 1);
            Grid.SetColumn(_pageHost, 0);
            MainGrid.Children.Add(_pageHost);
        }
    }

    private FrameworkElement BuildPageView(PageWindow page, UIElement content)
    {
        bool full = _pageHost?.Parent is not Grid g || !ReferenceEquals(g, SettingsPage);
        var dock = new DockPanel { LastChildFill = true };

        // ── Başlık: yuvarlak "←" düğmesi · büyük başlık · altında soluk açıklama (Ayarlar sayfalarıyla aynı düzen)
        string title = page.Title ?? "", sub = "";
        int dash = title.IndexOf(" – ", StringComparison.Ordinal);
        if (dash > 0) { sub = title[(dash + 3)..]; title = title[..dash]; }
        var head = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var back = new Button
        {
            Content = "←", FontSize = 18, Width = 42, Height = 42, Padding = new Thickness(0), Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "Geri (Esc)",
        };
        back.Click += (_, _) => ClosePage(page, null);
        head.Children.Add(back);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = MainWindow.Cap(title), FontSize = 24, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (sub.Length > 0)
            titles.Children.Add(new TextBlock { Text = MainWindow.Cap(sub), Foreground = (System.Windows.Media.Brush)FindResource("TextDim"), Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);

        // ── İçerik: bölümler ayrı kartlara ayrılır (temaya uygun), sayfa kendi "Kapat / Tamam" düğmesi gizlenir
        if (content is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Stretch;
            fe.VerticalAlignment = page.InlineScroll ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        }
        FrameworkElement body;
        if (!page.InlineScroll)
        {
            body = new Border { Style = (Style)FindResource("Panel"), Padding = new Thickness(6), Child = content };
        }
        else
        {
            var inner = content is ScrollViewer sv0 ? sv0.Content as UIElement : content;
            if (content is ScrollViewer sv1) sv1.Content = null;
            var styled = PageStyler.Restyle(inner!, this);
            styled.MaxWidth = full ? 940 : double.PositiveInfinity;
            styled.HorizontalAlignment = HorizontalAlignment.Stretch;
            body = new ScrollViewer
            {
                Content = styled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0),
            };
        }
        if (full) head.MaxWidth = 940; // kurulumda / ana ekranda ortada, okunur genişlikte
        dock.Children.Add(body);
        return dock;
    }

    private void ClosePage(PageWindow page, bool? result)
    {
        int i = _openPages.FindIndex(p => ReferenceEquals(p.Page, page));
        if (i < 0) return;
        _lastPageClose = DateTime.Now;
        // Üstündeki sayfalar da kapanır
        for (int k = _openPages.Count - 1; k >= i; k--)
        {
            var p = _openPages[k];
            _openPages.RemoveAt(k);
            _pageHost?.Children.Remove(p.View);
            try { p.Page.RaiseClosed(); } catch (Exception ex) { Logging.Log.Warn("Sayfa kapanırken: " + ex.Message); }
            p.Done.TrySetResult(k == i ? result : null);
        }
        if (_openPages.Count > 0) _openPages[^1].View.Visibility = Visibility.Visible;
        else if (_pageHost != null) _pageHost.Visibility = Visibility.Collapsed;
    }

    /// <summary>Açık bütün sayfaları kapatır (sekme / menü değişince).</summary>
    private void CloseAllPages()
    {
        if (_openPages.Count > 0) ClosePage(_openPages[0].Page, null);
    }

    /// <summary>Esc: en üstteki sayfayı kapatır. true: işlendi.</summary>
    private bool HandlePageKey(KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _openPages.Count == 0) return false;
        // Sayfadaki "Kapat / Vazgeç" düğmesi Esc ile zaten kapattıysa bir alttakini de kapatma
        if ((DateTime.Now - _lastPageClose).TotalMilliseconds < 400) { e.Handled = true; return true; }
        ClosePage(_openPages[^1].Page, null);
        e.Handled = true;
        return true;
    }
}

/// <summary>Pencere boyutuna göre yerleşim: dar pencerede düğmeler iki satıra geçer, sağ sütun daralır.</summary>
public partial class MainWindow
{
    private bool _bigButtonsStacked;

    private void BigButtons_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool stack = BigButtons.ActualWidth < 600;
        if (stack == _bigButtonsStacked && e.PreviousSize.Width > 0) return;
        _bigButtonsStacked = stack;
        if (stack)
        {
            BigButtons.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            BigButtons.ColumnDefinitions[2].Width = new GridLength(0);
            Grid.SetColumnSpan(StartStopButton, 2);
            StartStopButton.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetRow(AutoDetectButton, 1); Grid.SetColumn(AutoDetectButton, 0);
            AutoDetectButton.Margin = new Thickness(0, 0, 5, 0);
            Grid.SetRow(TestTvButton, 1); Grid.SetColumn(TestTvButton, 1);
            TestTvButton.Margin = new Thickness(5, 0, 0, 0);
        }
        else
        {
            BigButtons.ColumnDefinitions[0].Width = new GridLength(1.3, GridUnitType.Star);
            BigButtons.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumnSpan(StartStopButton, 1);
            StartStopButton.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetRow(AutoDetectButton, 0); Grid.SetColumn(AutoDetectButton, 1);
            AutoDetectButton.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetRow(TestTvButton, 0); Grid.SetColumn(TestTvButton, 2);
            TestTvButton.Margin = new Thickness(0);
        }
    }

    /// <summary>Üst bant: dar pencerede durum yazısı kısalır, yapımcı satırı gizlenir (sekmelerle çakışmasın).</summary>
    private void MainGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = e.NewSize.Width;
        CaptureStatus.MaxWidth = w < 1000 ? 110 : w < 1150 ? 160 : 220;
        HeaderSub.Visibility = w < 1000 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Sağdaki durum sütunu: geniş pencerede 380, dar pencerede 330 piksel.</summary>
    private void HomePage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = e.NewSize.Width;
        RightCol.Width = new GridLength(w < 1000 ? 330 : 380);
    }
}
