using System;
using System.Collections;
using System.Runtime.InteropServices;
using ASCOM;
using ASCOM.DeviceInterface;
using Stacker.Core;

namespace Stacker.ASCOM;

/// <summary>
/// Виртуальная ASCOM-камера для интеграции с PHD2
/// </summary>
[Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
[ClassInterface(ClassInterfaceType.None)]
[ProgId("Stacker.ASCOM.Camera")]
public class Camera : ICameraV3
{
    private readonly FrameStacker _stacker;
    
    private bool _isConnected;
    private bool _isExposing;
    private DateTime _exposureStartTime;
    private double _requestedExposureMs;
    private StackedResult? _lastResult;
    private bool _imageReady;
    
    // Настройки камеры
    private int _cameraXSize = 640;
    private int _cameraYSize = 480;
    private short _binX = 1;
    private short _binY = 1;
    private int _startX = 0;
    private int _startY = 0;
    private int _numX = 640;
    private int _numY = 480;
    private short _readoutMode = 0;
    private short _gain = 0;
    private short _offset = 0;
    
    public Camera()
    {
        _stacker = new FrameStacker();
        _stacker.Initialize(_cameraXSize, _cameraYSize);
    }
    
    /// <summary>
    /// Конструктор для использования с UI приложением
    /// </summary>
    public Camera(FrameStacker sharedStacker)
    {
        _stacker = sharedStacker;
    }
    
    #region ASCOM Required Properties
    
    public string ActionName => string.Empty;
    
    public ArrayList SupportedActions => new ArrayList();
    
    public void CommandBlind(string command, bool raw) => throw new NotImplementedException();
    
    public bool CommandBool(string command, bool raw) => throw new NotImplementedException();
    
    public string CommandString(string command, bool raw) => throw new NotImplementedException();
    
    public void Dispose()
    {
        Disconnect();
        if (_stacker != null)
            _stacker.Dispose();
    }
    
    public bool Connected
    {
        get => _isConnected;
        set
        {
            if (value && !_isConnected)
                Connect();
            else if (!value && _isConnected)
                Disconnect();
        }
    }
    
    public string Description => "UVC Stacker Camera for PHD2";
    
    public string DriverInfo => "UVC Stacker Camera Driver v1.0.0";
    
    public string DriverVersion => "1.0.0";
    
    public short InterfaceVersion => 3;
    
    public string Name => "UVC Stacker Camera";
    
    public string Action(string action, string parameter) => string.Empty;
    
    public void SetupDialog() { }
    
    #endregion
    
    #region Camera Specific Properties
    
    public bool CanAbortExposure => true;
    
    public bool CanAsymmetricBin => false;
    
    public bool CanFastReadout => false;
    
    public bool CanGetCoolerPower => false;
    
    public bool CanPulseGuide => false; // Не поддерживаем гидирование через камеру
    
    public bool CanSetCCDTemperature => false;
    
    public bool CanStopExposure => true;
    
    public double CCDTemperature => -999.0; // Не поддерживается
    
    public double CoolerPower => 0.0;
    
    public double ElectronsPerADU => 1.0;
    
    public double FullWellCapacity => 65535.0;
    
    public bool HasShutter => false;
    
    public double HeatSinkTemperature => -999.0;
    
    public bool IsPulseGuiding => false;
    
    public double LastExposureDuration => _lastResult?.TotalExposureMs ?? 0.0;
    
    public string LastExposureStartTime => _lastResult != null ? "true" : "false";
    
    public string LastExposureStartTimeString => _lastResult?.CompletionTime.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? string.Empty;
    
    public int MaxADU => 65535;
    
    public short MaxBinX => 1;
    
    public short MaxBinY => 1;
    
    public int NumX
    {
        get => _numX;
        set
        {
            if (_isExposing) throw new PropertyNotAvailableException("Нельзя менять ROI во время экспозиции");
            _numX = Math.Min(value, _cameraXSize - _startX);
        }
    }
    
    public int NumY
    {
        get => _numY;
        set
        {
            if (_isExposing) throw new PropertyNotAvailableException("Нельзя менять ROI во время экспозиции");
            _numY = Math.Min(value, _cameraYSize - _startY);
        }
    }
    
    public int StartX
    {
        get => _startX;
        set
        {
            if (_isExposing) throw new PropertyNotAvailableException("Нельзя менять ROI во время экспозиции");
            _startX = Math.Min(value, _cameraXSize - _numX);
        }
    }
    
    public int StartY
    {
        get => _startY;
        set
        {
            if (_isExposing) throw new PropertyNotAvailableException("Нельзя менять ROI во время экспозиции");
            _startY = Math.Min(value, _cameraYSize - _numY);
        }
    }
    
    public short BinX
    {
        get => _binX;
        set
        {
            if (_isExposing) throw new PropertyNotAvailableException("Нельзя менять binning во время экспозиции");
            _binX = value;
        }
    }
    
    public short BinY
    {
        get => _binY;
        set
        {
            if (_isExposing) throw new PropertyNotAvailableException("Нельзя менять binning во время экспозиции");
            _binY = value;
        }
    }
    
    public double PixelSizeX => 5.0; // Значение по умолчанию, пользователь должен ввести реальное
    
    public double PixelSizeY => 5.0;
    
    public double SetCCDTemperature
    {
        get => -999.0;
        set { }
    }
    
    public SensorType SensorType => SensorType.Monochrome;
    
    public int CameraXSize => _cameraXSize;
    
    public int CameraYSize => _cameraYSize;
    
    public string Gains => string.Empty;
    
    public bool GainPresent => false;
    
    public short Gain
    {
        get => _gain;
        set
        {
            if (_isExposing) throw new PropertyNotAvailableException("Нельзя менять Gain во время экспозиции");
            _gain = value;
        }
    }
    
    public short GainMin => 0;
    
    public short GainMax => 0;
    
    public ArrayList GainsList => new ArrayList();
    
    public string Offsets => string.Empty;
    
    public bool OffsetPresent => false;
    
    public short Offset
    {
        get => _offset;
        set
        {
            if (_isExposing) throw new PropertyNotAvailableException("Нельзя менять Offset во время экспозиции");
            _offset = value;
        }
    }
    
    public short OffsetMin => 0;
    
    public short OffsetMax => 0;
    
    public ArrayList OffsetsList => new ArrayList();
    
    public double ExposureMin => 1.0 / 30.0; // Минимальная экспозиция ~1 кадр при 30 FPS
    
    public double ExposureMax => 3600.0; // Максимум 1 час
    
    public bool FastReadout
    {
        get => false;
        set { }
    }
    
    public short BayerOffsetX => 0;
    
    public short BayerOffsetY => 0;
    
    public double ExposureResolution => 0.001;
    
    public bool CoolerOn
    {
        get => false;
        set { }
    }
    
    public short ReadoutMode
    {
        get => _readoutMode;
        set
        {
            if (_isExposing) throw new PropertyNotAvailableException("Нельзя менять ReadoutMode во время экспозиции");
            _readoutMode = value;
        }
    }
    
    public ArrayList ReadoutModes => new ArrayList { "Default" };
    
    public string SensorName => "UVC Sensor";
    
    public double SubExposureDuration
    {
        get => _requestedExposureMs / 1000.0;
        set { }
    }
    
    public short PercentCompleted => _isExposing ? (short)(GetExposureProgress() * 100) : (short)100;
    
    public cameraState CameraState
    {
        get
        {
            if (!_isConnected) return cameraState.cameraIdle;
            if (_isExposing) return cameraState.cameraExposing;
            if (_imageReady) return cameraState.cameraIdle;
            return cameraState.cameraIdle;
        }
    }
    
    public void AbortExposure()
    {
        if (!_isExposing) return;
        _stacker.CancelExposure();
        _isExposing = false;
        _imageReady = false;
    }
    
    public void PulseGuide(GuideDirections direction, int duration)
    {
        throw new MethodNotImplementedException("Камера не поддерживает пульс-гидирование");
    }
    
    public void StartExposure(double duration, bool light)
    {
        if (_isExposing)
            throw new InvalidOperationException("Экспозиция уже выполняется");
        
        if (duration < ExposureMin || duration > ExposureMax)
            throw new InvalidValueException($"Длительность экспозиции должна быть от {ExposureMin} до {ExposureMax} секунд");
        
        _requestedExposureMs = duration * 1000.0;
        _exposureStartTime = DateTime.Now;
        _isExposing = true;
        _imageReady = false;
        _lastResult = null;
        
        _stacker.StartNewExposure();
    }
    
    public void StopExposure()
    {
        if (!_isExposing) return;
        _stacker.CancelExposure();
        _isExposing = false;
    }
    
    public object ImageArray
    {
        get
        {
            if (!_imageReady || _lastResult == null)
                throw new InvalidValueException("Изображение ещё не готово");
            
            _imageReady = false;
            
            // Возвращаем массив ushort в формате ASCOM
            var result = _lastResult.ImageData;
            var array = new ushort[result.Length];
            Array.Copy(result, array, result.Length);
            
            return array;
        }
    }
    
    public object ImageArrayVariant
    {
        get
        {
            return ImageArray;
        }
    }
    
    public bool ImageReady => _imageReady && _lastResult != null;
    
    #endregion
    
    #region Connection Management
    
    private void Connect()
    {
        if (_isConnected) return;
        
        // В реальной реализации здесь будет подключение к общей камере
        // из UI приложения
        _isConnected = true;
    }
    
    private void Disconnect()
    {
        if (!_isConnected) return;
        
        AbortExposure();
        _isConnected = false;
    }
    
    #endregion
    
    #region Internal Methods for UI Integration
    
    /// <summary>
    /// Вызывается UI приложением для обновления статуса экспозиции
    /// </summary>
    public void FinishExposure(StackedResult result)
    {
        _lastResult = result;
        _isExposing = false;
        _imageReady = true;
    }
    
    /// <summary>
    /// Проверка завершения экспозиции
    /// </summary>
    public void CheckExposureCompletion()
    {
        if (!_isExposing) return;
        
        var elapsed = (DateTime.Now - _exposureStartTime).TotalMilliseconds;
        
        // Экспозиция завершена, если прошло запрошенное время
        if (elapsed >= _requestedExposureMs)
        {
            var result = _stacker.FinishExposure(_requestedExposureMs);
            if (result != null)
            {
                _lastResult = result;
                _isExposing = false;
                _imageReady = true;
            }
        }
    }
    
    public StackedResult? GetCurrentResult() => _lastResult;
    
    public bool IsExposing => _isExposing;
    
    public DateTime ExposureStartTime => _exposureStartTime;
    
    public double RequestedExposureMs => _requestedExposureMs;
    
    public double GetExposureProgress()
    {
        if (!_isExposing) return 1.0;
        
        var elapsed = (DateTime.Now - _exposureStartTime).TotalMilliseconds;
        return Math.Min(1.0, elapsed / _requestedExposureMs);
    }
    
    #endregion
}
