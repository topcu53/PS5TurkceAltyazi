// SPDX-License-Identifier: AGPL-3.0-only
// Altyazı+ için CouchSubs'tan (github.com/yorulmazsinan/couchsubs, AGPL-3.0) alındı (CouchSubs.Core/Frame.cs ve
// ConnectingHint.cs'nin Remote Play katmanının kullandığı kısmı).
using System;

namespace CouchSubs.RemotePlay;

/// <summary>BGRA (piksel başına 4 bayt) görüntü. PS5 akışından gelen kareler.</summary>
public sealed class Frame(byte[] pixels, int width, int height, int stride)
{
    public byte[] Pixels { get; } = pixels;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Stride { get; } = stride;

    /// <summary>Yerel kodun verdiği tamponun kopyası (tampon yalnız geri çağrı süresince geçerlidir).</summary>
    public static unsafe Frame Copy(IntPtr bgra, int width, int height, int stride)
    {
        var pixels = new byte[width * height * 4];
        var source = (byte*)bgra;
        fixed (byte* target = pixels)
            for (var y = 0; y < height; y++)
                Buffer.MemoryCopy(source + ((long)y * stride), target + ((long)y * width * 4), width * 4, width * 4);
        return new Frame(pixels, width, height, width * 4);
    }
}

/// <summary>Oturumun bağlanırken son bildirdiği adım.</summary>
public enum ConnectStep
{
    /// <summary>PS5 aramaya yanıt vermedi; yine de bağlanılıyor.</summary>
    NoAnswer,
    /// <summary>PS5 dinlenmedeydi; uyandırma gönderildi.</summary>
    Waking,
    /// <summary>PS5 yanıt verdi; oturum açılıyor.</summary>
    Opening,
}
