using SimpleTransparentWindow.Native;

namespace SimpleTransparentWindow;

internal sealed class FadeEngine
{
    public const IntPtr TimerFade = (IntPtr)2;

    private readonly IntPtr _host;
    private readonly Func<Config> _config;

    private IntPtr _hwnd = IntPtr.Zero;
    private long _origExStyle;
    private bool _layeredAdded;

    private int _current = 255;
    private int _from = 255;
    private int _target = 255;
    private long _startMs;
    private bool _running;

    public FadeEngine(IntPtr timerHost, Func<Config> config)
    {
        _host = timerHost;
        _config = config;
    }

    public IntPtr ActiveWindow => _hwnd;

    public void Attach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        if (_hwnd != IntPtr.Zero && _hwnd != hwnd)
            RestoreImmediate();

        _hwnd = hwnd;
        _origExStyle = N.GetWindowLongPtrW(hwnd, N.GWL_EXSTYLE).ToInt64();
        _layeredAdded = false;
        _current = 255;
        _from = 255;
        _target = 255;
        StopTimer();
    }

    public void FadeTo(int target)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (!N.IsWindow(_hwnd)) { Reset(); return; }

        target = Math.Clamp(target, 1, 255);
        if (_target == target && _running) return;

        _from = _current;
        _target = target;
        _startMs = Environment.TickCount64;

        if (_from == _target) { Apply(_current); return; }

        StartTimer();
    }

    public void Restore()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_current == 255 && _target == 255) { Cleanup(); return; }
        FadeTo(255);
    }

    public void RestoreImmediate()
    {
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            if (N.IsWindow(_hwnd))
            {
                if (_layeredAdded || (_origExStyle & N.WS_EX_LAYERED) != 0)
                {
                    N.SetLayeredWindowAttributes(_hwnd, 0, 255, N.LWA_ALPHA);
                }
            }
        }
        catch { }
        Cleanup();
    }

    public void OnTimer()
    {
        if (_hwnd == IntPtr.Zero) { StopTimer(); return; }

        if (!N.IsWindow(_hwnd))
        {
            Reset();
            return;
        }

        double elapsed = Environment.TickCount64 - _startMs;
        int duration = Math.Max(1, _config().TransitionMs);
        double t = Math.Clamp(elapsed / duration, 0.0, 1.0);
        double eased = 1.0 - (1.0 - t) * (1.0 - t);

        int alpha = (int)Math.Round(_from + (_target - _from) * eased);
        _current = alpha;
        Apply(alpha);

        if (t >= 1.0)
        {
            StopTimer();
            _running = false;

            if (_target == 255)
            {

                Cleanup();
            }
        }
    }

    private void Apply(int alpha)
    {
        if (_hwnd == IntPtr.Zero || !N.IsWindow(_hwnd)) return;

        long ex = N.GetWindowLongPtrW(_hwnd, N.GWL_EXSTYLE).ToInt64();
        if ((ex & N.WS_EX_LAYERED) == 0)
        {
            N.SetWindowLongPtrW(_hwnd, N.GWL_EXSTYLE, new IntPtr(ex | N.WS_EX_LAYERED));
            _layeredAdded = true;
        }

        if (!N.SetLayeredWindowAttributes(_hwnd, 0, (byte)Math.Clamp(alpha, 0, 255), N.LWA_ALPHA))
        {
            Logger.Log($"SetLayeredWindowAttributes failed hwnd=0x{_hwnd:X}", "WARN");
        }
    }

    private void Cleanup()
    {
        StopTimer();
        _running = false;

        if (_hwnd != IntPtr.Zero && _layeredAdded && N.IsWindow(_hwnd))
        {
            try { N.SetWindowLongPtrW(_hwnd, N.GWL_EXSTYLE, new IntPtr(_origExStyle)); } catch { }
        }

        _hwnd = IntPtr.Zero;
        _layeredAdded = false;
        _current = 255;
        _from = 255;
        _target = 255;
    }

    private void Reset()
    {
        StopTimer();
        _running = false;
        _hwnd = IntPtr.Zero;
        _layeredAdded = false;
        _current = 255;
    }

    private void StartTimer()
    {
        N.SetTimer(_host, TimerFade, 16, IntPtr.Zero);
        _running = true;
    }

    private void StopTimer() => N.KillTimer(_host, TimerFade);
}
