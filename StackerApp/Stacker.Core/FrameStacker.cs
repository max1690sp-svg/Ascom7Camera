namespace Stacker.Core;

/// <summary>
/// Модуль накопления кадров для улучшения сигнала
/// </summary>
public class FrameStacker : IDisposable
{
    private readonly object _lock = new();
    private readonly Queue<CapturedFrame> _frameQueue = new();
    private readonly int _maxQueueSize = 100;
    
    private CapturedFrame[]? _currentExposureFrames;
    private int _currentFrameCount;
    private DateTime _exposureStartTime;
    private bool _isAccumulating;
    
    public StackingMode Mode { get; set; } = StackingMode.Sum;
    public int Width { get; private set; }
    public int Height { get; private set; }
    
    public event EventHandler<StackedResult>? ResultReady;
    public event EventHandler<int>? FrameAdded;
    
    public void Initialize(int width, int height)
    {
        lock (_lock)
        {
            Width = width;
            Height = height;
            _frameQueue.Clear();
            ResetCurrentExposure();
        }
    }
    
    public void AddFrame(CapturedFrame frame)
    {
        lock (_lock)
        {
            if (_frameQueue.Count >= _maxQueueSize)
            {
                _frameQueue.Dequeue(); // Удаляем старый кадр
            }
            _frameQueue.Enqueue(frame);
            
            if (_isAccumulating)
            {
                if (_currentExposureFrames == null)
                {
                    _currentExposureFrames = new CapturedFrame[1000]; // Максимум 1000 кадров на экспозицию
                }
                
                if (_currentFrameCount < _currentExposureFrames.Length)
                {
                    _currentExposureFrames[_currentFrameCount++] = frame;
                    FrameAdded?.Invoke(this, _currentFrameCount);
                }
            }
        }
    }
    
    public void StartNewExposure()
    {
        lock (_lock)
        {
            ResetCurrentExposure();
            _isAccumulating = true;
            _exposureStartTime = DateTime.Now;
        }
    }
    
    public StackedResult? FinishExposure(double requestedExposureMs)
    {
        lock (_lock)
        {
            _isAccumulating = false;
            
            if (_currentExposureFrames == null || _currentFrameCount == 0)
            {
                return null;
            }
            
            var framesToProcess = _currentExposureFrames.Take(_currentFrameCount).ToArray();
            var result = ProcessFrames(framesToProcess, requestedExposureMs);
            
            ResetCurrentExposure();
            
            return result;
        }
    }
    
    public void CancelExposure()
    {
        lock (_lock)
        {
            _isAccumulating = false;
            ResetCurrentExposure();
        }
    }
    
    private void ResetCurrentExposure()
    {
        _currentExposureFrames = null;
        _currentFrameCount = 0;
    }
    
    private StackedResult ProcessFrames(CapturedFrame[] frames, double requestedExposureMs)
    {
        if (frames.Length == 0 || Width == 0 || Height == 0)
        {
            return new StackedResult();
        }
        
        int pixelCount = Width * Height;
        var accumulated = new double[pixelCount];
        double totalExposureMs = 0;
        
        // Оценка выдержки одного кадра (если нет точных данных)
        // Используем среднее время между кадрами или запрошенную экспозицию / количество кадров
        double estimatedFrameExposureMs = requestedExposureMs > 0 
            ? requestedExposureMs / frames.Length 
            : 33.33; // ~30 FPS по умолчанию
        
        foreach (var frame in frames)
        {
            if (frame.ImageData.Length != pixelCount)
            {
                continue; // Пропускаем кадр с неверным размером
            }
            
            for (int i = 0; i < pixelCount; i++)
            {
                accumulated[i] += frame.ImageData[i];
            }
            
            totalExposureMs += estimatedFrameExposureMs;
        }
        
        ushort[] resultData;
        
        if (Mode == StackingMode.Sum)
        {
            resultData = new ushort[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                // Ограничение максимальным значением ushort (65535)
                long value = (long)accumulated[i];
                resultData[i] = (ushort)Math.Min(value, 65535);
            }
        }
        else // Average
        {
            resultData = new ushort[pixelCount];
            double avgFactor = frames.Length > 0 ? 1.0 / frames.Length : 1.0;
            for (int i = 0; i < pixelCount; i++)
            {
                long value = (long)(accumulated[i] * avgFactor);
                resultData[i] = (ushort)Math.Min(value, 65535);
            }
        }
        
        return new StackedResult
        {
            ImageData = resultData,
            Width = Width,
            Height = Height,
            FramesStacked = frames.Length,
            TotalExposureMs = totalExposureMs,
            CompletionTime = DateTime.Now
        };
    }
    
    public int GetQueuedFrameCount()
    {
        lock (_lock) => _frameQueue.Count;
    }
    
    public CapturedFrame? GetLatestFrame()
    {
        lock (_lock)
        {
            return _frameQueue.Count > 0 ? _frameQueue.Last() : null;
        }
    }
    
    public void Dispose()
    {
        lock (_lock)
        {
            _frameQueue.Clear();
            ResetCurrentExposure();
        }
    }
}
