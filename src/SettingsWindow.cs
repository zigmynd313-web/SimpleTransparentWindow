using System.Runtime.InteropServices;
using SimpleTransparentWindow.Native;
using SimpleTransparentWindow.Ui;

namespace SimpleTransparentWindow;

internal static class SettingsWindow
{
    private const string ClassName = "SimpleTransparentWindowSettings";
    private const uint WsPopup = 0x80000000;

    private const int CardW = 500;
    private const int CardH = 417;
    private const int Radius = 18;

    private const double Ui = 0.85;

    private enum Hot { None, Close, Reset, Ok, Cancel, Check, Track, FieldDelay, FieldTransition }
    private enum Field { None, Delay, Transition }

    private static IntPtr _hwnd = IntPtr.Zero;
    private static N.WndProcDelegate? _wndProc;

    private static double _scale = 1.0;
    private static int _opacity = 80;
    private static string _delayText = "150";
    private static string _transitionText = "180";

    private static Hot _hover = Hot.None;
    private static Hot _pressed = Hot.None;
    private static Field _focus = Field.None;
    private static bool _draggingSlider;
    private static long _checkAnimStart;
    private static bool _checkRemoving;
    private static bool _checkVisualOn = true;
    private static bool _caretOn = true;
    private static int _selStart;
    private static int _selEnd;
    private static bool _selecting;
    private static long _savedAt;

    private static int S(int v) => (int)Math.Round(v * _scale * Ui);

    private static N.RECT R(int x, int y, int w, int h) => new()
    {
        Left = S(x),
        Top = S(y),
        Right = S(x + w),
        Bottom = S(y + h),
    };

    private static N.RECT FieldDelay => R(354, 54, 85, 37);
    private static N.RECT FieldTransition => R(354, 182, 85, 37);
    private static N.RECT TrackRect => R(241, 130, 164, 12);
    private static N.RECT TrackHit => R(231, 114, 184, 44);
    private static N.RECT ValuePill => R(420, 118, 56, 37);
    private static N.RECT CheckRect => R(444, 268, 32, 32);
    private static N.RECT BtnReset => R(24, 356, 140, 35);
    private static N.RECT BtnOk => R(180, 356, 140, 35);
    private static N.RECT BtnCancel => R(336, 356, 140, 35);
    private static N.RECT CloseRect => R(456, 13, 30, 30);

    private static int KnobX
    {
        get
        {
            var t = TrackRect;
            double f = (_opacity - Config.OpacityMin) / (double)(Config.OpacityMax - Config.OpacityMin);
            int pad = S(6);
            return (int)Math.Round(t.Left + pad + (t.Right - t.Left - pad * 2) * f);
        }
    }

    public static IntPtr Open(IntPtr hInstance, Config cfg)
    {
        if (_hwnd != IntPtr.Zero && N.IsWindow(_hwnd))
        {
            N.ShowWindow(_hwnd, N.SW_SHOW);
            N.SetForegroundWindow(_hwnd);
            return _hwnd;
        }

        try { _scale = N.GetDpiForSystem() / 96.0; }
        catch { _scale = 1.0; }
        if (_scale <= 0) _scale = 1.0;

        Theme.Ensure(_scale);

        if (_wndProc is null)
        {
            _wndProc = WndProc;

            var wc = new N.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<N.WNDCLASSEXW>(),
                style = N.CS_HREDRAW | N.CS_VREDRAW | N.CS_DBLCLKS,
                lpfnWndProc = _wndProc,
                hInstance = hInstance,
                hIcon = AppIcon.Large,
                hCursor = N.LoadCursorW(IntPtr.Zero, (IntPtr)N.IDC_ARROW),
                hbrBackground = IntPtr.Zero,
                lpszClassName = ClassName,
                hIconSm = AppIcon.Small,
            };

            if (N.RegisterClassExW(ref wc) == 0)
            {
                Logger.Log($"RegisterClassExW(settings) failed err={Marshal.GetLastWin32Error()}", "ERROR");
                return IntPtr.Zero;
            }
        }

        int w = S(CardW);
        int h = S(CardH);
        int x = (N.GetSystemMetrics(N.SM_CXSCREEN) - w) / 2;
        int y = (N.GetSystemMetrics(N.SM_CYSCREEN) - h) / 2;

        _hwnd = N.CreateWindowExW(N.WS_EX_APPWINDOW, ClassName, "SimpleTransparentWindow — настройки",
            WsPopup, x, y, w, h,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            Logger.Log($"CreateWindowExW(settings) failed err={Marshal.GetLastWin32Error()}", "ERROR");
            return IntPtr.Zero;
        }

        ApplyDwm(_hwnd);

        N.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            N.SWP_FRAMECHANGED | N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOZORDER | N.SWP_NOACTIVATE);

        N.ShowWindow(_hwnd, N.SW_SHOW);
        N.SetForegroundWindow(_hwnd);
        N.SetFocus(_hwnd);
        return _hwnd;
    }

    private static void ApplyDwm(IntPtr hwnd)
    {
        try
        {
            int dark = 1;
            N.DwmSetWindowAttribute(hwnd, N.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            int corner = N.DWMWCP_ROUND;
            N.DwmSetWindowAttribute(hwnd, N.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            int border = 0x2C2A2A;
            N.DwmSetWindowAttribute(hwnd, N.DWMWA_BORDER_COLOR, ref border, sizeof(int));

            var margins = new N.MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            N.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch { }
    }

    public static void RequestClose()
    {
        if (_hwnd != IntPtr.Zero) N.PostMessageW(_hwnd, N.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    public static void RequestSave()
    {
        if (_hwnd == IntPtr.Zero) return;
        Apply();
        MarkSaved();
    }

    public static void CycleFocus()
    {
        if (_hwnd == IntPtr.Zero) return;
        _focus = _focus == Field.Delay ? Field.Transition : Field.Delay;
        _selStart = _selEnd = FieldText(_focus).Length;
        _caretOn = true;
        N.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private static void PaintFrameArea(IntPtr hWnd, uint color)
    {
        var dc = N.GetWindowDC(hWnd);
        if (dc == IntPtr.Zero) return;

        if (N.GetWindowRect(hWnd, out var wr))
        {
            var full = new N.RECT
            {
                Left = 0,
                Top = 0,
                Right = wr.Right - wr.Left,
                Bottom = wr.Bottom - wr.Top,
            };

            var brush = N.CreateSolidBrush(color);
            N.FillRect(dc, ref full, brush);
            N.DeleteObject(brush);
        }

        N.ReleaseDC(hWnd, dc);
    }

    private static void Paint(IntPtr hdc)
    {
        N.GetClientRect(_hwnd, out var client);
        int w = client.Right, h = client.Bottom;

        const string title = "Настройки";
        int titleW = Theme.MeasureWidth(hdc, Theme.FTitle, title);
        int startX = (w - titleW) / 2;

        using (var c = new Canvas(hdc))
        {
            c.FillRect(0, 0, w, h, Theme.CardBg);

            DrawField(c, FieldDelay, _focus == Field.Delay);
            DrawField(c, FieldTransition, _focus == Field.Transition);

            DrawSlider(c);
            c.FillRound(ValuePill.Left, ValuePill.Top, ValuePill.Right - ValuePill.Left,
                ValuePill.Bottom - ValuePill.Top, S(10), Theme.PillBg);

            DrawCheckbox(c);
            DrawClose(c);

            c.Line(S(24), S(244), S(476), S(244), Theme.Divider, Math.Max(1, S(1)));
        }

        var titleRect = new N.RECT
        {
            Left = startX,
            Top = S(8),
            Right = startX + titleW + S(4),
            Bottom = S(48),
        };
        Theme.Text(hdc, Theme.FTitle, title, titleRect, Theme.TitleText, Theme.CardBg,
            N.DT_LEFT | N.DT_VCENTER | N.DT_SINGLELINE);

        Label(hdc, "Задержка перед затемнением:", R(24, 58, 320, 28));
        Label(hdc, "Прозрачность окна:", R(24, 122, 240, 28));
        Label(hdc, "Длительность анимации:", R(24, 186, 320, 28));
        Label(hdc, "Запускать при входе в Windows", R(24, 270, 410, 28));

        Theme.Text(hdc, Theme.FHint, "мс", R(455, 58, 26, 28), Theme.MutedText, Theme.CardBg,
            N.DT_LEFT | N.DT_VCENTER | N.DT_SINGLELINE);
        Theme.Text(hdc, Theme.FHint, "мс", R(455, 186, 26, 28), Theme.MutedText, Theme.CardBg,
            N.DT_LEFT | N.DT_VCENTER | N.DT_SINGLELINE);

        Theme.Text(hdc, Theme.FValue, _opacity + " %", ValuePill, Theme.PillText, Theme.PillBg,
            N.DT_CENTER | N.DT_VCENTER | N.DT_SINGLELINE);

        if (_savedAt != 0 && Environment.TickCount64 - _savedAt < 1800)
        {
            Theme.Text(hdc, Theme.FHint, "Сохранено", R(24, 320, 452, 26),
                0xFF7FD1A0, Theme.CardBg, N.DT_CENTER | N.DT_VCENTER | N.DT_SINGLELINE);
        }
        else
        {
            Theme.Text(hdc, Theme.FHint, "Изменения применяются сразу, перезапуск не нужен.",
                R(24, 320, 452, 26), Theme.MutedText, Theme.CardBg,
                N.DT_CENTER | N.DT_VCENTER | N.DT_SINGLELINE);
        }

        DrawFieldText(hdc, Field.Delay, _delayText);
        DrawFieldText(hdc, Field.Transition, _transitionText);

        Btn(hdc, "По умолчанию", BtnReset, false, _hover == Hot.Reset, _pressed == Hot.Reset);
        Btn(hdc, "Сохранить", BtnOk, true, _hover == Hot.Ok, _pressed == Hot.Ok);
        Btn(hdc, "Отмена", BtnCancel, false, _hover == Hot.Cancel, _pressed == Hot.Cancel);
    }

    private static void DrawFieldText(IntPtr hdc, Field field, string text)
    {
        var rect = field == Field.Delay ? FieldDelay : FieldTransition;
        var inner = new N.RECT
        {
            Left = rect.Left + S(8),
            Top = rect.Top,
            Right = rect.Right - S(8),
            Bottom = rect.Bottom,
        };

        bool focused = _focus == field;
        int lo = Math.Clamp(Math.Min(_selStart, _selEnd), 0, text.Length);
        int hi = Math.Clamp(Math.Max(_selStart, _selEnd), 0, text.Length);
        bool hasSel = focused && hi > lo;

        int total = Theme.MeasureWidth(hdc, Theme.FValue, text);
        int centerX = (inner.Left + inner.Right) / 2;
        int startX = centerX - total / 2;

        int X(int index) => startX + Theme.MeasureWidth(hdc, Theme.FValue, text[..Math.Clamp(index, 0, text.Length)]);

        if (hasSel)
        {
            int padX = S(4);
            int padY = S(7);
            int x0 = X(lo) - padX / 2;
            int x1 = X(hi) + padX / 2;

            using (var c = new Canvas(hdc))
            {
                c.FillRound(x0, rect.Top + padY, Math.Max(S(2), x1 - x0),
                    (rect.Bottom - rect.Top) - padY * 2, S(4), Theme.SelectionBg);
            }

            Theme.TextTransparent(hdc, Theme.FValue, text, inner, Theme.FieldText,
                N.DT_CENTER | N.DT_VCENTER | N.DT_SINGLELINE);
        }
        else
        {
            Theme.Text(hdc, Theme.FValue, text, inner, Theme.FieldText, Theme.FieldBg,
                N.DT_CENTER | N.DT_VCENTER | N.DT_SINGLELINE);
        }

        if (!focused || !_caretOn || hasSel) return;

        int caretX = X(_selEnd);
        int half = S(8);

        using var cv = new Canvas(hdc);
        cv.Line(caretX, (rect.Top + rect.Bottom) / 2 - half,
                caretX, (rect.Top + rect.Bottom) / 2 + half, 0xFFE8E8EE, Math.Max(1, S(1)));
    }

    private static void Label(IntPtr hdc, string text, N.RECT r)
        => Theme.Text(hdc, Theme.FLabel, text, r, Theme.LabelText, Theme.CardBg,
            N.DT_LEFT | N.DT_VCENTER | N.DT_SINGLELINE);

    private static void Btn(IntPtr hdc, string text, N.RECT r, bool primary, bool hover, bool pressed)
    {
        uint bg = primary
            ? (pressed ? Theme.BtnPrimaryPress : hover ? Theme.BtnPrimaryHover : Theme.BtnPrimaryBg)
            : (pressed ? Theme.BtnBgPress : hover ? Theme.BtnBgHover : Theme.BtnBg);

        using (var c = new Canvas(hdc))
            c.FillRound(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, S(12), bg);

        Theme.Text(hdc, Theme.FButton, text, r,
            primary ? Theme.BtnPrimaryText : Theme.BtnText, bg,
            N.DT_CENTER | N.DT_VCENTER | N.DT_SINGLELINE);
    }

    private static void DrawField(Canvas c, N.RECT r, bool focused)
    {
        int rad = S(10);
        c.FillRound(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, rad, Theme.FieldBg);
        c.StrokeRound(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, rad,
            focused ? Theme.FieldBorderFocus : Theme.FieldBorder, Math.Max(1, S(1)));
    }

    private static void DrawSlider(Canvas c)
    {
        var t = TrackRect;
        int barH = Math.Max(4, S(6));
        int cy = (t.Top + t.Bottom) / 2;
        int left = t.Left + S(6);
        int right = t.Right - S(6);
        int knobX = KnobX;

        c.FillRound(left, cy - barH / 2, right - left, barH, barH / 2, Theme.TrackBg);
        c.FillRound(left, cy - barH / 2, Math.Max(barH, knobX - left), barH, barH / 2, Theme.TrackFill);
        c.FillCircle(knobX, cy, S(8), Theme.Knob);
    }

    private static void DrawCheckbox(Canvas c)
    {
        double t = CheckProgress;
        double p = _checkRemoving ? 1.0 - t : t;
        DrawBox(c, CheckRect, _checkVisualOn, p, _checkRemoving);
    }

    private static void DrawBox(Canvas c, N.RECT r, bool on, double p, bool shrink)
    {
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int rad = S(9);

        if (!on)
        {
            c.FillRound(r.Left, r.Top, w, h, rad, Theme.CheckOffBg);
            c.StrokeRound(r.Left, r.Top, w, h, rad, Theme.CheckOffBorder, Math.Max(1, S(1)));
            return;
        }

        if (!shrink)
        {
            c.FillRound(r.Left, r.Top, w, h, rad, Theme.CheckOnBg);
            if (p > 0.001) DrawTick(c, r, p, 1.0);
            return;
        }

        double k = Math.Clamp(p, 0.0, 1.0);
        double f = 1.0 - k;

        c.FillRound(r.Left, r.Top, w, h, rad, Mix(Theme.CheckOnBg, Theme.CheckOffBg, f));
        c.StrokeRound(r.Left, r.Top, w, h, rad,
            ((uint)Math.Round(255 * f) << 24) | (Theme.CheckOffBorder & 0x00FFFFFF), Math.Max(1, S(1)));

        if (k > 0.01) DrawTick(c, r, 1.0, k);
    }

    private static void DrawTick(Canvas c, N.RECT r, double p, double scale)
    {
        int cx = (r.Left + r.Right) / 2;
        int cy = (r.Top + r.Bottom) / 2;
        int u = Math.Max(3, S(3));
        int pen = Math.Max(2, S(2));

        int p0x = cx - u * 2, p0y = cy + u / 4;
        int p1x = cx - u / 2, p1y = cy + u * 2;
        int p2x = cx + u * 2, p2y = cy - u * 2;

        uint col = Theme.CheckOnMark;

        if (scale < 0.999)
        {
            p0x = (int)Math.Round(cx + (p0x - cx) * scale);
            p0y = (int)Math.Round(cy + (p0y - cy) * scale);
            p1x = (int)Math.Round(cx + (p1x - cx) * scale);
            p1y = (int)Math.Round(cy + (p1y - cy) * scale);
            p2x = (int)Math.Round(cx + (p2x - cx) * scale);
            p2y = (int)Math.Round(cy + (p2y - cy) * scale);
            pen = Math.Max(1, (int)Math.Round(pen * scale));
            col = ((uint)Math.Round(255 * scale) << 24) | (Theme.CheckOnMark & 0x00FFFFFF);
        }

        const double split = 0.55;

        if (p <= split)
        {
            double k = p / split;
            c.Line(p0x, p0y, Lerp(p0x, p1x, k), Lerp(p0y, p1y, k), col, pen);
        }
        else
        {
            c.Line(p0x, p0y, p1x, p1y, col, pen);
            double k = (p - split) / (1 - split);
            c.Line(p1x, p1y, Lerp(p1x, p2x, k), Lerp(p1y, p2y, k), col, pen);
        }
    }

    private static uint Mix(uint a, uint b, double k)
    {
        double ar = (a >> 16) & 0xFF, ag = (a >> 8) & 0xFF, ab = a & 0xFF, aa = (a >> 24) & 0xFF;
        double br = (b >> 16) & 0xFF, bg = (b >> 8) & 0xFF, bb = b & 0xFF, ba = (b >> 24) & 0xFF;

        uint r = (uint)Math.Clamp(Math.Round(ar + (br - ar) * k), 0, 255);
        uint g = (uint)Math.Clamp(Math.Round(ag + (bg - ag) * k), 0, 255);
        uint bl = (uint)Math.Clamp(Math.Round(ab + (bb - ab) * k), 0, 255);
        uint al = (uint)Math.Clamp(Math.Round(aa + (ba - aa) * k), 0, 255);

        return (al << 24) | (r << 16) | (g << 8) | bl;
    }

    private static int Lerp(int a, int b, double k) => (int)Math.Round(a + (b - a) * k);

    private const int CheckAnimMs = 70;

    private static double CheckProgress
    {
        get
        {
            if (_checkAnimStart == 0) return 1;
            double t = (Environment.TickCount64 - _checkAnimStart) / (double)CheckAnimMs;
            return Math.Clamp(t, 0, 1);
        }
    }

    private static void DrawClose(Canvas c)
    {
        var r = CloseRect;
        if (_hover == Hot.Close)
            c.FillRound(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, S(8), Theme.MenuDangerHover);

        int cx = (r.Left + r.Right) / 2;
        int cy = (r.Top + r.Bottom) / 2;
        int d = S(6);
        uint color = _hover == Hot.Close ? Theme.MenuDangerText : Theme.CloseGlyph;
        int pen = Math.Max(1, S(2));

        c.Line(cx - d, cy - d, cx + d, cy + d, color, pen);
        c.Line(cx + d, cy - d, cx - d, cy + d, color, pen);
    }

    private static bool InRect(N.RECT r, int x, int y)
        => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    private static Hot HitTest(int x, int y)
    {
        if (InRect(CloseRect, x, y)) return Hot.Close;
        if (InRect(BtnReset, x, y)) return Hot.Reset;
        if (InRect(BtnOk, x, y)) return Hot.Ok;
        if (InRect(BtnCancel, x, y)) return Hot.Cancel;
        if (InRect(CheckRect, x, y)) return Hot.Check;
        if (InRect(FieldDelay, x, y)) return Hot.FieldDelay;
        if (InRect(FieldTransition, x, y)) return Hot.FieldTransition;
        if (InRect(TrackHit, x, y)) return Hot.Track;
        return Hot.None;
    }

    private static void SetOpacityFromX(int x)
    {
        var t = TrackRect;
        int left = t.Left + S(6);
        int right = t.Right - S(6);
        double f = Math.Clamp((double)(x - left) / Math.Max(1, right - left), 0, 1);

        _opacity = (int)Math.Round(Config.OpacityMin + f * (Config.OpacityMax - Config.OpacityMin));
        PreviewSelfOpacity();
        N.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private static void PreviewSelfOpacity()
    {
        if (_hwnd == IntPtr.Zero) return;
        int alpha = (int)Math.Round(_opacity * 255.0 / 100.0);
        SetSelfAlpha((byte)Math.Clamp(alpha, 64, 255));
    }

    private static void SetSelfAlpha(byte alpha)
    {
        long ex = N.GetWindowLongPtrW(_hwnd, N.GWL_EXSTYLE).ToInt64();
        if ((ex & N.WS_EX_LAYERED) == 0)
        {
            N.SetWindowLongPtrW(_hwnd, N.GWL_EXSTYLE, new IntPtr(ex | N.WS_EX_LAYERED));
        }
        N.SetLayeredWindowAttributes(_hwnd, 0, alpha, N.LWA_ALPHA);
    }

    private static void Apply()
    {
        var cfg = App.Cfg;

        if (int.TryParse(_delayText, out var d))
            cfg.DelayMs = Math.Clamp(d, Config.DelayMin, Config.DelayMax);

        if (int.TryParse(_transitionText, out var t))
            cfg.TransitionMs = Math.Clamp(t, Config.TransitionMin, Config.TransitionMax);

        cfg.Opacity = Math.Clamp(_opacity, Config.OpacityMin, Config.OpacityMax);
        cfg.Normalize();
        cfg.Save();
        Autostart.Sync(cfg.Autostart);

        Logger.Log($"Настройки сохранены: {cfg}");
    }

    private static void MarkSaved()
    {
        _savedAt = Environment.TickCount64;
        N.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private static void ResetDefaults()
    {
        var d = new Config();
        _delayText = d.DelayMs.ToString();
        _transitionText = d.TransitionMs.ToString();
        _opacity = d.Opacity;
        App.Cfg.Autostart = d.Autostart;
        _checkVisualOn = d.Autostart;
        _checkRemoving = false;
        _checkAnimStart = 0;
        N.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private static void OnClick(Hot hot)
    {
        switch (hot)
        {
            case Hot.Close:
            case Hot.Cancel:
                N.DestroyWindow(_hwnd);
                break;

            case Hot.Ok:
                Apply();
                MarkSaved();
                break;

            case Hot.Reset:
                ResetDefaults();
                Apply();
                MarkSaved();
                break;

            case Hot.Check:
                App.Cfg.Autostart = !App.Cfg.Autostart;
                if (App.Cfg.Autostart)
                {
                    _checkRemoving = false;
                    _checkVisualOn = true;
                }
                else
                {
                    _checkRemoving = true;
                }
                _checkAnimStart = Environment.TickCount64;
                N.SetTimer(_hwnd, (IntPtr)2, 10, IntPtr.Zero);
                N.InvalidateRect(_hwnd, IntPtr.Zero, false);
                break;

            case Hot.FieldDelay:
            case Hot.FieldTransition:
                _focus = hot == Hot.FieldDelay ? Field.Delay : Field.Transition;
                _caretOn = true;
                N.InvalidateRect(_hwnd, IntPtr.Zero, false);
                break;
        }
    }

    private static void TypeChar(char ch)
    {
        if (_focus == Field.None) return;

        string current = FieldText(_focus);
        int lo = Math.Clamp(Math.Min(_selStart, _selEnd), 0, current.Length);
        int hi = Math.Clamp(Math.Max(_selStart, _selEnd), 0, current.Length);

        string next;
        int caret;

        bool isBack = ch == '\b' || ch == (char)0x7F;

        if (isBack)
        {
            if (hi > lo)
            {
                next = current.Remove(lo, hi - lo);
                caret = lo;
            }
            else if (lo > 0)
            {
                next = current.Remove(lo - 1, 1);
                caret = lo - 1;
            }
            else
            {
                next = current;
                caret = 0;
            }
        }
        else if (char.IsDigit(ch))
        {
            if (current.Length - (hi - lo) >= 4)
            {
                next = current;
                caret = hi;
            }
            else
            {
                next = current.Remove(lo, hi - lo).Insert(lo, ch.ToString());
                caret = lo + 1;
            }
        }
        else
        {
            return;
        }

        SetFieldText(_focus, next);
        _selStart = _selEnd = caret;
        _caretOn = true;
        N.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private static string FieldText(Field f)
        => f == Field.Delay ? _delayText : _transitionText;

    private static void SetFieldText(Field f, string value)
    {
        if (f == Field.Delay) _delayText = value;
        else _transitionText = value;
    }

    private static int CaretFromX(Field f, int x)
    {
        if (f == Field.None) return 0;

        var rect = f == Field.Delay ? FieldDelay : FieldTransition;
        string text = FieldText(f);
        int innerL = rect.Left + S(8);
        int innerR = rect.Right - S(8);

        var hdc = N.GetDC(_hwnd);
        if (hdc == IntPtr.Zero) return text.Length;

        try
        {
            int total = Theme.MeasureWidth(hdc, Theme.FValue, text);
            int startX = (innerL + innerR) / 2 - total / 2;

            int best = 0;
            int bestDist = int.MaxValue;

            for (int i = 0; i <= text.Length; i++)
            {
                int w = Theme.MeasureWidth(hdc, Theme.FValue, text[..i]);
                int d = Math.Abs(startX + w - x);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            return best;
        }
        finally
        {
            N.ReleaseDC(_hwnd, hdc);
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case N.WM_NCCALCSIZE:
                if (wParam != IntPtr.Zero)
                {

                    return IntPtr.Zero;
                }
                break;

            case N.WM_CREATE:
                _hwnd = hWnd;
                _opacity = App.Cfg.Opacity;
                _checkVisualOn = App.Cfg.Autostart;
                _checkRemoving = false;
                _checkAnimStart = 0;
                _delayText = App.Cfg.DelayMs.ToString();
                _transitionText = App.Cfg.TransitionMs.ToString();
                _focus = Field.None;
                _hover = Hot.None;
                _pressed = Hot.None;
                _selStart = _selEnd = 0;
                _selecting = false;
                _draggingSlider = false;
                _savedAt = 0;
                N.SetTimer(hWnd, (IntPtr)1, 530, IntPtr.Zero);
                return IntPtr.Zero;

            case N.WM_TIMER:
            {
                if (wParam == (IntPtr)2)
                {
                    if (CheckProgress >= 1)
                    {
                        if (_checkRemoving)
                        {
                            _checkRemoving = false;
                            _checkVisualOn = false;
                        }
                        _checkAnimStart = 0;
                        N.KillTimer(hWnd, (IntPtr)2);
                    }
                    N.InvalidateRect(hWnd, IntPtr.Zero, false);
                    return IntPtr.Zero;
                }

                bool need = false;

                if (_focus != Field.None)
                {
                    _caretOn = !_caretOn;
                    need = true;
                }

                if (_savedAt != 0)
                {
                    if (Environment.TickCount64 - _savedAt >= 1800) _savedAt = 0;
                    need = true;
                }

                if (need) N.InvalidateRect(hWnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            }

            case N.WM_NCPAINT:
                PaintFrameArea(hWnd, Theme.CardBg);
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

            case N.WM_CHAR:
                TypeChar((char)wParam.ToInt64());
                return IntPtr.Zero;

            case N.WM_NCHITTEST:
            {
                int sx = (short)(lParam.ToInt64() & 0xFFFF);
                int sy = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

                var pt = new N.POINT { X = sx, Y = sy };
                N.ScreenToClient(hWnd, ref pt);

                return HitTest(pt.X, pt.Y) != Hot.None
                    ? new IntPtr(N.HTCLIENT)
                    : new IntPtr(N.HTCAPTION);
            }

            case N.WM_MOUSEMOVE:
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

                if (_draggingSlider)
                {
                    if (N.GetKeyState(N.VK_LBUTTON) >= 0)
                    {
                        _draggingSlider = false;
                        SetSelfAlpha(255);
                        if (N.GetCapture() == hWnd) N.ReleaseCapture();
                        N.InvalidateRect(hWnd, IntPtr.Zero, false);
                        return IntPtr.Zero;
                    }

                    SetOpacityFromX(x);
                    return IntPtr.Zero;
                }

                if (_selecting && _focus != Field.None)
                {
                    int idx = CaretFromX(_focus, x);
                    if (idx != _selEnd)
                    {
                        _selEnd = idx;
                        _caretOn = true;
                        N.InvalidateRect(hWnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                }

                var hot = HitTest(x, y);
                if (hot != _hover)
                {
                    _hover = hot;
                    N.InvalidateRect(hWnd, IntPtr.Zero, false);
                }

                var tme = new N.TRACKMOUSEEVENT
                {
                    cbSize = (uint)Marshal.SizeOf<N.TRACKMOUSEEVENT>(),
                    dwFlags = N.TME_LEAVE,
                    hwndTrack = hWnd,
                };
                N.TrackMouseEvent(ref tme);
                return IntPtr.Zero;
            }

            case N.WM_SETCURSOR:
            {

                if (N.LoWord(lParam) == N.HTCLIENT)
                {
                    N.GetCursorPos(out var pt);
                    N.ScreenToClient(hWnd, ref pt);

                    var over = HitTest(pt.X, pt.Y);
                    if (over == Hot.FieldDelay || over == Hot.FieldTransition)
                    {
                        N.SetCursor(N.LoadCursorW(IntPtr.Zero, new IntPtr(N.IDC_IBEAM)));
                        return new IntPtr(1);
                    }
                }
                break;
            }

            case N.WM_MOUSELEAVE:
                if (_hover != Hot.None)
                {
                    _hover = Hot.None;
                    N.InvalidateRect(hWnd, IntPtr.Zero, false);
                }
                return IntPtr.Zero;

            case N.WM_LBUTTONDOWN:
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

                var hot = HitTest(x, y);
                _pressed = hot;

                if (hot == Hot.FieldDelay || hot == Hot.FieldTransition)
                {
                    _focus = hot == Hot.FieldDelay ? Field.Delay : Field.Transition;
                    int idx = CaretFromX(_focus, x);
                    _selStart = idx;
                    _selEnd = idx;
                    _selecting = true;
                    _caretOn = true;
                    N.SetCapture(hWnd);
                    N.InvalidateRect(hWnd, IntPtr.Zero, false);
                    return IntPtr.Zero;
                }

                _selStart = _selEnd = 0;

                if (hot == Hot.Track)
                {
                    _draggingSlider = true;
                    N.SetCapture(hWnd);
                    SetOpacityFromX(x);
                }

                N.InvalidateRect(hWnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            }

            case N.WM_LBUTTONUP:
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

                bool wasSlider = _draggingSlider;
                _draggingSlider = false;
                _selecting = false;

                if (N.GetCapture() == hWnd) N.ReleaseCapture();

                var hot = HitTest(x, y);
                var pressed = _pressed;
                _pressed = Hot.None;

                if (hot == Hot.Track && (pressed == Hot.Track || wasSlider))
                {
                    SetOpacityFromX(x);
                }
                else if (hot == pressed && hot != Hot.None)
                {
                    OnClick(hot);
                }

                if (wasSlider) SetSelfAlpha(255);

                N.InvalidateRect(hWnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            }

            case N.WM_CAPTURECHANGED:
                return IntPtr.Zero;

            case N.WM_CLOSE:
                N.DestroyWindow(hWnd);
                return IntPtr.Zero;

            case N.WM_DESTROY:
                N.KillTimer(hWnd, (IntPtr)1);
                App.SettingsHwnd = IntPtr.Zero;
                _hwnd = IntPtr.Zero;
                _focus = Field.None;
                return IntPtr.Zero;
        }

        return N.DefWindowProcW(hWnd, msg, wParam, lParam);
    }
}
