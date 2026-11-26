using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Ps5Altyazi;

/// <summary>
/// Türkçe altyazı penceresi: yalnızca Türkçe metin, büyük kalın beyaz yazı, yarı saydam siyah zemin.
/// - Normal mod: ekranın alt ortası (veya kullanıcının sürüklediği yer); alt kenar sabit, yukarı büyür.
/// - Örtme modu: kutu oyunun İngilizce altyazısının tam üstüne yerleşir ve onu örter.
/// - Fare tıklamalarını geçirir (Remote Play'i engellemez); düzenleme modunda sürüklenir.
/// - Metin yoksa görünmez. Kısa bildirimler (F6/F8) birkaç saniye gösterilir.
/// </summary>
public sealed class SubtitleWindow : Window
{
    private readonly Border _box;
    private readonly TextBlock _text;
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(1.8) };

    private bool _editMode, _muted, _coverMode, _matchSize = true;
    private Brush _userBackground = new SolidColorBrush(Color.FromArgb(153, 0, 0, 0));
    private Color _bgColor = Colors.Black;
    private double _bgOpacity = 0.6;
    private Brush? _userTextColor;
    private double _coverLineHeightPx;
    private string _current = "";
    private string _notice = "";
    private double _baseFontSize = 42;

    // Normal yerleşim (DIP)
    private double _normalWidth = 900, _normalLeft, _normalBottom;
    private bool _placed;

    // Örtme modunda İngilizce altyazının ekran dikdörtgeni (fiziksel piksel)
    private Rect? _coverPx;
    private double _bottom;

    /// <summary>Kullanıcı pencereyi sürükleyip bıraktığında (Left, Bottom) – kaydetmek için.</summary>
    public event Action<double, double>? PositionChanged;

    public bool HasText => _current.Length > 0;
    public string CurrentText => _current;

    public SubtitleWindow()
    {
        Title = "Altyazı+ – Altyazı";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Topmost = true;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000; // yerleştirilene kadar ekran dışında
        Top = -10000;

        _text = new TextBlock
        {
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2, Opacity = 0.9 }
        };
        _box = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24, 8, 24, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = _text,
            Visibility = Visibility.Collapsed
        };
        Content = new Grid { Children = { _box }, MinHeight = 10 };

        MouseLeftButtonDown += (_, _) =>
        {
            if (!_editMode || _coverMode) return;
            try { DragMove(); } catch { /* fare bırakıldı */ }
            _normalLeft = Left;
            _normalBottom = _bottom = Top + ActualHeight;
            PositionChanged?.Invoke(_normalLeft, _normalBottom);
        };
        SizeChanged += (_, _) => { if (_placed) PlaceVertically(); };
        SourceInitialized += (_, _) => ApplyWindowStyles();
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); _notice = ""; Render(); };
    }

    // ───────── Görünüm / yerleşim ─────────

    public void ApplyAppearance(double fontSize, double backgroundOpacity, double widthFraction, bool topmost)
    {
        _baseFontSize = Math.Clamp(fontSize, 16, 120);
        _bgOpacity = Math.Clamp(backgroundOpacity, 0, 1);
        RebuildBackground();
        Topmost = topmost;

        var wa = SystemParameters.WorkArea;
        double newWidth = Math.Clamp(widthFraction, 0.2, 1.0) * wa.Width;
        if (_placed)
        {
            double center = _normalLeft + _normalWidth / 2;
            _normalLeft = center - newWidth / 2;
        }
        _normalWidth = newWidth;
        ApplyLayout();
    }

    private void RebuildBackground()
    {
        byte a = (byte)Math.Round(_bgOpacity * 255);
        _userBackground = new SolidColorBrush(Color.FromArgb(a, _bgColor.R, _bgColor.G, _bgColor.B));
        if (!_coverActive) _box.Background = _userBackground;
    }

    /// <summary>Yazı rengi (null = oyunun rengi / beyaz), zemin rengi, yazı gölgesi, köşe yuvarlaklığı.</summary>
    public void ApplyStyle(Color? textColor, Color bgColor, bool shadow, double cornerRadius)
    {
        _userTextColor = textColor is { } c ? new SolidColorBrush(c) : null;
        _userTextColor?.Freeze();
        _bgColor = bgColor;
        _text.Effect = shadow ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2, Opacity = 0.9 } : null;
        _box.CornerRadius = new CornerRadius(Math.Clamp(cornerRadius, 0, 30));
        RebuildBackground();
        Render();
    }

    private bool _coverActive;

    /// <summary>Kayıtlı konum yoksa ekranın alt ortasına yerleştirir.</summary>
    public void Place(double? left, double? bottom)
    {
        var wa = SystemParameters.WorkArea;
        if (left is { } l && bottom is { } b && b > wa.Top + 50 && b <= wa.Bottom + 5 && l + _normalWidth > wa.Left + 50 && l < wa.Right - 50)
        {
            _normalLeft = l;
            _normalBottom = b;
        }
        else
        {
            _normalLeft = wa.Left + (wa.Width - _normalWidth) / 2;
            _normalBottom = wa.Bottom - wa.Height * 0.06;
        }
        _placed = true;
        ApplyLayout();
    }

    public void ResetPosition()
    {
        var wa = SystemParameters.WorkArea;
        _normalLeft = wa.Left + (wa.Width - _normalWidth) / 2;
        _normalBottom = wa.Bottom - wa.Height * 0.06;
        ApplyLayout();
        PositionChanged?.Invoke(_normalLeft, _normalBottom);
    }

    public void SetCoverMode(bool on)
    {
        _coverMode = on;
        ApplyWindowStyles();
        ApplyLayout();
    }

    private Brush? _gameColor;
    private double _gameEmPx;

    /// <summary>
    /// Oyunun altyazı yazısına benzetilen yazı tipi. <paramref name="emPx"/>: yazı boyu, oyun karesinin pikseli
    /// cinsinden (örtme + "orijinalle aynı boy" açıkken kullanılır). Null → varsayılan (Segoe UI Bold, beyaz).
    /// </summary>
    public void SetGameFont(FontFamily? family, bool bold, double emPx, Color? color)
    {
        _text.FontFamily = family ?? new FontFamily("Segoe UI");
        _text.FontWeight = family == null || bold ? FontWeights.Bold : FontWeights.Normal;
        _gameEmPx = family == null ? 0 : emPx;
        _gameColor = color is { } c ? new SolidColorBrush(c) : null;
        _gameColor?.Freeze();
        Render();
    }

    public void SetMatchOriginalSize(bool on)
    {
        _matchSize = on;
        Render();
    }

    /// <summary>İngilizce altyazının ekrandaki yeri (fiziksel piksel). Örtme modunda kutu buraya yerleşir.</summary>
    public void SetCoverRect(Rect? screenPx, double lineHeightPx = 0)
    {
        if (lineHeightPx > 0) _coverLineHeightPx = lineHeightPx;
        if (_coverPx is { } old && screenPx is { } nw &&
            Math.Abs(old.X - nw.X) < 3 && Math.Abs(old.Y - nw.Y) < 3 &&
            Math.Abs(old.Width - nw.Width) < 3 && Math.Abs(old.Height - nw.Height) < 3)
            return;
        _coverPx = screenPx;
        if (_coverMode) ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (!_placed) return;
        if (_coverMode && _coverPx is { } px)
        {
            double sx = 1, sy = 1;
            try { var dpi = VisualTreeHelper.GetDpi(this); sx = dpi.DpiScaleX; sy = dpi.DpiScaleY; } catch { }
            double cx = (px.X + px.Width / 2) / sx;
            double w = Math.Max(_normalWidth, px.Width / sx + 40);
            Width = w;
            Left = cx - w / 2;
            _bottom = (px.Y + px.Height) / sy + 6;
            _coverTop = px.Y / sy - 6;
            _box.MinWidth = px.Width / sx + 30;
            _box.MinHeight = px.Height / sy + 12;
            _coverActive = true;
            _box.Background = new SolidColorBrush(_bgColor); // tam örtücü: İngilizce alttan görünmesin
        }
        else
        {
            Width = _normalWidth;
            Left = _normalLeft;
            _bottom = _normalBottom;
            _box.MinWidth = 0;
            _box.MinHeight = 0;
            _coverActive = false;
            _box.Background = _userBackground;
        }
        _text.MaxWidth = Math.Max(100, Width - 60);
        PlaceVertically();
        Render();
    }

    private double _coverTop;

    /// <summary>
    /// Örtme modunda kutu İngilizce altyazının üst kenarından başlar; uzun çeviri aşağıya doğru satır satır iner
    /// (ekranın altından taşarsa yalnızca gerektiği kadar yukarı kayar). Normal modda alt kenar sabittir.
    /// </summary>
    private void PlaceVertically()
    {
        double h = Math.Max(ActualHeight, 10);
        if (_coverMode && _coverPx != null)
        {
            double screenBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
            Top = Math.Min(_coverTop, screenBottom - h - 4);
        }
        else Top = _bottom - h;
    }

    // ───────── Metin ─────────

    /// <summary>Türkçe altyazıyı gösterir; boş metin = gizle.</summary>
    public void ShowSubtitle(string? text)
    {
        _current = (text ?? "").Trim();
        Render();
    }

    /// <summary>F8: altyazıları sustur/aç (pencere açık kalır ki bildirimler görünsün).</summary>
    public void SetMuted(bool muted)
    {
        _muted = muted;
        Render();
    }

    /// <summary>Kısa bilgi (ör. "Çeviri durduruldu (F6)") – ~2 sn.</summary>
    public void ShowNotice(string message)
    {
        _notice = message;
        _noticeTimer.Stop();
        _noticeTimer.Start();
        Render();
    }

    public void SetEditMode(bool on)
    {
        _editMode = on;
        ApplyWindowStyles();
        Cursor = on && !_coverMode ? Cursors.SizeAll : null;
        Render();
    }

    private void Render()
    {
        bool showSub = _current.Length > 0 && !_muted;
        string shown = showSub ? _current
                     : _notice.Length > 0 ? _notice
                     : _editMode ? (_coverMode ? "Örtme modu: kutu oyunun İngilizce altyazısının üstüne kendiliğinden yerleşir"
                                                : "Türkçe altyazı burada görünecek — sürükleyerek taşıyın")
                     : "";
        if (shown.Length == 0)
        {
            _box.Visibility = Visibility.Collapsed;
            return;
        }

        bool isNotice = !showSub && _notice.Length > 0;
        _text.Text = shown;
        _text.Foreground = isNotice ? Brushes.Gold : (_userTextColor ?? _gameColor ?? Brushes.White);
        _text.FontSize = isNotice ? Math.Max(16, _baseFontSize * 0.55) : FitFontSize(shown, CurrentBaseSize());
        _box.BorderBrush = _editMode ? Brushes.Gold : null;
        _box.BorderThickness = new Thickness(_editMode ? 2 : 0);
        _box.Visibility = Visibility.Visible;
    }

    /// <summary>Örtme modunda ve "orijinalle aynı boy" açıkken İngilizce satır yüksekliğinden, değilse ayardan.</summary>
    private double CurrentBaseSize()
    {
        if (_matchSize && (_gameEmPx > 0 || (_coverMode && _coverPx != null && _coverLineHeightPx > 0)))
        {
            double sy = 1;
            try { sy = VisualTreeHelper.GetDpi(this).DpiScaleY; } catch { }
            // Oyunun yazı tipi ölçüldüyse onun boyu (harfler orijinalle aynı yükseklikte), değilse satır yüksekliğinden
            double px = _gameEmPx > 0 ? _gameEmPx : _coverLineHeightPx * 0.95;
            return Math.Clamp(px / sy, 10, 140);
        }
        return _baseFontSize;
    }

    /// <summary>
    /// Yazı küçültülmez: altyazı her zaman orijinal boyunda kalır, sığmayan metin alt satırlara kayar (satır sınırı yok).
    /// </summary>
    private static double FitFontSize(string text, double baseSize) => baseSize;

    // ───────── Tıklama geçirme (Remote Play'i engellememek için) ─────────

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    private void ApplyWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        if (_editMode && !_coverMode) ex &= ~WS_EX_TRANSPARENT;
        else ex |= WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }
}
