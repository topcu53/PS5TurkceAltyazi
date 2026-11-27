using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;

namespace Ps5Altyazi;

/// <summary>
/// Temalar: Resources/Theme.template.xaml (stiller ve renkler, özgün mavi tema renkleriyle yazılmış) açılışta seçilen
/// temanın renkleriyle değiştirilip uygulamanın kaynaklarına yüklenir. Kod içinde çizilen pencereler <see cref="Theme"/>'yı kullanır.
/// </summary>
public static class ThemePalettes
{
    public sealed record Palette(string Key, string Name, string Description, Dictionary<string, string> Map,
                                 string Bg, string Bar, string Text, string Dim, string Good, string Accent, string Warn, string Bad, string Line);

    private static readonly Dictionary<string, string> Purple = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0B0F1A"] = "0E0C13", ["121826"] = "16131D", ["182033"] = "1D1926", ["222C42"] = "2A2535",
        ["E8EDF7"] = "ECE8F3", ["8B97AD"] = "A097B3", ["2F8BFF"] = "9D7BEA", ["33C6FF"] = "C3B1F7",
        ["1E5BFF"] = "7B5BD6", ["22B4F5"] = "A98BEF", ["1846D8"] = "4E3893", ["0E7CC4"] = "7B4FB5",
        ["26324A"] = "302A3D", ["2A3650"] = "3A3249", ["1F2A42"] = "262130", ["0F1422"] = "100E16",
        ["C9D2E3"] = "DCD5EA", ["5DB8FF"] = "BBA5F5", ["3A4766"] = "4C4262", ["33415E"] = "40374F",
        ["141B2B"] = "120F18", ["1D2639"] = "221D2B", ["070A12"] = "0A0910", ["AFC0DA"] = "BDB4CC",
    };

    private static readonly Dictionary<string, string> Graphite = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0B0F1A"] = "0F1012", ["121826"] = "17181B", ["182033"] = "1E2024", ["222C42"] = "2A2D32",
        ["E8EDF7"] = "E6E7EA", ["8B97AD"] = "9A9EA6", ["2F8BFF"] = "5FB89A", ["33C6FF"] = "9FDCC6",
        ["1E5BFF"] = "3E8F76", ["22B4F5"] = "6CC4A1", ["1846D8"] = "2E5E50", ["0E7CC4"] = "3F7F6A",
        ["26324A"] = "33363C", ["2A3650"] = "3A3D44", ["1F2A42"] = "25272C", ["0F1422"] = "111215",
        ["C9D2E3"] = "D6D8DC", ["5DB8FF"] = "8FD3BA", ["3A4766"] = "474B53", ["33415E"] = "3D4047",
        ["141B2B"] = "131416", ["1D2639"] = "222428", ["070A12"] = "0A0B0C", ["AFC0DA"] = "B8BCC4",
    };

    public static readonly IReadOnlyList<Palette> All = new[]
    {
        new Palette("mor", "Gece moru", "Siyaha yakın zemin, yumuşak mor vurgular (önerilen)", Purple,
            "#0E0C13", "#09080D", "#ECE8F3", "#A097B3", "#6EE7A8", "#B49CF2", "#F6C177", "#F38BA8", "#2A2535"),
        new Palette("grafit", "Grafit", "Nötr koyu gri, sakin yeşil vurgular", Graphite,
            "#0F1012", "#0A0B0C", "#E6E7EA", "#9A9EA6", "#7DDCB0", "#8FD3BA", "#E9C46A", "#EF8A8A", "#2A2D32"),
        new Palette("mavi", "Okyanus mavisi", "Önceki tema: lacivert zemin, mavi vurgular", new Dictionary<string, string>(),
            "#0B0F1A", "#070A12", "#E8EDF7", "#8B97AD", "#4ADE80", "#33C6FF", "#FFB03B", "#FF6B6B", "#222C42"),
    };

    public static Palette Current { get; private set; } = All[0];

    public static Palette Get(string? key) => All.FirstOrDefault(p => p.Key == key) ?? All[0];

    /// <summary>Özgün (mavi) tema rengini seçili temadaki karşılığına çevirir ("#RRGGBB" ya da "#AARRGGBB").</summary>
    public static string Map(string hex) => MapWith(Current, hex);

    public static string MapPublic(Palette p, string hex) => MapWith(p, hex);

    private static string MapWith(Palette p, string hex)
    {
        string h = hex.TrimStart('#');
        if (h.Length == 8) return "#" + h[..2] + (p.Map.TryGetValue(h[2..], out var a) ? a : h[2..]);
        return "#" + (p.Map.TryGetValue(h, out var m) ? m : h);
    }

    /// <summary>Temayı uygulamanın kaynaklarına yükler. Pencereler açılmadan önce (App.OnStartup) çağrılır.</summary>
    public static void Apply(Application app, string? key)
    {
        var p = Get(key);
        Current = p;
        using var s = typeof(ThemePalettes).Assembly.GetManifestResourceStream("theme.template.xaml")
                      ?? throw new InvalidOperationException("Tema şablonu bulunamadı.");
        string xaml = new StreamReader(s).ReadToEnd();
        xaml = Regex.Replace(xaml, "#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\\b", m => MapWith(p, m.Value));
        var dict = (ResourceDictionary)XamlReader.Parse(xaml);
        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(dict);

        Theme.Bg = Theme.Frozen(p.Bg);
        Theme.Bar = Theme.Frozen(p.Bar);
        Theme.Text = Theme.Frozen(p.Text);
        Theme.Dim = Theme.Frozen(p.Dim);
        Theme.Good = Theme.Frozen(p.Good);
        Theme.Accent = Theme.Frozen(p.Accent);
        Theme.Warn = Theme.Frozen(p.Warn);
        Theme.Bad = Theme.Frozen(p.Bad);
        Theme.Line = Theme.Frozen(p.Line);
    }
}
