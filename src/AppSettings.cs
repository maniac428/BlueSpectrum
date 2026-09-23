using System.IO;
using System.Text.Json;

namespace BlueSpectrum;

public sealed class AppSettings
{
    public double GainDb { get; set; } = 6;
    public double Brightness { get; set; } = .85;
    public double Glow { get; set; } = .6;
    public double AttackMs { get; set; } = 35;
    public double ReleaseMs { get; set; } = 230;
    public bool PeakHold { get; set; }
    public bool AlwaysOnTop { get; set; }
    public int Fps { get; set; } = 60;
    public string? DeviceId { get; set; }
    public string? RenderGpuId { get; set; }
    public double Width { get; set; } = 800;
    public double Height { get; set; } = 214;
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;

    // Portable by default; no registry or system audio configuration is changed.
    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");
    public static AppSettings Load()
    {
        try { var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }); s?.Validate(); return s ?? new(); }
        catch { return new(); }
    }
    public void Validate()
    {
        GainDb = Clamp(GainDb, -24, 30, 6); Brightness = Clamp(Brightness, .15, 1, .85);
        Glow = Clamp(Glow, 0, 1, .6); AttackMs = Clamp(AttackMs, 10, 150, 35);
        ReleaseMs = Clamp(ReleaseMs, 80, 900, 230);
        Width = Clamp(Width, 720, 3000, 800); Height = Clamp(Height, 200, 1800, 214);
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
