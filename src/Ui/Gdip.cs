using System.Runtime.InteropServices;

namespace SimpleTransparentWindow.Ui;

internal static class Gdip
{
    private static IntPtr _token;
    private static bool _started;

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInput
    {
        public uint GdiplusVersion;
        public IntPtr DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [DllImport("gdiplus.dll")]
    private static extern int GdiplusStartup(out IntPtr token, ref StartupInput input, IntPtr output);

    [DllImport("gdiplus.dll")]
    private static extern void GdiplusShutdown(IntPtr token);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateFromHDC(IntPtr hdc, out IntPtr graphics);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteGraphics(IntPtr graphics);

    [DllImport("gdiplus.dll")]
    public static extern int GdipSetSmoothingMode(IntPtr graphics, int mode);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateSolidFill(uint argb, out IntPtr brush);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteBrush(IntPtr brush);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreatePath(int fillMode, out IntPtr path);

    [DllImport("gdiplus.dll")]
    public static extern int GdipAddPathArc(IntPtr path, float x, float y, float w, float h,
                                            float startAngle, float sweepAngle);

    [DllImport("gdiplus.dll")]
    public static extern int GdipClosePathFigure(IntPtr path);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeletePath(IntPtr path);

    [DllImport("gdiplus.dll")]
    public static extern int GdipFillPath(IntPtr graphics, IntPtr brush, IntPtr path);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreatePen1(uint argb, float width, int unit, out IntPtr pen);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeletePen(IntPtr pen);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDrawPath(IntPtr graphics, IntPtr pen, IntPtr path);

    [DllImport("gdiplus.dll")]
    public static extern int GdipFillEllipse(IntPtr graphics, IntPtr brush,
                                             float x, float y, float w, float h);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDrawLine(IntPtr graphics, IntPtr pen,
                                          float x1, float y1, float x2, float y2);

    [DllImport("gdiplus.dll")]
    public static extern int GdipFillRectangle(IntPtr graphics, IntPtr brush,
                                               float x, float y, float w, float h);

    public static void Start()
    {
        if (_started) return;
        var input = new StartupInput { GdiplusVersion = 1 };
        _started = GdiplusStartup(out _token, ref input, IntPtr.Zero) == 0;
    }

    public static void Stop()
    {
        if (!_started) return;
        GdiplusShutdown(_token);
        _started = false;
    }
}

internal sealed class Canvas : IDisposable
{
    private const int SmoothingAntiAlias = 4;
    private const int UnitWorld = 0;

    private IntPtr _g;
    private bool _ok;

    public Canvas(IntPtr hdc)
    {
        Gdip.Start();
        _ok = Gdip.GdipCreateFromHDC(hdc, out _g) == 0;
        if (_ok) Gdip.GdipSetSmoothingMode(_g, SmoothingAntiAlias);
    }

    public bool Ok => _ok;

    public void Dispose()
    {
        if (_g != IntPtr.Zero)
        {
            Gdip.GdipDeleteGraphics(_g);
            _g = IntPtr.Zero;
        }
    }

    private static IntPtr RoundPath(int x, int y, int w, int h, int radius)
    {
        Gdip.GdipCreatePath(0, out var path);

        int r = Math.Min(radius, Math.Min(w, h) / 2);
        if (r <= 0)
        {
            Gdip.GdipClosePathFigure(path);
            return path;
        }

        float d = r * 2f;
        Gdip.GdipAddPathArc(path, x, y, d, d, 180, 90);
        Gdip.GdipAddPathArc(path, x + w - d, y, d, d, 270, 90);
        Gdip.GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90);
        Gdip.GdipAddPathArc(path, x, y + h - d, d, d, 90, 90);
        Gdip.GdipClosePathFigure(path);
        return path;
    }

    public void FillRound(int x, int y, int w, int h, int radius, uint argb)
    {
        if (!_ok || w <= 0 || h <= 0) return;

        var path = RoundPath(x, y, w, h, radius);
        if (Gdip.GdipCreateSolidFill(argb, out var brush) == 0)
        {
            Gdip.GdipFillPath(_g, brush, path);
            Gdip.GdipDeleteBrush(brush);
        }
        Gdip.GdipDeletePath(path);
    }

    public void StrokeRound(int x, int y, int w, int h, int radius, uint argb, int width = 1)
    {
        if (!_ok || w <= 0 || h <= 0) return;

        var path = RoundPath(x, y, w, h, radius);
        if (Gdip.GdipCreatePen1(argb, width, UnitWorld, out var pen) == 0)
        {
            Gdip.GdipDrawPath(_g, pen, path);
            Gdip.GdipDeletePen(pen);
        }
        Gdip.GdipDeletePath(path);
    }

    public void FillCircle(int cx, int cy, int radius, uint argb)
    {
        if (!_ok || radius <= 0) return;
        if (Gdip.GdipCreateSolidFill(argb, out var brush) != 0) return;

        Gdip.GdipFillEllipse(_g, brush, cx - radius, cy - radius, radius * 2, radius * 2);
        Gdip.GdipDeleteBrush(brush);
    }

    public void FillRect(int x, int y, int w, int h, uint argb)
    {
        if (!_ok || w <= 0 || h <= 0) return;
        if (Gdip.GdipCreateSolidFill(argb, out var brush) != 0) return;

        Gdip.GdipFillRectangle(_g, brush, x, y, w, h);
        Gdip.GdipDeleteBrush(brush);
    }

    public void Line(int x1, int y1, int x2, int y2, uint argb, int width = 1)
    {
        if (!_ok) return;
        if (Gdip.GdipCreatePen1(argb, width, UnitWorld, out var pen) != 0) return;

        Gdip.GdipDrawLine(_g, pen, x1, y1, x2, y2);
        Gdip.GdipDeletePen(pen);
    }
}
