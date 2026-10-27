using System.Text.Json;

namespace UGreenCapViewer;

sealed class Settings
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UGreenCapViewer", "settings.json");

    /// <summary>Null = first device whose name contains "UGREEN".</summary>
    public string? VideoDevice { get; set; }
    /// <summary>Null = match the video device, "" = no audio.</summary>
    public string? AudioDevice { get; set; }
    public string? Format { get; set; }
    public bool Muted { get; set; }
    public bool TopMost { get; set; }
    public int[]? Bounds { get; set; }
    public bool Maximized { get; set; }

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
