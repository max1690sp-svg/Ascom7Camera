using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Stacker.Core;

namespace Stacker.UI
{
    public partial class MainWindow : Window
    {
        private readonly ICameraController _cameraController;
        private readonly FrameStacker _frameStacker;
        private WriteableBitmap? _currentFrameBitmap;
        private WriteableBitmap? _stackingBitmap;
        private WriteableBitmap? _resultBitmap;
        
        private int _totalFramesReceived;
        private int _totalFramesIncluded;
        private int _totalFramesSkipped;
        private DateTime _lastFpsTime;
        private int _framesLastSecond;
        
        public MainWindow()
        {
            InitializeComponent();
            
            _cameraController = new UvcCameraController();
            _frameStacker = new FrameStacker();
            
            _cameraController.ConnectionStateChanged += OnConnectionStateChanged;
            _cameraController.FrameCaptured += OnFrameCaptured;
            _cameraController.ErrorOccurred += OnErrorOccurred;
            
            _frameStacker.FrameAdded += OnFrameAdded;
            
            Loaded += OnLoaded;
            Closing += OnClosing;
        }
        
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshCameras();
        }
        
        private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            await _cameraController.DisconnectAsync();
            _frameStacker.Dispose();
        }
        
        private async void BtnRefreshCameras_Click(object sender, RoutedEventArgs e)
        {
            await RefreshCameras();
        }
        
        private async Task RefreshCameras()
        {
            var cameras = await _cameraController.GetAvailableCamerasAsync();
            CmbCameras.ItemsSource = cameras;
            if (cameras.Count > 0)
            {
                CmbCameras.SelectedIndex = 0;
            }
        }
        
        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (CmbCameras.SelectedItem is not string cameraId) return;
            if (CmbModes.SelectedItem is not CameraMode mode) return;
            
            BtnConnect.IsEnabled = false;
            var success = await _cameraController.ConnectAsync(cameraId, mode);
            
            if (success)
            {
                _frameStacker.Initialize(mode.Width, mode.Height);
                _cameraController.StartCapture();
            }
            
            BtnConnect.IsEnabled = true;
        }
        
        private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            await _cameraController.DisconnectAsync();
        }
        
        private async void BtnApplyExposure_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtExposure.Text, out var exposure))
            {
                await _cameraController.SetExposureAsync(exposure, ChkAutoExposure.IsChecked ?? false);
            }
        }
        
        private async void BtnApplyGain_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtGain.Text, out var gain))
            {
                await _cameraController.SetGainAsync(gain, ChkAutoGain.IsChecked ?? false);
            }
        }
        
        private void BtnApplyStackMode_Click(object sender, RoutedEventArgs e)
        {
            _frameStacker.Mode = RdoSum.IsChecked == true ? StackingMode.Sum : StackingMode.Average;
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
