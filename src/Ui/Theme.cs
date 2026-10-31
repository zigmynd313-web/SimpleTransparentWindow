using System.Runtime.InteropServices;
using SimpleTransparentWindow.Native;

namespace SimpleTransparentWindow.Ui;

internal static class Theme
{

    public const uint PageBg = 0xFF0B0B0C;
    public const uint CardBg = 0xFF0E0E0F;
    public const uint CardBorder = 0xFF2A2A2C;

    public const uint TitleText = 0xFFEDEDED;
    public const uint LabelText = 0xFFD4D4D6;
    public const uint MutedText = 0xFF8A8A8E;

    public const uint FieldBg = 0xFF1A1A1C;
    public const uint FieldBorder = 0xFF2E2E31;
    public const uint FieldBorderFocus = 0xFF6C6C72;
    public const uint FieldText = 0xFFECECEE;
    public const uint SelectionBg = 0xFF2D5B8E;

    public const uint TrackBg = 0xFF4A4A4E;
    public const uint TrackFill = 0xFFB8B8BE;
    public const uint Knob = 0xFFFFFFFF;

    public const uint PillBg = 0xFF26262A;
    public const uint PillText = 0xFFE4E4E8;

    public const uint Divider = 0xFF262629;

    public const uint CheckOnBg = 0xFFFFFFFF;
    public const uint CheckOnMark = 0xFF111113;
    public const uint CheckOffBg = 0xFF1A1A1C;
    public const uint CheckOffBorder = 0xFF3C3C40;

    public const uint BtnBg = 0xFF1E1E21;
    public const uint BtnBgHover = 0xFF2B2B30;
    public const uint BtnBgPress = 0xFF35353B;
    public const uint BtnText = 0xFFE2E2E6;

    public const uint BtnPrimaryBg = 0xFF1B2734;
    public const uint BtnPrimaryHover = 0xFF24344A;
    public const uint BtnPrimaryPress = 0xFF2C4059;
    public const uint BtnPrimaryText = 0xFFA9C7E3;

    public const uint CloseGlyph = 0xFF7C7C82;
    public const uint CloseGlyphHover = 0xFFFFFFFF;
    public const uint CloseHoverBg = 0xFF2A2A2E;

    public const uint MenuBg = 0xFF1A1A1C;
    public const uint MenuBorder = 0xFF303034;
    public const uint MenuHover = 0xFF2C2C31;
    public const uint MenuText = 0xFFEDEDED;
    public const uint MenuTextMuted = 0xFF8A8A8E;
    public const uint MenuSeparator = 0xFF2E2E32;
    public const uint MenuDangerHover = 0xFF3B2326;
    public const uint MenuDangerText = 0xFFE89A9A;

    private static double _scale;
    private static IntPtr _title, _label, _value, _hint, _button, _menu;

    public static IntPtr FTitle => _title;
    public static IntPtr FLabel => _label;
    public static IntPtr FValue => _value;
    public static IntPtr FHint => _hint;
    public static IntPtr FButton => _button;
    public static IntPtr FMenu => _menu;

    public static void Ensure(double scale)
    {
        if (Math.Abs(scale - _scale) < 0.01 && _title != IntPtr.Zero) return;
        _scale = scale;

        Destroy();
        _title = Make(scale, 17, 600);
        _label = Make(scale, 17, 400);
        _value = Make(scale, 17, 400);
        _hint = Make(scale, 14, 400);
        _button = Make(scale, 15, 400);
        _menu = Make(scale, 13, 400);
    }

    private static IntPtr Make(double scale, int px, int weight)
    {
        var lf = new N.LOGFONTW
        {
            lfHeight = -(int)Math.Round(px * scale),
            lfWidth = 0,
            lfWeight = weight,
            lfCharSet = 1,
            lfQuality = 4,
            lfPitchAndFamily = 0,
            lfFaceName = "Segoe UI",
        };
        return N.CreateFontIndirectW(ref lf);
    }

    private static void Destroy()
    {
        foreach (var f in new[] { _title, _label, _value, _hint, _button, _menu })
            if (f != IntPtr.Zero) N.DeleteObject(f);

        _title = _label = _value = _hint = _button = _menu = IntPtr.Zero;
    }

    private static uint ToColorRef(uint argb)
        => ((argb & 0x000000FF) << 16) | (argb & 0x0000FF00) | ((argb >> 16) & 0x000000FF);

    public static void Text(IntPtr hdc, IntPtr font, string text, N.RECT rect, uint color, uint bg, uint flags)
    {
        var old = N.SelectObject(hdc, font);
        N.SetTextColor(hdc, ToColorRef(color));

        N.SetBkColor(hdc, ToColorRef(bg));
        N.SetBkMode(hdc, N.OPAQUE);
        N.DrawTextW(hdc, text, -1, ref rect, flags | N.DT_NOPREFIX);
        N.SelectObject(hdc, old);
    }

    public static void TextTransparent(IntPtr hdc, IntPtr font, string text, N.RECT rect, uint color, uint flags)
    {
        var old = N.SelectObject(hdc, font);
        N.SetTextColor(hdc, ToColorRef(color));
        N.SetBkMode(hdc, 1);
        N.DrawTextW(hdc, text, -1, ref rect, flags | N.DT_NOPREFIX);
        N.SelectObject(hdc, old);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetTextExtentPoint32W(IntPtr hdc, string s, int len, out SIZE size);

    public static int MeasureWidth(IntPtr hdc, IntPtr font, string text)
    {
        var old = N.SelectObject(hdc, font);
        bool ok = GetTextExtentPoint32W(hdc, text, text.Length, out var size);
        N.SelectObject(hdc, old);
        return ok ? size.cx : text.Length * 8;
    }
}
