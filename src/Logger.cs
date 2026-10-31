using System.Text;

namespace SimpleTransparentWindow;

internal static class Logger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleTransparentWindow", "logs");

    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly object Gate = new();

    public static string Directory => LogDir;

    public static void Log(string msg, string level = "INFO")
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(LogDir);
                var path = Path.Combine(LogDir, $"{DateTime.Now:yyyy-MM-dd}.log");
                var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}";
                File.AppendAllText(path, line, Utf8Bom);
            }
        }
        catch
        {

        }
    }
}
