

namespace Stacker.Core
{
    /// <summary>
    /// Тестовый генератор кадров с искусственными звёздами
    /// </summary>
    public class TestFrameGenerator : ICameraController
    {
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _task;
        private int _frameIndex;
        private bool _isRunning;
        
        private int _width = 640;
        private int _height = 480;
        private double _starX = 320;
        private double _starY = 240;
        private double _driftX = 0.5; // Пикселей в секунду
        private double _driftY = 0.2;
        
        public event EventHandler<CameraConnectionState>? ConnectionStateChanged;
        public event EventHandler<CapturedFrame>? FrameCaptured;
        public event EventHandler<string>? ErrorOccurred;
        
        public CameraConnectionState State { get; private set; } = CameraConnectionState.Disconnected;
        public string? ErrorMessage { get; private set; }
        public bool IsCapturing => _isRunning;
        
        public Task<IReadOnlyList<string>> GetAvailableCamerasAsync()
        {
            return Task.FromResult<IReadOnlyList<string>>(new List<string> { "Test Camera (Simulator)" });
        }
        
        public Task<IReadOnlyList<CameraMode>> GetSupportedModesAsync(string cameraId)
        {
            var modes = new List<CameraMode>
            {
                new(640, 480, "GRAY", 30.0),
                new(800, 600, "GRAY", 30.0),
                new(1280, 720, "GRAY", 30.0)
            };
            return Task.FromResult<IReadOnlyList<CameraMode>>(modes);
        }
        
        public Task<bool> ConnectAsync(string cameraId, CameraMode mode)
        {
            return Task.Run(() =>
            {
                ConnectionStateChanged?.Invoke(this, CameraConnectionState.Connecting);
                
                _width = mode.Width;
                _height = mode.Height;
                _starX = _width / 2.0;
                _starY = _height / 2.0;
                
                ConnectionStateChanged?.Invoke(this, CameraConnectionState.Connected);
                return true;
            });
        }
        
        public Task DisconnectAsync()
        {
            StopCapture();
            ConnectionStateChanged?.Invoke(this, CameraConnectionState.Disconnected);
            return Task.CompletedTask;
        }
        
        public Task<ExposureSettings?> GetCurrentExposureSettingsAsync()
        {
            return Task.FromResult<ExposureSettings?>(new ExposureSettings(33333, 0, false, false));
        }
        
        public Task<bool> SetExposureAsync(int exposureUs, bool auto) => Task.FromResult(true);
        public Task<bool> SetGainAsync(int gain, bool auto) => Task.FromResult(true);
        
        public void StartCapture()
        {
            if (_isRunning) return;
            
            _isRunning = true;
            _cts = new CancellationTokenSource();
            _task = Task.Run(() => CaptureLoop(_cts.Token));
        }
        
        public void StopCapture()
        {
            _cts?.Cancel();
            _task?.Wait(1000);
            _isRunning = false;
        }
        
        private void CaptureLoop(CancellationToken ct)
        {
            var random = new Random();
            var lastTime = DateTime.Now;
            
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var elapsed = (now - lastTime).TotalSeconds;
                    
                    if (elapsed >= 1.0 / 30.0) // 30 FPS
                    {
                        // Обновление позиции звезды (дрейф)
                        _starX += _driftX * elapsed;
                        _starY += _driftY * elapsed;
                        
                        // Генерация кадра со звездой
                        var frame = GenerateFrameWithStar(_starX, _starY, random);
                        FrameCaptured?.Invoke(this, frame);
                        
                        lastTime = now;
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, ex.Message);
                }
            }
        }
        
        private CapturedFrame GenerateFrameWithStar(double starX, double starY, Random random)
        {
            var data = new byte[_width * _height];
            
            // Базовый шум (небо)
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(10 + random.Next(5)); // Фон ~10-15
            }
            
            // Добавляем звезду (гауссово пятно)
            int radius = 5;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = (int)(starX + dx);
                    int y = (int)(starY + dy);
                    
                    if (x >= 0 && x < _width && y >= 0 && y < _height)
                    {
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        double intensity = 200 * Math.Exp(-dist * dist / (2 * 2 * 2)); // Гауссиан с sigma=2
                        
                        int idx = y * _width + x;
                        data[idx] = (byte)Math.Min(255, data[idx] + (int)intensity);
                    }
                }
            }
            
            return new CapturedFrame
            {
                ImageData = data,
                Width = _width,
                Height = _height,
                Channels = 1,
                TimestampTicks = DateTime.Now.Ticks,
                FrameIndex = Interlocked.Increment(ref _frameIndex)
            };
        }
        
        public void Dispose()
        {
            StopCapture();
        }
    }
}
