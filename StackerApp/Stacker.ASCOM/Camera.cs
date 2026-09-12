using System;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ASCOM;
using ASCOM.DeviceInterface;
using ASCOM.Utilities;

namespace Stacker.ASCOM
{
    [Guid("4a7b3c2d-1e5f-4a8b-9c3d-2e1f0a9b8c7d")]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ProgId("Stacker.ASCOM.Camera")]
    public class Camera : ICameraV3
    {
        #region Private fields

        private bool _connected;
        private readonly string _driverInfo = "UVC Stacker ASCOM Driver";
        private readonly string _driverVersion = "1.0.0";
        private readonly short _interfaceVersion = 3;
        private readonly string _name = "UVC Stacker Camera";
        private readonly ArrayList _supportedActions = new ArrayList();

        // Состояние экспозиции
        private CameraStates _cameraState = CameraStates.cameraIdle;
        private double _exposureDuration;
        private DateTime _exposureStartTime;
        private bool _imageReady;
        private object _imageArray;
        private CancellationTokenSource? _exposureCts;
        private Task? _exposureTask;

        // Параметры изображения
        private int _numX;
        private int _numY;
        private int _startX;
        private int _startY;
        private short _binX = 1;
        private short _binY = 1;

        // Параметры Gain/Offset
        private short _gain;
        private int _offset;
        private bool _fastReadout;
        private short _readoutMode;

        // Константы камеры
        private const int MaxCamX = 1920;
        private const int MaxCamY = 1080;
        private const double PixelSizeXVal = 5.86;
        private const double PixelSizeYVal = 5.86;
        private const short MaxGain = 100;
        private const short MinGain = 0;
        private const short MaxOffset = 100;
        private const short MinOffset = 0;
        private const double MaxExposure = 3600.0;
        private const double MinExposure = 0.001;

        #endregion

        #region Constructor

        public Camera()
        {
            _numX = MaxCamX;
            _numY = MaxCamY;
            _startX = 0;
            _startY = 0;
            _imageArray = new short[MaxCamX, MaxCamY];
        }

        #endregion

        #region Common Properties and Methods (ASCOM Base)

        public ArrayList SupportedActions => _supportedActions;

        public string Action(string actionName, string actionParameters)
        {
            throw new System.NotImplementedException("Action not implemented");
        }

        public void CommandBlind(string command, bool raw)
        {
            throw new System.NotImplementedException();
        }

        public bool CommandBool(string command, bool raw)
        {
            throw new System.NotImplementedException();
        }

        public string CommandString(string command, bool raw)
        {
            throw new System.NotImplementedException();
        }

        public void Dispose()
        {
            _connected = false;
            _exposureCts?.Cancel();
            _exposureCts?.Dispose();
        }

        public bool Connected
        {
            get => _connected;
            set
            {
                if (value == _connected) return;
                _connected = value;
                if (!_connected)
                {
                    _cameraState = CameraStates.cameraIdle;
                    _exposureCts?.Cancel();
                }
            }
        }

        public string Description => _name;

        public string DriverInfo => _driverInfo;

        public string DriverVersion => _driverVersion;

        public short InterfaceVersion => _interfaceVersion;

        public string Name => _name;

        #endregion

        #region ICamera Properties

        public short BayerOffsetX => 0;

        public short BayerOffsetY => 0;

        public float BitsPerPixel => 16.0f;

        public double CCDTemperature => 0.0;

        public CameraStates CameraState => _cameraState;

        public int CameraXSize => MaxCamX;

        public int CameraYSize => MaxCamY;

        public bool CanAbortExposure => true;

        public bool CanAsymmetricBin => false;

        public bool CanGetCoolerPower => false;

        public bool CanPulseGuide => false;

        public bool CanSetCCDTemperature => false;

        public bool CanStopExposure => true;

        public bool CanFastReadout => true;

        public double CoolerPower => 0.0;

        public bool CoolerOn
        {
            get => false;
            set { }
        }

        public double ElectronsPerADU => 1.0;

        public double FullWellCapacity => 65535.0;

        public bool HasShutter => false;

        public double HeatSinkTemperature => 0.0;

        public bool IsPulseGuiding => false;

        public double LastExposureDuration => _exposureDuration;

        public string LastExposureStartTime => _exposureStartTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

        public int MaxADU => 65535;

        public short MaxBinX => 1;

        public short MaxBinY => 1;

        public int NumX
        {
            get => _numX;
            set
            {
                if (value < 1 || value > CameraXSize) throw new InvalidValueException("NumX out of range");
                _numX = value;
            }
        }

        public int NumY
        {
            get => _numY;
            set
            {
                if (value < 1 || value > CameraYSize) throw new InvalidValueException("NumY out of range");
                _numY = value;
            }
        }

        public int StartX
        {
            get => _startX;
            set
            {
                if (value < 0 || value >= CameraXSize) throw new InvalidValueException("StartX out of range");
                _startX = value;
            }
        }

        public int StartY
        {
            get => _startY;
            set
            {
                if (value < 0 || value >= CameraYSize) throw new InvalidValueException("StartY out of range");
                _startY = value;
            }
        }

        public short BinX
        {
            get => _binX;
            set
            {
                if (value < 1 || value > MaxBinX) throw new InvalidValueException("BinX out of range");
                _binX = value;
            }
        }

        public short BinY
        {
            get => _binY;
            set
            {
                if (value < 1 || value > MaxBinY) throw new InvalidValueException("BinY out of range");
                _binY = value;
            }
        }

        public short Gain
        {
            get => _gain;
            set
            {
                if (value < MinGain || value > MaxGain) throw new InvalidValueException("Gain out of range");
                _gain = value;
            }
        }

        public short GainMax => MaxGain;

        public short GainMin => MinGain;

        public ArrayList Gains
        {
            get
            {
                var list = new ArrayList();
                for (short i = MinGain; i <= MaxGain; i += 10) list.Add(i);
                return list;
            }
        }

        public int Offset
        {
            get => _offset;
            set
            {
                if (value < MinOffset || value > MaxOffset) throw new InvalidValueException("Offset out of range");
                _offset = value;
            }
        }

        public int OffsetMax => MaxOffset;

        public int OffsetMin => MinOffset;

        public ArrayList Offsets
        {
            get
            {
                var list = new ArrayList();
                for (short i = MinOffset; i <= MaxOffset; i += 10) list.Add(i);
                return list;
            }
        }

        public double PixelSizeX => PixelSizeXVal;

        public double PixelSizeY => PixelSizeYVal;

        public short ReadoutMode
        {
            get => _readoutMode;
            set
            {
                if (value < 0 || value >= ReadoutModes.Count) throw new InvalidValueException("ReadoutMode out of range");
                _readoutMode = value;
            }
        }

        public ArrayList ReadoutModes => new ArrayList { "Mode 0 (Default)" };

        public string SensorName => "UVC Sensor";

        public SensorType SensorType => SensorType.Monochrome;

        public double SetCCDTemperature
        {
            get => 0.0;
            set { throw new System.NotImplementedException(); }
        }

        public double ExposureMax => MaxExposure;

        public double ExposureMin => MinExposure;

        public double ExposureResolution => 0.001;

        public bool FastReadout
        {
            get => _fastReadout;
            set => _fastReadout = value;
        }

        public short PercentCompleted => (short)(_cameraState == CameraStates.cameraExposing ? 50 : 100);

        public object ImageArray
        {
            get
            {
                if (!_imageReady) throw new System.InvalidOperationException("Image not ready");
                return _imageArray;
            }
        }

        public object ImageArrayVariant
        {
            get
            {
                if (!_imageReady) throw new System.InvalidOperationException("Image not ready");
                return _imageArray;
            }
        }

        public bool ImageReady => _imageReady;

        public double SubExposureDuration
        {
            get => _exposureDuration;
            set
            {
                if (value < ExposureMin || value > ExposureMax) throw new InvalidValueException("Exposure duration out of range");
                _exposureDuration = value;
            }
        }

        #endregion

        #region ICamera Methods

        public void AbortExposure()
        {
            if (_cameraState != CameraStates.cameraExposing) return;
            _exposureCts?.Cancel();
            _cameraState = CameraStates.cameraIdle;
            _imageReady = false;
        }

        public void PulseGuide(GuideDirections direction, int duration)
        {
            throw new System.NotImplementedException("Pulse guiding is not supported by this camera.");
        }

        public void StartExposure(double duration, bool light)
        {
            if (_cameraState == CameraStates.cameraExposing)
                throw new System.InvalidOperationException("Exposure already in progress");

            if (duration < ExposureMin || duration > ExposureMax)
                throw new InvalidValueException("Invalid exposure duration");

            _exposureDuration = duration;
            _exposureStartTime = DateTime.UtcNow;
            _imageReady = false;
            _cameraState = CameraStates.cameraExposing;

            _exposureCts = new CancellationTokenSource();
            _exposureTask = Task.Run(() => SimulateExposure(duration, _exposureCts!.Token), _exposureCts.Token);
        }

        public void StopExposure()
        {
            if (_cameraState != CameraStates.cameraExposing) return;
            _exposureCts?.Cancel();
            _cameraState = CameraStates.cameraIdle;
            _imageReady = true;
        }

        public void SetupDialog()
        {
            // Пустая реализация
        }

        #endregion

        #region Private Helpers

        private void SimulateExposure(double duration, CancellationToken token)
        {
            try
            {
                var waitTime = TimeSpan.FromSeconds(duration);
                var startTime = DateTime.UtcNow;

                while (DateTime.UtcNow - startTime < waitTime)
                {
                    if (token.IsCancellationRequested)
                    {
                        _cameraState = CameraStates.cameraIdle;
                        return;
                    }
                    Thread.Sleep(100);
                }

                GenerateTestImage();

                _cameraState = CameraStates.cameraIdle;
                _imageReady = true;
            }
            catch (OperationCanceledException)
            {
                _cameraState = CameraStates.cameraIdle;
            }
            catch (System.Exception ex)
            {
                _cameraState = CameraStates.cameraError;
                System.Diagnostics.Debug.WriteLine($"Exposure error: {ex.Message}");
            }
        }

        private void GenerateTestImage()
        {
            var width = NumX;
            var height = NumY;
            var data = new short[width, height];

            var rand = new Random();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    data[x, y] = (short)(1000 + (x * 10) + (y * 10) + rand.Next(0, 100));
                }
            }
            _imageArray = data;
        }

        #endregion
    }
}
