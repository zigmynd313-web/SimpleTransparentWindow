using System.Globalization;

namespace SimpleTransparentWindow;

internal sealed class Config
{
    public int DelayMs { get; set; } = 150;
    public int Opacity { get; set; } = 80;
    public int TransitionMs { get; set; } = 180;
    public bool Autostart { get; set; } = true;

    public const int DelayMin = 0, DelayMax = 5000;
    public const int OpacityMin = 10, OpacityMax = 90;
    public const int TransitionMin = 0, TransitionMax = 2000;

    public byte TargetAlpha => (byte)Math.Clamp((int)Math.Round(Opacity * 255.0 / 100.0), 1, 255);

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleTransparentWindow");

    private static string FilePath => Path.Combine(Dir, "config.ini");

    public static Config Load()
    {
        var c = new Config();
        try
        {
            if (!File.Exists(FilePath)) { c.Save(); return c; }

            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();

                switch (key.ToLowerInvariant())
                {
                    case "delayms" when int.TryParse(val, out var d):
                        c.DelayMs = Math.Clamp(d, DelayMin, DelayMax); break;
                    case "opacity" when int.TryParse(val, out var o):
                        c.Opacity = Math.Clamp(o, OpacityMin, OpacityMax); break;
                    case "transitionms" when int.TryParse(val, out var t):
                        c.TransitionMs = Math.Clamp(t, TransitionMin, TransitionMax); break;
                    case "autostart":
                        c.Autostart = val is "1" or "true" or "True" or "yes"; break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Не удалось прочитать настройки: {ex.Message}", "WARN");
        }
        return c;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var text = string.Join(Environment.NewLine, new[]
            {
                "# SimpleTransparentWindow — настройки",
                $"delayMs={DelayMs}",
                $"opacity={Opacity}",
                $"transitionMs={TransitionMs}",
                $"autostart={(Autostart ? 1 : 0)}",
                "",
            });
            File.WriteAllText(FilePath, text, new System.Text.UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            Logger.Log($"Не удалось сохранить настройки: {ex.Message}", "WARN");
        }
    }

    public Config Clone() => new()
    {
        DelayMs = DelayMs,
        Opacity = Opacity,
        TransitionMs = TransitionMs,
        Autostart = Autostart,
    };

    public void Normalize()
    {
        DelayMs = Math.Clamp(DelayMs, DelayMin, DelayMax);
        Opacity = Math.Clamp(Opacity, OpacityMin, OpacityMax);
        TransitionMs = Math.Clamp(TransitionMs, TransitionMin, TransitionMax);
    }

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "delay={0}ms opacity={1}% transition={2}ms autostart={3}",
        DelayMs, Opacity, TransitionMs, Autostart);
}
