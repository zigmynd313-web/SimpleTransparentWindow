using Microsoft.Win32;

namespace SimpleTransparentWindow;

internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SimpleTransparentWindow";

    public static string ExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SimpleTransparentWindow.exe");

    private const string SilentFlag = "--silent";

    public static void Sync(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                Logger.Log("Не удалось открыть ключ автозапуска", "WARN");
                return;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExePath}\" {SilentFlag}", RegistryValueKind.String);
                Logger.Log($"Автозапуск включён: {ExePath}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Logger.Log("Автозапуск отключён");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка автозапуска: {ex.Message}", "ERROR");
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is not null;
        }
        catch
        {
            return false;
        }
    }
}
