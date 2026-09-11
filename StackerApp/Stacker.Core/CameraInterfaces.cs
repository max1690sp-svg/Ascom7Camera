namespace Stacker.Core;

/// <summary>
/// Информация о режиме камеры (разрешение, формат, FPS)
/// </summary>
public record CameraMode(int Width, int Height, string Format, double Fps);

/// <summary>
/// Параметры экспозиции камеры
/// </summary>
public record ExposureSettings(int ExposureUs, int Gain, bool AutoExposure, bool AutoGain);

/// <summary>
/// Статус подключения камеры
/// </summary>
public enum CameraConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

/// <summary>
/// Режим накопления кадров
/// </summary>
public enum StackingMode
{
    Sum,        // Сумма кадров (по умолчанию)
    Average     // Среднее арифметическое
}

/// <summary>
/// Результат захвата кадра
/// </summary>
public class CapturedFrame
{
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public int Channels { get; set; }
    public long TimestampTicks { get; set; }
    public int FrameIndex { get; set; }
    
    public DateTime CaptureTime => new DateTime(TimestampTicks);
}

/// <summary>
/// Результат накопления для передачи PHD2
/// </summary>
public class StackedResult
{
    public ushort[] ImageData { get; set; } = Array.Empty<ushort>();
    public int Width { get; set; }
    public int Height { get; set; }
    public int FramesStacked { get; set; }
    public double TotalExposureMs { get; set; }
    public DateTime CompletionTime { get; set; }
    public bool IsValid => ImageData.Length > 0 && Width > 0 && Height > 0;
}

/// <summary>
/// Интерфейс управления физической камерой
/// </summary>
public interface ICameraController : IDisposable
{
    event EventHandler<CameraConnectionState> ConnectionStateChanged;
    event EventHandler<CapturedFrame> FrameCaptured;
    event EventHandler<string> ErrorOccurred;
    
    CameraConnectionState State { get; }
    string? ErrorMessage { get; }
    
    Task<IReadOnlyList<string>> GetAvailableCamerasAsync();
    Task<IReadOnlyList<CameraMode>> GetSupportedModesAsync(string cameraId);
    Task<bool> ConnectAsync(string cameraId, CameraMode mode);
    Task DisconnectAsync();
    
    Task<ExposureSettings?> GetCurrentExposureSettingsAsync();
    Task<bool> SetExposureAsync(int exposureUs, bool auto);
    Task<bool> SetGainAsync(int gain, bool auto);
    
    void StartCapture();
    void StopCapture();
    bool IsCapturing { get; }
}
