import math, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

S = 1024
NOTO = '/usr/share/fonts/truetype/freefont/FreeSansBold.ttf'

def grad(size, top, bot):
    w, h = size
    im = Image.new('RGBA', size)
    d = ImageDraw.Draw(im)
    for y in range(h):
        t = y / (h - 1)
        c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)) + (255,)
        d.line([(0, y), (w, y)], fill=c)
    return im

def rounded_mask(size, r):
    m = Image.new('L', size, 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, size[0] - 1, size[1] - 1], r, fill=255)
    return m

def flag_badge(d, cx, cy, r):
    # Türk bayrağı rozeti: kırmızı daire, beyaz hilal ve yıldız
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(227, 10, 23, 255), outline=(255, 255, 255, 255), width=max(2, int(r * 0.12)))
    R = r * 0.55
    ox = cx - r * 0.18
    d.ellipse([ox - R, cy - R, ox + R, cy + R], fill=(255, 255, 255, 255))
    R2 = R * 0.8
    ox2 = ox + R * 0.25
    d.ellipse([ox2 - R2, cy - R2, ox2 + R2, cy + R2], fill=(227, 10, 23, 255))
    # yıldız
    sx, sy, sr = ox2 + R2 * 0.95, cy, r * 0.22
    pts = []
    for k in range(10):
        ang = -math.pi / 2 + k * math.pi / 5 + math.pi / 2  # bir ucu sola baksın
        rr = sr if k % 2 == 0 else sr * 0.42
        pts.append((sx + rr * math.cos(ang + math.pi), sy + rr * math.sin(ang + math.pi)))
    d.polygon(pts, fill=(255, 255, 255, 255))

def icon(size=S):
    im = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    bg = grad((S, S), (18, 52, 140), (8, 12, 38))
    # mavi parıltı
    glow = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse([S * 0.1, S * 0.05, S * 0.9, S * 0.75], fill=(60, 140, 255, 120))
    glow = glow.filter(ImageFilter.GaussianBlur(S * 0.12))
    bg = Image.alpha_composite(bg, glow)
    im.paste(bg, (0, 0), rounded_mask((S, S), int(S * 0.22)))
    d = ImageDraw.Draw(im)

    # Ekran (TV) çerçevesi
    x0, y0, x1, y1 = S * 0.12, S * 0.25, S * 0.88, S * 0.76
    shadow = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([x0, y0 + S * 0.02, x1, y1 + S * 0.02], int(S * 0.06), fill=(0, 0, 0, 150))
    im = Image.alpha_composite(im, shadow.filter(ImageFilter.GaussianBlur(S * 0.025)))
    d = ImageDraw.Draw(im)
    scr = grad((int(x1 - x0), int(y1 - y0)), (34, 44, 82), (14, 18, 40))
    im.paste(scr, (int(x0), int(y0)), rounded_mask(scr.size, int(S * 0.06)))
    d.rounded_rectangle([x0, y0, x1, y1], int(S * 0.06), outline=(120, 200, 255, 255), width=int(S * 0.022))
    # ayak
    d.rounded_rectangle([S * 0.40, y1 + S * 0.035, S * 0.60, y1 + S * 0.065], int(S * 0.015), fill=(120, 200, 255, 255))

    # Altyazı satırları: üstte beyaz (İngilizce yerine Türkçe), altta sarı kısa satır
    d.rounded_rectangle([S * 0.22, S * 0.54, S * 0.78, S * 0.605], int(S * 0.03), fill=(255, 255, 255, 255))
    d.rounded_rectangle([S * 0.31, S * 0.645, S * 0.69, S * 0.71], int(S * 0.03), fill=(255, 201, 77, 255))
    # oynat üçgeni (oyun) ekranın üst kısmında
    cx, cy, r = S * 0.5, S * 0.405, S * 0.08
    d.polygon([(cx - r * 0.7, cy - r), (cx - r * 0.7, cy + r), (cx + r, cy)], fill=(120, 200, 255, 255))

    # "TR" yazılı bayrak rozeti sağ üstte
    flag_badge(d, S * 0.81, S * 0.24, S * 0.13)

    return im

def banner(w=1280, h=720):
    im = grad((w, h), (18, 52, 140), (8, 12, 38))
    glow = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([-w * 0.1, -h * 0.3, w * 0.6, h * 1.0], fill=(60, 140, 255, 110))
    im = Image.alpha_composite(im, glow.filter(ImageFilter.GaussianBlur(h * 0.15)))
    ic = icon().resize((int(h * 0.82), int(h * 0.82)), Image.LANCZOS)
    im.paste(ic, (int(h * 0.09), int(h * 0.09)), ic)
    d = ImageDraw.Draw(im)
    f1 = ImageFont.truetype(NOTO, int(h * 0.17))
    f2 = ImageFont.truetype(NOTO, int(h * 0.062))
    x = int(h * 0.98)
    d.text((x, h * 0.24), "Altyazı+", font=f1, fill=(255, 255, 255, 255))
    d.text((x + 6, h * 0.56), "PS5 için Türkçe altyazı", font=f2, fill=(255, 201, 77, 255))
    return im

if __name__ == '__main__':
    out = sys.argv[1]
    big = icon()
    for name, sz in [('icon80.png', 80), ('icon130.png', 130), ('icon192.png', 192), ('icon512.png', 512)]:
        big.resize((sz, sz), Image.LANCZOS).save(f'{out}/{name}')
    banner().resize((320, 180), Image.LANCZOS).save(f'{out}/banner320.png')
    big.resize((512, 512), Image.LANCZOS).save(f'{out}/preview.png')
