using OpenCvSharp;

namespace Stacker.Core;

/// <summary>
/// Реализация контроллера UVC-камеры с использованием OpenCvSharp
/// </summary>
public class UvcCameraController : ICameraController
{
    private VideoCapture? _capture;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private int _frameIndex;
    private readonly object _lock = new();
    
    public event EventHandler<CameraConnectionState>? ConnectionStateChanged;
    public event EventHandler<CapturedFrame>? FrameCaptured;
    public event EventHandler<string>? ErrorOccurred;
    
    public CameraConnectionState State { get; private set; } = CameraConnectionState.Disconnected;
    public string? ErrorMessage { get; private set; }
    public bool IsCapturing { get; private set; }
    
    private void SetState(CameraConnectionState newState, string? error = null)
    {
        State = newState;
        ErrorMessage = error;
        ConnectionStateChanged?.Invoke(this, newState);
    }
    
    public Task<IReadOnlyList<string>> GetAvailableCamerasAsync()
    {
        return Task.Run(() =>
        {
            var cameras = new List<string>();
            // Попытка обнаружить камеры от 0 до 9
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using var test = new VideoCapture(i);
                    if (test.IsOpened())
                    {
                        cameras.Add($"USB Camera {i}");
                    }
                }
                catch
                {
                    // Камера недоступна
                }
            }
            return (IReadOnlyList<string>)cameras;
        });
    }
    
    public Task<IReadOnlyList<CameraMode>> GetSupportedModesAsync(string cameraId)
    {
        return Task.Run(() =>
        {
            var modes = new List<CameraMode>
            {
                new(640, 480, "YUY2", 30.0),
                new(640, 480, "MJPG", 30.0),
                new(800, 600, "YUY2", 30.0),
                new(800, 600, "MJPG", 30.0),
                new(1280, 720, "YUY2", 30.0),
                new(1280, 720, "MJPG", 30.0),
                new(1920, 1080, "YUY2", 30.0),
                new(1920, 1080, "MJPG", 30.0)
            };
            return (IReadOnlyList<CameraMode>)modes;
        });
    }
    
    public Task<bool> ConnectAsync(string cameraId, CameraMode mode)
    {
        return Task.Run(() =>
        {
            try
            {
                SetState(CameraConnectionState.Connecting);
                
                // Извлекаем индекс камеры из имени
                int cameraIndex = 0;
                if (cameraId.Contains("Camera ") && int.TryParse(cameraId.Split(' ').Last(), out var idx))
                {
                    cameraIndex = idx;
                }
                
                _capture = new VideoCapture(cameraIndex);
                if (!_capture.IsOpened())
                {
                    SetState(CameraConnectionState.Error, "Не удалось открыть камеру");
                    return false;
                }
                
                // Установка разрешения
                _capture.Set(VideoCaptureProperties.FrameWidth, mode.Width);
                _capture.Set(VideoCaptureProperties.FrameHeight, mode.Height);
                _capture.Set(VideoCaptureProperties.Fps, mode.Fps);
                
                // Проверка применённых настроек
                var actualWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
                var actualHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
                
                if (actualWidth != mode.Width || actualHeight != mode.Height)
                {
                    // Камера не поддерживает запрошенный режим
                }
                
                _frameIndex = 0;
                SetState(CameraConnectionState.Connected);
                return true;
            }
            catch (Exception ex)
            {
                SetState(CameraConnectionState.Error, ex.Message);
                ErrorOccurred?.Invoke(this, ex.Message);
                return false;
            }
        });
    }
    
    public Task DisconnectAsync()
    {
        return Task.Run(() =>
        {
            StopCapture();
            _capture?.Dispose();
            _capture = null;
            SetState(CameraConnectionState.Disconnected);
        });
    }
    
    public Task<ExposureSettings?> GetCurrentExposureSettingsAsync()
    {
        return Task.Run(() =>
        {
            if (_capture == null) return null;
            
            try
            {
                var exposure = (int)_capture.Get(VideoCaptureProperties.Exposure);
                var gain = (int)_capture.Get(VideoCaptureProperties.Gain);
                
                // Примечание: OpenCvSharp не предоставляет прямой доступ к авто-режимам
                return new ExposureSettings(exposure, gain, false, false);
            }
            catch
            {
                return null;
            }
        });
    }
    
    public Task<bool> SetExposureAsync(int exposureUs, bool auto)
    {
        return Task.Run(() =>
        {
            if (_capture == null) return false;
            
            try
            {
                // Конвертация микросекунд в единицы OpenCV (зависит от камеры)
                int exposureValue = auto ? -1 : exposureUs / 100;
                _capture.Set(VideoCaptureProperties.Exposure, exposureValue);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }
    
    public Task<bool> SetGainAsync(int gain, bool auto)
    {
        return Task.Run(() =>
        {
            if (_capture == null) return false;
            
            try
            {
                _capture.Set(VideoCaptureProperties.Gain, auto ? -1 : gain);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }
    
    public void StartCapture()
    {
        if (_capture == null || IsCapturing) return;
        
        _captureCts = new CancellationTokenSource();
        IsCapturing = true;
        
        _captureTask = Task.Run(() => CaptureLoop(_captureCts.Token));
    }
    
    public void StopCapture()
    {
        if (!IsCapturing) return;
        
        _captureCts?.Cancel();
        _captureTask?.Wait(1000);
        _captureCts?.Dispose();
        _captureCts = null;
        _captureTask = null;
        IsCapturing = false;
    }
    
    private void CaptureLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _capture != null)
        {
            try
            {
                using var frame = new Mat();
                if (!_capture.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(10);
                    continue;
                }
                
                // Конвертация в нужный формат
                Mat processedFrame;
                if (frame.Channels() == 3)
                {
                    // BGR -> Grayscale для упрощения
                    processedFrame = new Mat();
                    Cv2.CvtColor(frame, processedFrame, ColorConversionCodes.BGR2GRAY);
                }
                else if (frame.Channels() == 1)
                {
                    processedFrame = frame;
                }
                else
                {
                    frame.CopyTo(processedFrame = new Mat());
                }
                
                var imageData = new byte[processedFrame.Total()];
                processedFrame.GetArray<byte>(out imageData);
                
                var capturedFrame = new CapturedFrame
                {
                    ImageData = imageData,
                    Width = processedFrame.Cols,
                    Height = processedFrame.Rows,
                    Channels = 1,
                    TimestampTicks = DateTime.Now.Ticks,
                    FrameIndex = Interlocked.Increment(ref _frameIndex)
                };
                
                FrameCaptured?.Invoke(this, capturedFrame);
                
                processedFrame.Dispose();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex.Message);
                Thread.Sleep(50);
            }
        }
    }
    
    public void Dispose()
    {
        StopCapture();
        _capture?.Dispose();
        _capture = null;
    }
}
