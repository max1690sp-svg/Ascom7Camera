using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Stacker.Core;
using Stacker.ASCOM;

namespace Stacker.UI
{
    public partial class MainWindow : Window
    {
        private ICameraController? _cameraController;
        private readonly FrameStacker _frameStacker;
        private WriteableBitmap? _currentFrameBitmap;
        private WriteableBitmap? _stackingBitmap;
        private WriteableBitmap? _resultBitmap;
        
        private int _totalFramesReceived;
        private int _totalFramesIncluded;
        private int _totalFramesSkipped;
        private DateTime _lastFpsTime;
        private int _framesLastSecond;
        
        private CameraSettings _settings;
        private Camera? _ascomCamera;
        private System.Threading.Timer? _ascomUpdateTimer;
        
        public MainWindow()
        {
            InitializeComponent();
            
            _settings = CameraSettings.Load();
            _frameStacker = new FrameStacker();
            _frameStacker.Mode = _settings.StackingMode;
            
            // Установка настроек из сохранённых
            RdoSum.IsChecked = _settings.StackingMode == StackingMode.Sum;
            RdoAverage.IsChecked = _settings.StackingMode == StackingMode.Average;
            
            _frameStacker.FrameAdded += OnFrameAdded;
            _frameStacker.ResultReady += OnResultReady;
            
            Loaded += OnLoaded;
            Closing += OnClosing;
        }
        
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Инициализация ASCOM-драйвера
            _ascomCamera = new Camera(_frameStacker);
            StartAscomUpdateTimer();
            
            RefreshCameras();
        }
        
        private void StartAscomUpdateTimer()
        {
            _ascomUpdateTimer = new System.Threading.Timer(
                _ => UpdateAscomStatus(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(100));
        }
        
        private void UpdateAscomStatus()
        {
            if (_ascomCamera == null) return;
            
            Dispatcher.Invoke(() =>
            {
                SetAscomConnected(_ascomCamera.Connected);
                
                if (_ascomCamera.IsExposing)
                {
                    var progress = _ascomCamera.GetExposureProgress();
                    LblRequestedExposure.Text = $"{_ascomCamera.RequestedExposureMs:F0} мс";
                    PrgExposure.Value = Math.Min(100, progress * 100);
                    LblExposureProgress.Text = $"{PrgExposure.Value:F0}%";
                }
            });
        }
        
        private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _ascomUpdateTimer?.Dispose();
            _ascomCamera?.Disconnect();
            _ascomCamera?.Dispose();
            
            if (_cameraController != null)
            {
                await _cameraController.DisconnectAsync();
            }
            _frameStacker.Dispose();
            
            _settings.Save();
        }
        
        private async void BtnRefreshCameras_Click(object sender, RoutedEventArgs e)
        {
            await RefreshCameras();
        }
        
        private async Task RefreshCameras()
        {
            // Очистка списка и добавление тестовой камеры
            var cameras = new List<string> { "Test Camera (Simulator)" };
            
            // Попытка найти реальные UVC-камеры
            try
            {
                var uvcController = new UvcCameraController();
                var uvcCameras = await uvcController.GetAvailableCamerasAsync();
                cameras.AddRange(uvcCameras);
                uvcController.Dispose();
            }
            catch
            {
                // Игнорируем ошибки при поиске камер
            }
            
            CmbCameras.ItemsSource = cameras;
            if (cameras.Count > 0)
            {
                CmbCameras.SelectedIndex = 0;
            }
        }
        
        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (CmbCameras.SelectedItem is not string cameraId) return;
            
            // Выбор контроллера: тестовый или UVC
            if (cameraId.Contains("Test Camera"))
            {
                _cameraController = new TestFrameGenerator();
            }
            else
            {
                _cameraController = new UvcCameraController();
            }
            
            _cameraController.ConnectionStateChanged += OnConnectionStateChanged;
            _cameraController.FrameCaptured += OnFrameCaptured;
            _cameraController.ErrorOccurred += OnErrorOccurred;
            
            // Получение режимов
            var modes = await _cameraController.GetSupportedModesAsync(cameraId);
            CmbModes.ItemsSource = modes;
            if (modes.Count > 0)
            {
                // Выбор режима из настроек или первого доступного
                var selectedMode = modes.FirstOrDefault(m => 
                    m.Width == _settings.SelectedWidth && 
                    m.Height == _settings.SelectedHeight) ?? modes[0];
                CmbModes.SelectedItem = selectedMode;
            }
            
            if (CmbModes.SelectedItem is not CameraMode mode) return;
            
            BtnConnect.IsEnabled = false;
            var success = await _cameraController.ConnectAsync(cameraId, mode);
            
            if (success)
            {
                _frameStacker.Initialize(mode.Width, mode.Height);
                _cameraController.StartCapture();
                
                // Сохранение выбранной камеры и режима
                _settings.SelectedCameraId = cameraId;
                _settings.SelectedWidth = mode.Width;
                _settings.SelectedHeight = mode.Height;
            }
            
            BtnConnect.IsEnabled = true;
        }
        
        private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_cameraController != null)
            {
                _cameraController.ConnectionStateChanged -= OnConnectionStateChanged;
                _cameraController.FrameCaptured -= OnFrameCaptured;
                _cameraController.ErrorOccurred -= OnErrorOccurred;
                
                await _cameraController.DisconnectAsync();
                _cameraController.Dispose();
                _cameraController = null;
            }
            
            CmbModes.ItemsSource = null;
        }
        
        private async void BtnApplyExposure_Click(object sender, RoutedEventArgs e)
        {
            if (_cameraController == null) return;
            
            if (int.TryParse(TxtExposure.Text, out var exposure))
            {
                await _cameraController.SetExposureAsync(exposure, ChkAutoExposure.IsChecked ?? false);
            }
        }
        
        private async void BtnApplyGain_Click(object sender, RoutedEventArgs e)
        {
            if (_cameraController == null) return;
            
            if (int.TryParse(TxtGain.Text, out var gain))
            {
                await _cameraController.SetGainAsync(gain, ChkAutoGain.IsChecked ?? false);
            }
        }
        
        private void BtnApplyStackMode_Click(object sender, RoutedEventArgs e)
        {
            _frameStacker.Mode = RdoSum.IsChecked == true ? StackingMode.Sum : StackingMode.Average;
            _settings.StackingMode = _frameStacker.Mode;
        }
        
        private void OnConnectionStateChanged(object? sender, CameraConnectionState state)
        {
            Dispatcher.Invoke(() =>
            {
                LblStatus.Text = $"Статус: {GetStateName(state)}";
                BtnConnect.IsEnabled = state == CameraConnectionState.Disconnected;
                BtnDisconnect.IsEnabled = state == CameraConnectionState.Connected;
                CmbCameras.IsEnabled = state == CameraConnectionState.Disconnected;
                CmbModes.IsEnabled = state == CameraConnectionState.Disconnected;
            });
        }
        
        private string GetStateName(CameraConnectionState state) => state switch
        {
            CameraConnectionState.Disconnected => "Отключено",
            CameraConnectionState.Connecting => "Подключение...",
            CameraConnectionState.Connected => "Подключено",
            CameraConnectionState.Error => "Ошибка",
            _ => "Неизвестно"
        };
        
        private void OnFrameCaptured(object? sender, CapturedFrame frame)
        {
            _totalFramesReceived++;
            _framesLastSecond++;
            
            // Обновление статистики FPS
            var now = DateTime.Now;
            if ((now - _lastFpsTime).TotalSeconds >= 1.0)
            {
                Dispatcher.Invoke(() => LblFps.Text = $"FPS: {_framesLastSecond}");
                _framesLastSecond = 0;
                _lastFpsTime = now;
            }
            
            _frameStacker.AddFrame(frame);
            
            // Отображение текущего кадра
            UpdateCurrentFrameImage(frame);
            
            Dispatcher.Invoke(() =>
            {
                LblFrameStats.Text = $"Получено: {_totalFramesReceived} | Включено: {_totalFramesIncluded} | Пропущено: {_totalFramesSkipped}";
            });
        }
        
        private void OnFrameAdded(object? sender, int count)
        {
            _totalFramesIncluded = count;
        }
        
        private void OnResultReady(object? sender, StackedResult result)
        {
            // Обновление изображения накопления
            UpdateStackingImage(result);
            
            // Если ASCOM-драйвер ждёт результат экспозиции
            if (_ascomCamera?.IsExposing == true)
            {
                var elapsed = (DateTime.Now - _ascomCamera.ExposureStartTime).TotalMilliseconds;
                if (elapsed >= _ascomCamera.RequestedExposureMs)
                {
                    _ascomCamera.FinishExposure(result);
                    UpdateResultImage(result);
                }
            }
        }
        
        private void OnErrorOccurred(object? sender, string error)
        {
            Dispatcher.Invoke(() =>
            {
                LblStatus.Text = $"Ошибка: {error}";
            });
        }
        
        private void UpdateCurrentFrameImage(CapturedFrame frame)
        {
            if (_currentFrameBitmap == null || 
                _currentFrameBitmap.PixelWidth != frame.Width || 
                _currentFrameBitmap.PixelHeight != frame.Height)
            {
                _currentFrameBitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null);
                ImgCurrentFrame.Source = _currentFrameBitmap;
            }
            
            _currentFrameBitmap.Lock();
            try
            {
                _currentFrameBitmap.WritePixels(
                    new Int32Rect(0, 0, frame.Width, frame.Height),
                    frame.ImageData,
                    frame.Width,
                    0);
            }
            finally
            {
                _currentFrameBitmap.Unlock();
            }
        }
        
        public void UpdateStackingImage(StackedResult result)
        {
            if (_stackingBitmap == null ||
                _stackingBitmap.PixelWidth != result.Width ||
                _stackingBitmap.PixelHeight != result.Height)
            {
                _stackingBitmap = new WriteableBitmap(result.Width, result.Height, 96, 96, PixelFormats.Gray8, null);
                ImgStacking.Source = _stackingBitmap;
            }
            
            // Конвертация ushort -> byte для отображения
            var byteData = new byte[result.Width * result.Height];
            double maxVal = 1;
            foreach (var val in result.ImageData)
            {
                if (val > maxVal) maxVal = val;
            }
            double scale = maxVal > 0 ? 255.0 / maxVal : 1.0;
            
            for (int i = 0; i < byteData.Length; i++)
            {
                byteData[i] = (byte)Math.Min(255, result.ImageData[i] * scale);
            }
            
            _stackingBitmap.Lock();
            try
            {
                _stackingBitmap.WritePixels(
                    new Int32Rect(0, 0, result.Width, result.Height),
                    byteData,
                    result.Width,
                    0);
            }
            finally
            {
                _stackingBitmap.Unlock();
            }
            
            Dispatcher.Invoke(() =>
            {
                LblStackingInfo.Text = $"Кадров: {result.FramesStacked}\nЭкспозиция: {result.TotalExposureMs:F1} мс";
            });
        }
        
        public void UpdateResultImage(StackedResult result)
        {
            if (_resultBitmap == null ||
                _resultBitmap.PixelWidth != result.Width ||
                _resultBitmap.PixelHeight != result.Height)
            {
                _resultBitmap = new WriteableBitmap(result.Width, result.Height, 96, 96, PixelFormats.Gray8, null);
                ImgResult.Source = _resultBitmap;
            }
            
            // Конвертация ushort -> byte для отображения
            var byteData = new byte[result.Width * result.Height];
            double maxVal = 1;
            foreach (var val in result.ImageData)
            {
                if (val > maxVal) maxVal = val;
            }
            double scale = maxVal > 0 ? 255.0 / maxVal : 1.0;
            
            for (int i = 0; i < byteData.Length; i++)
            {
                byteData[i] = (byte)Math.Min(255, result.ImageData[i] * scale);
            }
            
            _resultBitmap.Lock();
            try
            {
                _resultBitmap.WritePixels(
                    new Int32Rect(0, 0, result.Width, result.Height),
                    byteData,
                    result.Width,
                    0);
            }
            finally
            {
                _resultBitmap.Unlock();
            }
            
            Dispatcher.Invoke(() =>
            {
                LblResultInfo.Text = $"Кадров: {result.FramesStacked}\nВсего экспозиция: {result.TotalExposureMs:F1} мс";
            });
        }
        
        public void UpdateExposureProgress(double requestedMs, double elapsedMs)
        {
            Dispatcher.Invoke(() =>
            {
                LblRequestedExposure.Text = $"{requestedMs:F0} мс";
                if (requestedMs > 0)
                {
                    PrgExposure.Value = Math.Min(100, (elapsedMs / requestedMs) * 100);
                    LblExposureProgress.Text = $"{PrgExposure.Value:F0}%";
                }
            });
        }
        
        public void SetAscomConnected(bool connected)
        {
            Dispatcher.Invoke(() =>
            {
                LblAscomStatus.Text = connected ? "ASCOM: Подключено" : "ASCOM: Не подключено";
                LblAscomStatus.Foreground = connected ? Brushes.Green : Brushes.Gray;
            });
        }
        
        public ICameraController CameraController => _cameraController;
        public FrameStacker FrameStacker => _frameStacker;
    }
}
