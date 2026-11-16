using System;
using System.Collections.Generic;
using System.Linq;

namespace Ps5Altyazi.Fonts;

/// <summary>
/// Popüler oyunlar için hazır yazı tipi ailesi: oyunun kendi (lisanslı) fontuna en yakın ücretsiz font ailesi.
/// Kalınlık ve boy yine oyunun ekranındaki altyazıdan ölçülür; yalnızca aday aile daraltılır.
/// Listede olmayan oyunlarda tüm katalog içinden en benzeri otomatik seçilir.
/// </summary>
public sealed record GamePreset(string Name, string FamilyPrefix, string Original, string[] Aliases);

public static class GamePresets
{
    public static readonly IReadOnlyList<GamePreset> All = new[]
    {
        // GTA V arayüz/altyazı: Chalet 1960 (Helvetica'ya yakın grotesk) → Arimo
        new GamePreset("GTA V", "arimo", "Chalet 1960", new[] { "gta 5", "gta5", "gta v", "grand theft auto v", "grand theft auto 5", "gta online" }),
        // The Last of Us arayüzü: FF DIN → DIN esinli Barlow
        new GamePreset("The Last of Us", "barlow", "FF DIN", new[] { "tlou", "the last of us", "last of us", "tlou1", "tlou2", "the last of us part i", "the last of us part ii" }),
        // Elden Ring metinleri: Agmena (klasik serif) → EB Garamond
        new GamePreset("Elden Ring", "eb-garamond", "Agmena", new[] { "elden ring", "eldenring", "nightreign", "elden ring nightreign" }),
    };

    private static string Norm(string s) => new string(s.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim();

    public static GamePreset? Find(string? game)
    {
        if (string.IsNullOrWhiteSpace(game)) return null;
        string n = Norm(game);
        return All.FirstOrDefault(p => Norm(p.Name) == n || p.Aliases.Any(a => Norm(a) == n));
    }
}
