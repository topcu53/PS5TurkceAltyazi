using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Ps5Altyazi.Translation;

string dir = args[0];
var sw = Stopwatch.StartNew();
using var m = new MarianOnnx(dir);
Console.WriteLine($"Model yüklendi: {sw.ElapsedMilliseconds} ms");
string[] samples =
{
    "Hey, let me call his goofy ass.",
    "Man, my shit just got stolen, bro.",
    "It's comin' dog. Just be patient, dog. Damn.",
    "Bro, I just wanted to say I respect these kicks you just dropped, king.",
    "Are you okay? I saw the whole thing, do you need my statement or something?",
    "You heard of Venture Apartments? Up off Northbrook Drive in Rockridge.",
    "Okay, we're done. I have nothing for you.",
    "Drive to the Speedshop.",
};
// Parçalayıcı karşılaştırması için kimlikler (CI'da Python'daki resmi parçalayıcıyla kıyaslanır)
File.WriteAllLines(Path.Combine(dir, "samples.txt"), samples);
File.WriteAllLines(Path.Combine(dir, "cs_ids.txt"), samples.Select(s => string.Join(" ", m.Encode(s))));
m.Translate("Warm up.");
foreach (var s in samples)
{
    sw.Restart();
    string t = m.Translate(s);
    Console.WriteLine($"{sw.ElapsedMilliseconds,5} ms | {s}\n         → {t}");
}
