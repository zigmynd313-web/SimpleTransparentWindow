using SimpleTransparentWindow.Native;

namespace SimpleTransparentWindow;

internal sealed class MoveWatcher
{
    public const IntPtr TimerDelay = (IntPtr)1;

    private readonly IntPtr _host;
    private readonly FadeEngine _fade;
    private readonly Func<Config> _config;

    private IntPtr _hook;
    private readonly N.WinEventProc _proc;
    private IntPtr _pendingHwnd = IntPtr.Zero;

    public MoveWatcher(IntPtr timerHost, FadeEngine fade, Func<Config> config)
    {
        _host = timerHost;
        _fade = fade;
        _config = config;

        _proc = OnWinEvent;
        _hook = N.SetWinEventHook(
            N.EVENT_SYSTEM_MOVESIZESTART,
            N.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, _proc, 0, 0,

            N.WINEVENT_OUTOFCONTEXT);

        if (_hook == IntPtr.Zero)
            Logger.Log($"SetWinEventHook failed err={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}", "ERROR");
        else
            Logger.Log("Хук перемещения установлен");
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
                            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == N.EVENT_SYSTEM_MOVESIZESTART)
        {
            CancelPending();
            _fade.Attach(hwnd);
            Logger.Log($"Перетаскивание начато: 0x{hwnd:X}");

            var cfg = _config();
            if (cfg.DelayMs <= 0)
            {
                _fade.FadeTo(cfg.TargetAlpha);
                Logger.Log($"Задержка 0 мс → сразу затемняю до {cfg.TargetAlpha}");
            }
            else
            {
                _pendingHwnd = hwnd;
                N.SetTimer(_host, TimerDelay, (uint)cfg.DelayMs, IntPtr.Zero);
            }
        }
        else if (eventType == N.EVENT_SYSTEM_MOVESIZEEND)
        {
            CancelPending();
            _fade.Restore();
            Logger.Log($"Перетаскивание завершено: 0x{hwnd:X}");
        }
    }

    public void OnDelayElapsed()
    {
        N.KillTimer(_host, TimerDelay);

        var hwnd = _pendingHwnd;
        _pendingHwnd = IntPtr.Zero;

        if (hwnd == IntPtr.Zero) return;
        if (!N.IsWindow(hwnd)) return;
        if (_fade.ActiveWindow != hwnd) return;

        var cfg = _config();
        Logger.Log($"Задержка {cfg.DelayMs} мс истекла → затемняю до {cfg.TargetAlpha}");
        _fade.FadeTo(cfg.TargetAlpha);
    }

    private void CancelPending()
    {
        if (_pendingHwnd != IntPtr.Zero)
        {
            N.KillTimer(_host, TimerDelay);
            _pendingHwnd = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        CancelPending();
        if (_hook != IntPtr.Zero)
        {
            N.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
