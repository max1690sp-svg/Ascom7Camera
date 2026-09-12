using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASCOM.DeviceInterface;
using Stacker.ASCOM;
using Stacker.Core;

namespace Stacker.UI
{
    public partial class MainWindow : Window
    {
        private Camera? _ascomCamera;
        private ICameraController? _cameraController;
        private FrameStacker? _frameStacker;
        private DispatcherTimer _uiTimer;
        private bool _isInitialized;

        public MainWindow()
        {
            InitializeComponent();
            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _uiTimer.Tick += UiTimer_Tick;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) return;
            try
            {
                _frameStacker = new FrameStacker();
                _ascomCamera = new Camera();
                await LoadCamerasAsync();
                _uiTimer.Start();
                _isInitialized = true;
                UpdateStatus("Готов к работе", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadCamerasAsync()
        {
            var cmb = FindName("CmbCameras") as ComboBox;
            var btn = FindName("BtnConnect") as Button;
            if (cmb == null || btn == null) return;

            cmb.ItemsSource = null;
            btn.IsEnabled = false;

            var cameras = new List<CameraInfo>
            {
                new CameraInfo { Id = "test", Name = "Тестовая камера (Симулятор)" }
            };
            cmb.ItemsSource = cameras;
            cmb.SelectedIndex = 0;
            btn.IsEnabled = true;
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            var btn = FindName("BtnConnect") as Button;
            if (_cameraController != null)
            {
                await DisconnectCameraAsync();
                return;
            }

            var cmb = FindName("CmbCameras") as ComboBox;
            if (cmb?.SelectedItem is not CameraInfo selectedCamera || btn == null) return;

            try
            {
                btn.IsEnabled = false;
                UpdateStatus("Подключение...", false);

                _cameraController = new TestFrameGenerator();
                _cameraController.ConnectionStateChanged += Controller_ConnectionStateChanged;
                _cameraController.FrameCaptured += Controller_FrameCaptured;
                _cameraController.ErrorOccurred += Controller_ErrorOccurred;

                var mode = new CameraMode(640, 480, "YUY2", 30.0);
                await _cameraController.ConnectAsync(selectedCamera.Id, mode);
                UpdateStatus($"Подключено: {selectedCamera.Name}", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка");
                btn.IsEnabled = true;
            }
        }

        private async Task DisconnectCameraAsync()
        {
            if (_cameraController == null) return;
            try
            {
                await _cameraController.DisconnectAsync();
                _cameraController.ConnectionStateChanged -= Controller_ConnectionStateChanged;
                _cameraController.FrameCaptured -= Controller_FrameCaptured;
                _cameraController.ErrorOccurred -= Controller_ErrorOccurred;
                _cameraController = null;
                UpdateStatus("Отключено", false);
            }
            finally
            {
                var btn = FindName("BtnConnect") as Button;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private void Controller_ConnectionStateChanged(object? sender, CameraConnectionState e)
        {
            Dispatcher.Invoke(() =>
            {
                var btn = FindName("BtnConnect") as Button;
                var grp = FindName("GrpCameraSettings") as GroupBox;
                
                // Используем поле структуры напрямую
                bool isConnected = e.Connected; 

                if (btn != null) btn.Content = isConnected ? "Отключить" : "Подключить";
                if (grp != null) grp.IsEnabled = !isConnected;
            });
        }

        private void Controller_FrameCaptured(object? sender, CapturedFrame e)
        {
            _frameStacker?.AddFrame(e);

            Dispatcher.Invoke(() =>
            {
                var img = FindName("ImgCurrent") as Image;
                var lblFps = FindName("LblFps") as TextBlock;

                if (img != null) img.Source = ConvertToBitmapSource(e);
                if (lblFps != null) lblFps.Text = $"FPS: {e.Fps:F1}";
            });
        }

        private void Controller_ErrorOccurred(object? sender, string e) => UpdateStatus($"Ошибка: {e}", true);

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (_ascomCamera == null || !_ascomCamera.Connected)
            {
                var lbl = FindName("LblAscomStatus") as TextBlock;
                if (lbl != null) lbl.Text = "ASCOM: Отключено";
                return;
            }

            var state = _ascomCamera.CameraState;
            var lblAscom = FindName("LblAscomStatus") as TextBlock;
            if (lblAscom != null) lblAscom.Text = $"ASCOM: {state}";

            if (state == CameraStates.cameraExposing)
            {
                var prb = FindName("PrbExposure") as ProgressBar;
                var lblExp = FindName("LblExposureInfo") as TextBlock;
                var progress = _ascomCamera.PercentCompleted;
                
                if (prb != null) prb.Value = progress;
                if (lblExp != null) lblExp.Text = $"Экспозиция: {_ascomCamera.LastExposureDuration:F2}s ({progress}%)";
            }
            else
            {
                var prb = FindName("PrbExposure") as ProgressBar;
                if (prb != null) prb.Value = 0;
            }
        }

        private void UpdateStatus(string message, bool isError)
        {
            Dispatcher.Invoke(() =>
            {
                var lbl = FindName("LblStatus") as TextBlock;
                if (lbl != null)
                {
                    lbl.Text = message;
                    lbl.Foreground = isError ? Brushes.Red : Brushes.Black;
                }
            });
        }

        private void BtnStartExposure_Click(object sender, RoutedEventArgs e)
        {
            if (_ascomCamera == null || !_ascomCamera.Connected)
            {
                MessageBox.Show("ASCOM не подключен", "Ошибка");
                return;
            }
            var txt = FindName("TxtExposure") as TextBox;
            if (txt == null || !double.TryParse(txt.Text, out double duration))
            {
                MessageBox.Show("Неверный формат", "Ошибка");
                return;
            }
            try
            {
                _ascomCamera.StartExposure(duration, true);
                UpdateStatus($"Начата экспозиция {duration}s", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка");
            }
        }

        private void BtnStopExposure_Click(object sender, RoutedEventArgs e)
        {
            _ascomCamera?.StopExposure();
            UpdateStatus("Остановлено", false);
        }

        private void BtnConnectAscom_Click(object sender, RoutedEventArgs e)
        {
            if (_ascomCamera == null) return;
            try
            {
                var btn = sender as Button;
                if (_ascomCamera.Connected)
                {
                    _ascomCamera.Connected = false;
                    if (btn != null) btn.Content = "Подключить ASCOM";
                }
                else
                {
                    _ascomCamera.Connected = true;
                    if (btn != null) btn.Content = "Отключить ASCOM";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка");
            }
        }

        private BitmapSource ConvertToBitmapSource(CapturedFrame frame)
        {
            return new WriteableBitmap(10, 10, 96, 96, PixelFormats.Gray8, null);
        }
    }

    public class CameraInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }
}
