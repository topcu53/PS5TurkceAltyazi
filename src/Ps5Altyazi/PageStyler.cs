using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ps5Altyazi;

/// <summary>
/// Ayrı pencere için yazılmış ayar sayfalarını ana pencerenin tasarımına uydurur:
/// <list type="bullet">
/// <item>Her bölüm başlığı (kalın, ≥ 14 pt yazı) ve altındakiler ayrı bir karta alınır (Ayarlar sayfalarındaki kartlarla aynı).</item>
/// <item>Başlıktan önceki açıklama yazısı kartın dışında, sayfanın girişinde soluk yazı olarak durur.</item>
/// <item>En alttaki düğme satırı kartların altına, sağa yaslı bir eylem çubuğu olur; varsayılan düğme vurgu renginde.</item>
/// <item>"Kapat" / "Tamam" düğmeleri gizlenir (sayfada "←" ile geri dönülür).</item>
/// </list>
/// Panelin içindeki kurulum panelleri (PS5 / TV) de aynı şekilde işlenir.
/// </summary>
internal static class PageStyler
{
    public static FrameworkElement Restyle(UIElement content, FrameworkElement res)
    {
        if (content is not StackPanel root) return Card(new[] { content }, null, res);
        Process(root, res, top: true);
        return root;
    }

    private static bool IsHeader(UIElement e) =>
        e is TextBlock tb && tb.FontWeight.ToOpenTypeWeight() >= 600 && tb.FontSize >= 14 && tb.Inlines.Count <= 1;

    private static bool IsNote(UIElement e) =>
        e is TextBlock tb && !IsHeader(tb) && ReferenceEquals(tb.Foreground, Theme.Dim);

    private static bool IsCloseButton(UIElement e) =>
        e is Button b && b.Content is string s && (s == "Kapat" || s == "Tamam");

    private static bool IsButtonBar(UIElement e) =>
        e is Panel p && p is not Grid && p.Children.Count > 0 && p.Children.OfType<UIElement>().All(c => c is Button) ||
        IsCloseButton(e);

    private static void Process(StackPanel sp, FrameworkElement res, bool top)
    {
        var items = sp.Children.OfType<UIElement>().ToList();
        sp.Children.Clear();
        sp.Margin = new Thickness(0);

        // İçinde başlık olan iç paneller (PS5 / TV kurulum panelleri) kendi kartlarını kurar
        foreach (var uc in items.OfType<UserControl>())
            if (uc.Content is StackPanel inner && inner.Children.OfType<UIElement>().Any(IsHeader))
                Process(inner, res, top: false);

        // Alt eylem çubuğu: sondan geriye, soluk ipucu yazılarını atlayarak ilk düğme satırı.
        // Çubuktan sonra gelen ipuçları çubuğun altında kalır.
        UIElement? bar = null;
        var tail = new List<UIElement>();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (IsButtonBar(items[i]))
            {
                bar = items[i];
                tail = items.Skip(i + 1).ToList();
                items.RemoveRange(i, items.Count - i);
                break;
            }
            if (!IsNote(items[i])) break;
        }
        // Çubuğun hemen üstündeki durum yazısı ("Kaydedildi…", "✔ …") çubukla birlikte durur
        TextBlock? status = null;
        if (bar != null && items.Count > 0 && items[^1] is TextBlock st && !IsHeader(st) && !IsNote(st))
        {
            status = st;
            items.RemoveAt(items.Count - 1);
        }

        // Gruplar: başlık + altındakiler
        var groups = new List<(TextBlock? Head, List<UIElement> Items)> { (null, new List<UIElement>()) };
        foreach (var e in items)
        {
            if (IsHeader(e)) groups.Add(((TextBlock)e, new List<UIElement>()));
            else groups[^1].Items.Add(e);
        }

        foreach (var (head, list) in groups)
        {
            list.RemoveAll(IsCloseButton);
            if (head == null)
            {
                if (list.Count == 0) continue;
                // Başlıksız giriş: yalnız açıklamalarsa kartsız giriş yazısı; içinde işlenmiş panel varsa olduğu gibi
                if (list.All(x => IsNote(x) || x is UserControl))
                {
                    foreach (var x in list)
                    {
                        if (x is TextBlock tb) { tb.FontSize = 13; tb.Margin = new Thickness(2, 0, 2, 14); tb.LineHeight = 20; }
                        sp.Children.Add(x);
                    }
                    continue;
                }
                sp.Children.Add(Card(list, null, res));
                continue;
            }
            sp.Children.Add(Card(list, head.Text, res));
        }

        if (status != null)
        {
            status.Margin = new Thickness(4, 0, 4, 8);
            status.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(status);
        }
        if (bar != null)
        {
            if (bar is Panel p)
            {
                foreach (var c in p.Children.OfType<UIElement>().Where(IsCloseButton).ToList()) p.Children.Remove(c);
                if (p.Children.Count > 0)
                {
                    foreach (var b in p.Children.OfType<Button>())
                        if (b.IsDefault && res.TryFindResource("Primary") is Style primary) b.Style = primary;
                    p.Margin = new Thickness(0, 2, 0, 8);
                    p.HorizontalAlignment = HorizontalAlignment.Right;
                    sp.Children.Add(p);
                }
            }
        }
        foreach (var t in tail)
        {
            if (t is TextBlock tb) { tb.FontSize = 12; tb.Margin = new Thickness(2, 0, 2, 8); }
            sp.Children.Add(t);
        }
        if (top) sp.Margin = new Thickness(0, 0, 0, 12);
    }

    /// <summary>Tema kartı: başlık (16 pt, yarı kalın) + içerik. Ayarlar sayfalarındaki "Panel" stiliyle aynı.</summary>
    private static Border Card(IEnumerable<UIElement> items, string? title, FrameworkElement res)
    {
        var stack = new StackPanel();
        if (title != null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }
        bool first = true;
        foreach (var e in items)
        {
            // Ayrı pencerede bölümler arası boşluk için verilen üst kenar payları kartta gereksiz
            if (first && e is FrameworkElement fe && title == null) fe.Margin = new Thickness(fe.Margin.Left, 0, fe.Margin.Right, fe.Margin.Bottom);
            first = false;
            stack.Children.Add(e);
        }
        var card = new Border { Child = stack, Margin = new Thickness(0, 0, 0, 14) };
        if (res.TryFindResource("Panel") is Style panel) card.Style = panel;
        else { card.Background = Theme.Bar; card.CornerRadius = new CornerRadius(18); card.Padding = new Thickness(20, 16, 20, 16); }
        return card;
    }
}
