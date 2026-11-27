using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ps5Altyazi.Settings;

namespace Ps5Altyazi;

/// <summary>
/// Oyun seçimi. Her oyunun kendi altyazı alanı ve yazı tipi ayarı saklanır; oyun seçilince geri gelir.
/// PS5'e doğrudan bağlıyken PS5'te açık oyun kendiliğinden seçilebilir.
/// </summary>
public sealed class GamePickerWindow : PageWindow
{
    /// <summary>Sık oynanan PS5 oyunları (liste yalnız kolaylık içindir; listede olmayan oyun da yazılabilir).</summary>
    public static readonly string[] KnownGames =
    {
        "Grand Theft Auto VI", "Red Dead Redemption 2", "Grand Theft Auto V", "The Last of Us Part I", "The Last of Us Part II Remastered",
        "God of War Ragnarök", "Ghost of Tsushima", "Marvel's Spider-Man 2", "Marvel's Spider-Man: Miles Morales",
        "Horizon Forbidden West", "Elden Ring", "Cyberpunk 2077", "Baldur's Gate 3", "Hogwarts Legacy",
        "Resident Evil 4", "Resident Evil Village", "Alan Wake 2", "Death Stranding", "Days Gone",
        "Uncharted: Legacy of Thieves Collection", "Final Fantasy XVI", "Final Fantasy VII Rebirth",
        "Star Wars Jedi: Survivor", "Assassin's Creed Shadows", "Assassin's Creed Valhalla", "Mafia: Definitive Edition",
        "Detroit: Become Human", "The Witcher 3: Wild Hunt", "Black Myth: Wukong", "Stellar Blade", "Silent Hill 2",
        "Metal Gear Solid Delta: Snake Eater", "Ghost of Yōtei", "Death Stranding 2: On the Beach", "Kingdom Come: Deliverance II",
        "Indiana Jones and the Great Circle", "Dragon Age: The Veilguard", "Persona 5 Royal", "Mass Effect Legendary Edition",
    };

    public string? Chosen { get; private set; }

    private readonly ListBox _list = new() { Height = 300, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _search = new() { Padding = new Thickness(6, 4, 6, 4) };
    private readonly List<string> _all;

    public GamePickerWindow(AppSettings s, string? runningOnPs5)
    {
        Title = "Oyun seç";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Önce daha önce oynananlar (en son oynanan üstte), sonra liste
        var played = s.GameFonts.Where(kv => kv.Key != "Genel")
            .OrderByDescending(kv => kv.Value.LastPlayed ?? DateTime.MinValue).Select(kv => kv.Key).ToList();
        _all = played.Concat(KnownGames.Where(g => !played.Contains(g, StringComparer.OrdinalIgnoreCase))).ToList();

        var root = new StackPanel { Margin = new Thickness(20, 14, 20, 16) };
        root.Children.Add(new TextBlock
        {
            Text = "Her oyunun altyazı alanı ve yazı tipi ayrı saklanır; oyunu seçince kendiliğinden geri gelir.",
            TextWrapping = TextWrapping.Wrap, Foreground = Theme.Dim, Margin = new Thickness(0, 0, 0, 10)
        });

        var auto = new CheckBox { Content = "PS5'te açık oyunu kendiliğinden seç (PS5'e doğrudan bağlıyken)", IsChecked = s.AutoGame };
        auto.Checked += (_, _) => s.AutoGame = true;
        auto.Unchecked += (_, _) => s.AutoGame = false;
        root.Children.Add(auto);

        if (!string.IsNullOrWhiteSpace(runningOnPs5))
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var pick = new Button { Content = "Bunu seç", Padding = new Thickness(12, 4, 12, 4) };
            pick.Click += (_, _) => Done(runningOnPs5);
            DockPanel.SetDock(pick, Dock.Right);
            row.Children.Add(pick);
            row.Children.Add(new TextBlock { Text = $"PS5'te şu an açık: {runningOnPs5}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            root.Children.Add(row);
        }

        root.Children.Add(new TextBlock { Text = "Ara ya da yeni oyun adı yaz:", Margin = new Thickness(0, 12, 0, 4) });
        root.Children.Add(_search);
        root.Children.Add(_list);
        Fill("");
        _search.TextChanged += (_, _) => Fill(_search.Text);
        _search.KeyDown += (_, e) => { if (e.Key == Key.Enter) Done(_list.SelectedItem as string ?? _search.Text); };
        _list.MouseDoubleClick += (_, _) => { if (_list.SelectedItem is string g) Done(g); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var general = new Button { Content = "Genel (oyunsuz)", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        general.Click += (_, _) => Done("Genel");
        var cancel = new Button { Content = "Vazgeç", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Seç", Padding = new Thickness(18, 5, 18, 5), FontWeight = FontWeights.Bold, IsDefault = true };
        cancel.Click += (_, _) => Finish();
        ok.Click += (_, _) => Done(_list.SelectedItem as string ?? _search.Text);
        buttons.Children.Add(general);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
        Theme.Apply(this);
        WhenOpened(() => _search.Focus());
    }

    private void Fill(string q)
    {
        q = q.Trim();
        _list.ItemsSource = q.Length == 0 ? _all : _all.Where(g => g.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private void Done(string? name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > 60) return;
        Chosen = name;
        Finish(true);
    }
}
