using System.Runtime.InteropServices;
using SimpleTransparentWindow.Native;
using SimpleTransparentWindow.Ui;

namespace SimpleTransparentWindow;

internal static class PopupMenu
{
    private const string ClassName = "SimpleTransparentWindowMenu";
    private const uint WsPopup = 0x80000000;

    private const int Width = 182;
    private const int ItemH = 29;
    private const int SepH = 5;
    private const int PadV = 4;
    private const int Radius = 12;

    internal readonly record struct Item(string? Text, int Id, bool Checked, bool Danger = false)
    {
        public static Item Separator => new(null, 0, false);
    }

    private static IntPtr _hwnd = IntPtr.Zero;
    private static N.WndProcDelegate? _wndProc;

    private static Item[] _items = Array.Empty<Item>();
    private static Action<int>? _onSelect;

    private static double _scale = 1.0;
    private static int _hover = -1;
    private static int _pressed = -1;
    private static bool _closing;
    private static bool _activated;

    private static int S(int v) => (int)Math.Round(v * _scale);

    private static int ItemHeight(Item it) => it.Text is null ? S(SepH) : S(ItemH);

    public static bool IsOpen => _hwnd != IntPtr.Zero && N.IsWindow(_hwnd);

    public static void Show(IntPtr owner, Item[] items, Action<int> onSelect)
    {

        if (IsOpen) return;

        try { _scale = N.GetDpiForSystem() / 96.0; }
        catch { _scale = 1.0; }
        if (_scale <= 0) _scale = 1.0;

        Theme.Ensure(_scale);

        _items = items;
        _onSelect = onSelect;
        _hover = -1;
        _pressed = -1;
        _closing = false;
        _activated = false;

        if (_wndProc is null)
        {
            _wndProc = WndProc;

            var wc = new N.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<N.WNDCLASSEXW>(),
                lpfnWndProc = _wndProc,
                hInstance = N.GetModuleHandleW(null),
                hCursor = N.LoadCursorW(IntPtr.Zero, (IntPtr)N.IDC_ARROW),
                hbrBackground = IntPtr.Zero,
                lpszClassName = ClassName,
            };

            if (N.RegisterClassExW(ref wc) == 0)
            {
                Logger.Log($"RegisterClassExW(menu) failed err={Marshal.GetLastWin32Error()}", "ERROR");
                return;
            }
        }

        int w = S(Width);
        int h = S(PadV * 2);
        foreach (var it in _items) h += ItemHeight(it);

        N.GetCursorPos(out var pt);

        var work = new N.RECT
        {
            Left = 0,
            Top = 0,
            Right = N.GetSystemMetrics(N.SM_CXSCREEN),
            Bottom = N.GetSystemMetrics(N.SM_CYSCREEN),
        };
        N.SystemParametersInfoW(N.SPI_GETWORKAREA, 0, ref work, 0);

        int pad = S(8);
        int x = pt.X;

        int screenH = N.GetSystemMetrics(N.SM_CYSCREEN);
        int bottomEdge = work.Bottom;

        var shellTray = N.FindWindowW("Shell_TrayWnd", null);
        if (shellTray != IntPtr.Zero && N.GetWindowRect(shellTray, out var tb))
        {
            bool taskbarHidden = tb.Top >= screenH - 2;
            bottomEdge = taskbarHidden ? screenH : tb.Top;
        }

        int y = bottomEdge - h;
        if (y < work.Top + pad) y = work.Top + pad;

        int minX = work.Left + pad;
        int maxX = Math.Max(minX, work.Right - w - pad);
        int minY = work.Top + pad;
        int maxY = Math.Max(minY, bottomEdge - h);

        x = Math.Clamp(x, minX, maxX);
        y = Math.Clamp(y, minY, maxY);

        _hwnd = N.CreateWindowExW(N.WS_EX_TOPMOST | N.WS_EX_TOOLWINDOW, ClassName, "SimpleTransparentWindow",
            WsPopup | N.WS_THICKFRAME, x, y, w, h, owner, IntPtr.Zero,
            N.GetModuleHandleW(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            Logger.Log($"CreateWindowExW(menu) failed err={Marshal.GetLastWin32Error()}", "ERROR");
            return;
        }

        try
        {
            int dark = 1;
            N.DwmSetWindowAttribute(_hwnd, N.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            int corner = N.DWMWCP_ROUND;
            N.DwmSetWindowAttribute(_hwnd, N.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            int border = 0x343030;
            N.DwmSetWindowAttribute(_hwnd, N.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch { }

        N.ShowWindow(_hwnd, N.SW_SHOW);

        N.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            N.SWP_FRAMECHANGED | N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOZORDER | N.SWP_NOACTIVATE);

        N.SetForegroundWindow(_hwnd);
        N.SetFocus(_hwnd);
        N.SetCapture(_hwnd);
        N.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private static void Close()
    {
        if (_hwnd == IntPtr.Zero || _closing) return;

        _closing = true;
        var hwnd = _hwnd;
        _hwnd = IntPtr.Zero;

        N.ReleaseCapture();
        N.DestroyWindow(hwnd);
    }

    private static void Invoke(int id)
    {
        var cb = _onSelect;
        _onSelect = null;

        cb?.Invoke(id);

        Close();
    }

    private static int HitTest(int x, int y)
    {
        int top = S(PadV);
        for (int i = 0; i < _items.Length; i++)
        {
            int h = ItemHeight(_items[i]);
            if (_items[i].Text is not null && y >= top && y < top + h) return i;
            top += h;
        }
        return -1;
    }

    private static void Paint(IntPtr hdc)
    {
        N.GetClientRect(_hwnd, out var rc);
        int w = rc.Right, h = rc.Bottom;

        using (var c = new Canvas(hdc))
        {
            c.FillRect(0, 0, w, h, Theme.MenuBg);

            int top = S(PadV);
            for (int i = 0; i < _items.Length; i++)
            {
                var it = _items[i];
                int ih = ItemHeight(it);

                if (it.Text is null)
                {

                    int ly = top + ih / 2;
                    c.Line(S(12), ly, w - S(12), ly, Theme.MenuSeparator, Math.Max(1, S(1)));
                    top += ih;
                    continue;
                }

                if (i == _hover)
                {
                    uint hoverBg = it.Danger ? Theme.MenuDangerHover : Theme.MenuHover;
                    c.FillRound(S(4), top + S(1), w - S(8), ih - S(2), S(5), hoverBg);
                }

                top += ih;
            }
        }

        int y2 = S(PadV);
        for (int i = 0; i < _items.Length; i++)
        {
            var it = _items[i];
            int ih = ItemHeight(it);

            if (it.Text is not null)
            {
                var textRect = new N.RECT
                {
                    Left = S(12),
                    Top = y2,
                    Right = w - S(34),
                    Bottom = y2 + ih,
                };

                uint txtBg = i == _hover
                    ? (it.Danger ? Theme.MenuDangerHover : Theme.MenuHover)
                    : Theme.MenuBg;

                Theme.Text(hdc, Theme.FMenu, it.Text, textRect,
                    it.Danger && i == _hover ? Theme.MenuDangerText : Theme.MenuText,
                    txtBg, N.DT_LEFT | N.DT_VCENTER | N.DT_SINGLELINE);

                if (it.Checked)
                {
                    using var c = new Canvas(hdc);
                    int cx = w - S(18);
                    int cy = y2 + ih / 2;
                    int u = Math.Max(2, S(3));
                    int pw = Math.Max(2, S(2));

                    c.Line(cx - u * 2, cy, cx - u / 2, cy + u * 2, 0xFFFFFFFF, pw);
                    c.Line(cx - u / 2, cy + u * 2, cx + u * 2, cy - u * 2, 0xFFFFFFFF, pw);
                }
            }

            y2 += ih;
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case N.WM_NCCALCSIZE when wParam != IntPtr.Zero:
                return IntPtr.Zero;

            case N.WM_NCPAINT:

                var dc = N.GetWindowDC(hWnd);
                if (dc != IntPtr.Zero)
                {
                    if (N.GetWindowRect(hWnd, out var wr))
                    {
                        var full = new N.RECT
                        {
                            Left = 0,
                            Top = 0,
                            Right = wr.Right - wr.Left,
                            Bottom = wr.Bottom - wr.Top,
                        };
                        var brush = N.CreateSolidBrush(Theme.MenuBg);
                        N.FillRect(dc, ref full, brush);
                        N.DeleteObject(brush);
                    }
                    N.ReleaseDC(hWnd, dc);
                }
                return IntPtr.Zero;

            case N.WM_ERASEBKGND:
                return new IntPtr(1);

            case N.WM_PRINTCLIENT:
                Paint(wParam);
                return IntPtr.Zero;

            case N.WM_PAINT:
            {
                var hdc = N.BeginPaint(hWnd, out var ps);
                N.GetClientRect(hWnd, out var rc);

                var mem = N.CreateCompatibleDC(hdc);
                var bmp = N.CreateCompatibleBitmap(hdc, rc.Right, rc.Bottom);
                var old = N.SelectObject(mem, bmp);

                Paint(mem);

                N.BitBlt(hdc, 0, 0, rc.Right, rc.Bottom, mem, 0, 0, N.SRCCOPY);

                N.SelectObject(mem, old);
                N.DeleteObject(bmp);
                N.DeleteDC(mem);
                N.EndPaint(hWnd, ref ps);
                return IntPtr.Zero;
            }

            case N.WM_MOUSEMOVE:
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

                int hot = HitTest(x, y);
                if (hot != _hover)
                {
                    _hover = hot;
                    N.InvalidateRect(hWnd, IntPtr.Zero, false);
                }
                return IntPtr.Zero;
            }

            case N.WM_LBUTTONDOWN:
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

                _pressed = HitTest(x, y);
                if (_pressed < 0) { Close(); return IntPtr.Zero; }

                N.InvalidateRect(hWnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            }

            case N.WM_LBUTTONUP:
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

                int hot = HitTest(x, y);
                int pressed = _pressed;
                _pressed = -1;

                if (hot >= 0 && hot == pressed) Invoke(_items[hot].Id);
                else if (hot < 0) Close();

                return IntPtr.Zero;
            }

            case N.WM_KEYDOWN:
                if ((int)wParam.ToInt64() == N.VK_ESCAPE) { Close(); return IntPtr.Zero; }
                break;

            case N.WM_ACTIVATE:

                if (N.LoWord(wParam) == 0)
                {
                    if (_activated) Close();
                }
                else
                {
                    _activated = true;
                }
                return IntPtr.Zero;

            case N.WM_CAPTURECHANGED:

                return IntPtr.Zero;

            case N.WM_DESTROY:
                if (_hwnd == hWnd) _hwnd = IntPtr.Zero;
                return IntPtr.Zero;
        }

        return N.DefWindowProcW(hWnd, msg, wParam, lParam);
    }
}
