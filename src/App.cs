using SimpleTransparentWindow.Native;

namespace SimpleTransparentWindow;

internal static class App
{
    private static Mutex? _mutex;
    private static Config _cfg = new();
    private static FadeEngine? _fade;
    private static MoveWatcher? _watcher;
    private static IntPtr _host = IntPtr.Zero;

    public static Config Cfg => _cfg;

    public static IntPtr Host => _host;

    public static IntPtr SettingsHwnd { get; set; } = IntPtr.Zero;

    [STAThread]
    private static void Main(string[] args)
    {
        _mutex = new Mutex(true, "Global\\SimpleTransparentWindowMutex", out bool created);
        if (!created) return;

        try
        {

            N.SetProcessDpiAwarenessContext(new IntPtr(-4));
        }
        catch
        {

        }

        var icc = new N.INITCOMMONCONTROLSEX
        {
            dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<N.INITCOMMONCONTROLSEX>(),
            dwICC = N.ICC_STANDARD_CLASSES | N.ICC_BAR_CLASSES,
        };
        N.InitCommonControlsEx(ref icc);

        AppIcon.Load();

        _cfg = Config.Load();

        if (args.Any(a => string.Equals(a, "--disable-autostart", StringComparison.OrdinalIgnoreCase)))
        {
            _cfg.Autostart = false;
            _cfg.Save();
            Autostart.Sync(false);
            return;
        }

        var hInstance = N.GetModuleHandleW(null);

        _host = Tray.Create(hInstance);
        if (_host == IntPtr.Zero)
        {
            Logger.Log("Не удалось создать окно трея — выход", "ERROR");
            return;
        }

        _fade = new FadeEngine(_host, () => _cfg);
        _watcher = new MoveWatcher(_host, _fade, () => _cfg);

        Autostart.Sync(_cfg.Autostart);

        Logger.Log($"SimpleTransparentWindow запущен: {_cfg}");

        bool silent = args.Any(a =>
            string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase));

        if (!silent) OpenSettings();

        RunMessageLoop();

        _watcher.Dispose();
        _fade.RestoreImmediate();
        Tray.Remove();

        Logger.Log("SimpleTransparentWindow остановлен");

        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }

    private static void RunMessageLoop()
    {
        while (true)
        {
            int r = N.GetMessageW(out var msg, IntPtr.Zero, 0, 0);
            if (r <= 0) break;

            var sh = SettingsHwnd;
            if (sh != IntPtr.Zero && N.IsWindow(sh) && (msg.hwnd == sh || N.IsChild(sh, msg.hwnd)))
            {
                if (HandleSettingsKey(msg)) continue;
            }

            N.TranslateMessage(ref msg);
            N.DispatchMessageW(ref msg);
        }
    }

    private static bool HandleSettingsKey(N.MSG msg)
    {
        if (msg.message != N.WM_KEYDOWN) return false;

        switch ((int)msg.wParam.ToInt64())
        {
            case N.VK_ESCAPE:
                SettingsWindow.RequestClose();
                return true;

            case N.VK_RETURN:
                SettingsWindow.RequestSave();
                return true;

            case N.VK_TAB:
                SettingsWindow.CycleFocus();
                return true;

            default:
                return false;
        }
    }

    public static void OnTimer(IntPtr timerId)
    {
        if (timerId == MoveWatcher.TimerDelay) _watcher?.OnDelayElapsed();
        else if (timerId == FadeEngine.TimerFade) _fade?.OnTimer();
    }

    public static void OpenSettings()
    {
        var h = SettingsHwnd;
        if (h != IntPtr.Zero && N.IsWindow(h))
        {
            N.ShowWindow(h, N.SW_SHOW);
            N.SetForegroundWindow(h);
            return;
        }

        SettingsHwnd = SettingsWindow.Open(N.GetModuleHandleW(null), _cfg);
    }

    public static void Shutdown()
    {
        Tray.Remove();

        var sh = SettingsHwnd;
        if (sh != IntPtr.Zero && N.IsWindow(sh)) N.DestroyWindow(sh);

        N.PostQuitMessage(0);
    }
}

internal static class AppIcon
{
    public static IntPtr Large { get; private set; } = IntPtr.Zero;
    public static IntPtr Small { get; private set; } = IntPtr.Zero;

    public static void Load()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) &&
                N.ExtractIconExW(exe, 0, out var big, out var small, 1) > 0)
            {
                Large = big;
                Small = small;
            }
        }
        catch { }

        if (Large == IntPtr.Zero) Large = N.LoadIconW(IntPtr.Zero, (IntPtr)N.IDI_APPLICATION);
        if (Small == IntPtr.Zero) Small = Large;
    }
}
