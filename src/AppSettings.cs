using System.IO;
using System.Text.Json;

namespace BlueSpectrum;

public sealed class AppSettings
{
    public double GainDb { get; set; } = 6;
    public double Brightness { get; set; } = .85;
    public double Glow { get; set; } = .6;
    public double AttackMs { get; set; } = 20;
    public double ReleaseMs { get; set; } = 180;
    public int DisplayProfileVersion { get; set; }
    public bool PeakHold { get; set; }
    public bool AlwaysOnTop { get; set; }
    public int Fps { get; set; } = 60;
    public string? DeviceId { get; set; }
    public string? RenderGpuId { get; set; }
    public double Width { get; set; } = 800;
    public double Height { get; set; } = 170;
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;

    // Portable by default; no registry or system audio configuration is changed.
    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");
    public static AppSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
            if (s == null) return new();
            if (s.DisplayProfileVersion < 2)
            {
                // Keep personal motion/window choices; migrate only the previous stock values.
                if (s.AttackMs == 35 && s.ReleaseMs == 230) { s.AttackMs = 20; s.ReleaseMs = 180; }
                if (s.Height == 214) s.Height = 170;
                s.DisplayProfileVersion = 2;
            }
            if (s.DisplayProfileVersion < 3)
            {
                if (s.Height == 170) s.Height = 220;
                s.DisplayProfileVersion = 3;
            }
            if (s.DisplayProfileVersion < 4)
            {
                if (s.Height == 220) s.Height = 170;
                s.DisplayProfileVersion = 4;
            }
            s.Validate(); return s;
        }
        catch { return new(); }
    }
    public void Validate()
    {
        DisplayProfileVersion = 4;
        GainDb = Clamp(GainDb, -24, 30, 6); Brightness = Clamp(Brightness, .15, 1, .85);
        Glow = Clamp(Glow, 0, 1, .6); AttackMs = Clamp(AttackMs, 10, 150, 20);
        ReleaseMs = Clamp(ReleaseMs, 80, 900, 180);
        Width = Clamp(Width, 720, 3000, 800); Height = Clamp(Height, 160, 1800, 170);
        if (Fps != 30) Fps = 60;
        if (string.IsNullOrWhiteSpace(RenderGpuId)) RenderGpuId = null;
    }
    private static double Clamp(double v, double lo, double hi, double fallback) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : fallback;
    public bool Save()
    {
        try
        {
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
            File.Move(temp, SettingsPath, true); return true;
        }
        catch { return false; }
    }
}
