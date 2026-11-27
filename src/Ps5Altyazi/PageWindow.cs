using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Ps5Altyazi;

/// <summary>
/// Ayar sayfalarının ortak tabanı. Ana pencere açıkken sayfa ayrı pencere olarak değil, ana pencerenin içinde
/// (o an bakılan yerde, üstte "← Geri" ile) açılır; ana pencere yoksa eskisi gibi ayrı pencere olarak gösterilir.
/// Sayfa kodu kapanmak için <see cref="Finish"/>, açılış / kapanış işleri için <see cref="WhenOpened"/> /
/// <see cref="WhenClosed"/> kullanır (Window.Close, DialogResult, Loaded, Closed yerine).
/// </summary>
public class PageWindow : Window
{
    /// <summary>Sayfanın asıl içeriği (Theme.Apply başlık çubuğuyla sarmadan önceki hali).</summary>
    internal UIElement? PageContent { get; set; }
    internal bool IsInline { get; private set; }
    /// <summary>Ana pencerede açılırken içerik kaydırılabilir bir alana konsun mu?</summary>
    internal virtual bool InlineScroll => true;
    private Action<bool?>? _end;
    private event Action? Opened, Ended;
    private bool _closedRaised;

    /// <summary>Sayfayı kapatır. result: true = onaylandı, null = vazgeçildi / kapatıldı.</summary>
    protected void Finish(bool? result = null)
    {
        if (IsInline)
        {
            var end = _end;
            _end = null;
            end?.Invoke(result);
            return;
        }
        if (result != null) { try { DialogResult = result; return; } catch (InvalidOperationException) { } }
        Close();
    }

    /// <summary>Sayfa ekranda göründüğünde (ayrı pencerede: Loaded).</summary>
    protected void WhenOpened(Action a)
    {
        Loaded += (_, _) => { if (!IsInline) a(); };
        Opened += a;
    }

    /// <summary>Sayfa kapandığında (ayrı pencerede: Closed).</summary>
    protected void WhenClosed(Action a)
    {
        Closed += (_, _) => { if (!IsInline) a(); };
        Ended += a;
    }

    /// <summary>İçeriği pencereden ayırıp ana pencereye verir.</summary>
    internal UIElement TakeContentForInline(Action<bool?> end)
    {
        IsInline = true;
        _end = end;
        var c = PageContent ?? (UIElement)Content;
        Detach(c);
        if (ReferenceEquals(Content, c)) Content = null;
        return c;
    }

    internal void RaiseOpened() => Opened?.Invoke();

    internal void RaiseClosed()
    {
        if (_closedRaised) return;
        _closedRaised = true;
        Ended?.Invoke();
    }

    private static void Detach(UIElement c)
    {
        var parent = LogicalTreeHelper.GetParent(c) ?? System.Windows.Media.VisualTreeHelper.GetParent(c);
        switch (parent)
        {
            case Panel p: p.Children.Remove(c); break;
            case ContentControl cc: cc.Content = null; break;
            case Decorator d: d.Child = null; break;
            case ContentPresenter cp: cp.Content = null; break;
        }
    }
}

/// <summary>Sayfaları ana pencerenin içinde açan yardımcı.</summary>
public static class Pages
{
    /// <summary>
    /// Sayfayı gösterir ve kapanmasını bekler. Ana pencere görünürse içinde açılır; değilse ayrı pencere (ShowDialog).
    /// Dönüş: true = onaylandı, null/false = vazgeçildi.
    /// </summary>
    public static Task<bool?> ShowAsync(PageWindow page)
    {
        if (Application.Current?.MainWindow is MainWindow mw && mw.IsVisible && mw.CanHostPages)
            return mw.ShowPageAsync(page);
        var owner = Application.Current?.MainWindow;
        if (owner != null && owner.IsVisible && !ReferenceEquals(owner, page)) page.Owner = owner;
        page.WindowStartupLocation = page.Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        return Task.FromResult(page.ShowDialog());
    }
}
