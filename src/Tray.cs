using System.Runtime.InteropServices;
using SimpleTransparentWindow.Native;

namespace SimpleTransparentWindow;

internal static class Tray
{
    private const uint TrayMessage = N.WM_APP + 1;
    private const uint WsPopup = 0x80000000;

    private const int IdSettings = 100;
    private const int IdAutostart = 101;
    private const int IdExit = 102;

    private static IntPtr _hwnd = IntPtr.Zero;
    private static N.WndProcDelegate? _wndProc;
    private static uint _taskbarCreated;
    private static N.NOTIFYICONDATAW _data;

    public static IntPtr Create(IntPtr hInstance)
    {
        _wndProc = WndProc;
        _taskbarCreated = N.RegisterWindowMessageW("TaskbarCreated");

        var wc = new N.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<N.WNDCLASSEXW>(),
            style = 0,
            lpfnWndProc = _wndProc,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = AppIcon.Large,
            hCursor = N.LoadCursorW(IntPtr.Zero, (IntPtr)N.IDC_ARROW),
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = "SimpleTransparentWindowTray",
            hIconSm = AppIcon.Small,
        };

        if (N.RegisterClassExW(ref wc) == 0)
        {
            Logger.Log($"RegisterClassExW(tray) failed err={Marshal.GetLastWin32Error()}", "ERROR");
            return IntPtr.Zero;
        }

        _hwnd = N.CreateWindowExW(0, "SimpleTransparentWindowTray", "SimpleTransparentWindow", WsPopup,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            Logger.Log($"CreateWindowExW(tray) failed err={Marshal.GetLastWin32Error()}", "ERROR");
            return IntPtr.Zero;
        }

        AddIcon();
        return _hwnd;
    }

    private static void AddIcon()
    {
        _data = new N.NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<N.NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = N.NIF_MESSAGE | N.NIF_ICON | N.NIF_TIP,
            uCallbackMessage = TrayMessage,
            hIcon = AppIcon.Small,
            szTip = "SimpleTransparentWindow",
        };

        if (!N.Shell_NotifyIconW(N.NIM_ADD, ref _data))
            Logger.Log($"Shell_NotifyIcon(NIM_ADD) failed err={Marshal.GetLastWin32Error()}", "WARN");
    }

    public static void Remove()
    {
        if (_hwnd != IntPtr.Zero && _data.cbSize != 0)
        {
            N.Shell_NotifyIconW(N.NIM_DELETE, ref _data);
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {

            AddIcon();
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case TrayMessage:
                OnTrayMessage(lParam);
                return IntPtr.Zero;

            case N.WM_TIMER:
                App.OnTimer(wParam);
                return IntPtr.Zero;
        }

        return N.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void OnTrayMessage(IntPtr lParam)
    {
        uint ev = (uint)lParam.ToInt64();

        if (ev == N.WM_LBUTTONDBLCLK)
        {
            App.OpenSettings();
        }
        else if (ev == N.WM_RBUTTONUP || ev == N.WM_CONTEXTMENU)
        {
            ShowMenu();
        }
    }

    private static void ShowMenu()
    {
        var items = new[]
        {
            new PopupMenu.Item("Настройки", IdSettings, false),
            PopupMenu.Item.Separator,
            new PopupMenu.Item("Запускать при входе", IdAutostart, App.Cfg.Autostart),
            PopupMenu.Item.Separator,
            new PopupMenu.Item("Выход", IdExit, false, Danger: true),
        };

        PopupMenu.Show(_hwnd, items, OnMenuSelect);
    }

    private static void OnMenuSelect(int id)
    {
        switch (id)
        {
            case IdSettings:
                App.OpenSettings();
                break;

            case IdAutostart:
                App.Cfg.Autostart = !App.Cfg.Autostart;
                App.Cfg.Save();
                Autostart.Sync(App.Cfg.Autostart);
                break;

            case IdExit:
                App.Shutdown();
                break;
        }
    }
}
