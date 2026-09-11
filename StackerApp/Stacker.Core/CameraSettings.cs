using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stacker.Core;

/// <summary>
/// Сохраняемые настройки приложения
/// </summary>
public class CameraSettings
{
    public string? SelectedCameraId { get; set; }
    public int SelectedWidth { get; set; } = 640;
    public int SelectedHeight { get; set; } = 480;
    public int SelectedFPS { get; set; } = 30;
    public int Exposure { get; set; } = 100;
    public int Gain { get; set; } = 0;
    public bool AutoExposure { get; set; } = true;
    public bool AutoGain { get; set; } = false;
    public StackingMode StackingMode { get; set; } = StackingMode.Sum;
    
    // Размер пикселя в микронах (пользователь вводит вручную)
    public double PixelSizeMicrons { get; set; } = 3.75;
    
    // Последний выбранный режим симуляции
    public bool UseTestGenerator { get; set; } = false;
    
    [JsonIgnore]
    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UVCStacker",
        "settings.json"
    );
    
    public static CameraSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<CameraSettings>(json) ?? new CameraSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки настроек: {ex.Message}");
        }
        return new CameraSettings();
    }
    
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка сохранения настроек: {ex.Message}");
        }
    }
}
