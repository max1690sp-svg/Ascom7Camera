using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

                _frameStacker.FrameAdded += (s, args) => UpdateStatus("Кадр получен", false);
                _frameStacker.ResultReady += (s, args) => UpdateStatus("Результат готов", false);

                await LoadCamerasAsync();
                _uiTimer.Start();
                _isInitialized = true;
                UpdateStatus("Готов к работе", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadCamerasAsync()
        {
            CmbCameras.ItemsSource = null;
            BtnConnect.IsEnabled = false;

            try
            {
                var cameras = new List<CameraInfo>
                {
                    new CameraInfo { Id = "test", Name = "Тестовая камера (Симулятор)" }
                };

                CmbCameras.ItemsSource = cameras;
                CmbCameras.SelectedIndex = 0;
                BtnConnect.IsEnabled = true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка сканирования камер: {ex.Message}", true);
            }
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_cameraController != null)
            {
                await DisconnectCameraAsync();
                return;
            }

            if (CmbCameras.SelectedItem is not CameraInfo selectedCamera)
                return;

            try
            {
                BtnConnect.IsEnabled = false;
                UpdateStatus("Подключение...", false);

                if (selectedCamera.Id == "test")
                {
                    _cameraController = new TestFrameGenerator();
                }
                else
                {
                    MessageBox.Show("Реальная UVC камера пока не подключена в демо-режиме.", "Инфо");
                    BtnConnect.IsEnabled = true;
                    return;
                }

                _cameraController.ConnectionStateChanged += Controller_ConnectionStateChanged;
                _cameraController.FrameCaptured += Controller_FrameCaptured;
                _cameraController.ErrorOccurred += Controller_ErrorOccurred;

                // Создаем заглушку режима, если требуется
                var mode = new CameraMode(640, 480, "YUY2", 30.0);
                await _cameraController.ConnectAsync(selectedCamera.Id, mode);

                UpdateStatus($"Подключено: {selectedCamera.Name}", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnConnect.IsEnabled = true;
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
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка отключения: {ex.Message}", true);
            }
            finally
            {
                BtnConnect.IsEnabled = true;
            }
        }

        private void Controller_ConnectionStateChanged(object? sender, CameraConnectionState e)
        {
            Dispatcher.Invoke(() =>
            {
                bool isConnected = e.Connected;
                BtnConnect.Content = isConnected ? "Отключить" : "Подключить";

                if (FindName("GrpCameraSettings") is GroupBox grp)
                {
                    grp.IsEnabled = !isConnected;
                }
            });
        }

        private void Controller_FrameCaptured(object? sender, CapturedFrame e)
        {
            // Передача кадра в накопитель
            if (e.FrameData is short[,] grayData)
            {
                _frameStacker?.AddFrame(grayData);
            }

            Dispatcher.Invoke(() =>
            {
                if (FindName("ImgCurrent") is Image img)
                {
                    img.Source = ConvertToBitmapSource(e.FrameData);
                }

                if (FindName("LblFps") is TextBlock lblFps)
                {
                    lblFps.Text = $"FPS: {e.Fps:F1}";
                }
            });
        }

        private void Controller_ErrorOccurred(object? sender, string e)
        {
            UpdateStatus($"Ошибка камеры: {e}", true);
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (_ascomCamera == null || !_ascomCamera.Connected)
            {
                if (FindName("LblAscomStatus") is TextBlock lblStatus)
                    lblStatus.Text = "ASCOM: Отключено";
                return;
            }

            var state = _ascomCamera.CameraState;
            if (FindName("LblAscomStatus") is TextBlock lblAscom)
                lblAscom.Text = $"ASCOM: {state}";

            if (state == ASCOM.DeviceInterface.CameraStates.cameraExposing)
            {
                var progress = _ascomCamera.PercentCompleted;
                if (FindName("PrbExposure") is ProgressBar prb) prb.Value = progress;

                if (FindName("LblExposureInfo") is TextBlock lblExp)
                    lblExp.Text = $"Экспозиция: {_ascomCamera.LastExposureDuration:F2}s ({progress}%)";
            }
            else
            {
                if (FindName("PrbExposure") is ProgressBar prb) prb.Value = 0;
                if (_ascomCamera.ImageReady)
                {
                    if (FindName("LblExposureInfo") is TextBlock lblExp)
                        lblExp.Text = "Изображение готово";
                }
            }
        }

        private void UpdateStatus(string message, bool isError)
        {
            Dispatcher.Invoke(() =>
            {
                if (FindName("LblStatus") is TextBlock lbl)
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
                MessageBox.Show("ASCOM камера не подключена", "Ошибка");
                return;
            }

            if (!double.TryParse(TxtExposure.Text, out double duration))
            {
                MessageBox.Show("Неверный формат выдержки", "Ошибка");
                return;
            }

            try
            {
                _ascomCamera.StartExposure(duration, true);
                UpdateStatus($"Начата экспозиция {duration}s", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка старта: {ex.Message}", "Ошибка");
            }
        }

        private void BtnStopExposure_Click(object sender, RoutedEventArgs e)
        {
            _ascomCamera?.StopExposure();
            UpdateStatus("Экспозиция остановлена", false);
        }

        private void BtnConnectAscom_Click(object sender, RoutedEventArgs e)
        {
            if (_ascomCamera == null) return;

            try
            {
                if (_ascomCamera.Connected)
                {
                    _ascomCamera.Connected = false;
                    if (sender is Button btn) btn.Content = "Подключить ASCOM";
                    UpdateStatus("ASCOM отключено", false);
                }
                else
                {
                    _ascomCamera.Connected = true;
                    if (sender is Button btn) btn.Content = "Отключить ASCOM";
                    UpdateStatus("ASCOM подключено", false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения ASCOM: {ex.Message}", "Ошибка");
            }
        }

        private BitmapSource ConvertToBitmapSource(object? data)
        {
            if (data is byte[] rawBytes)
            {
                int width = 640;
                int height = 480;
                var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
                bitmap.WritePixels(new Int32Rect(0, 0, width, height), rawBytes, width, 0);
                return bitmap;
            }
            else if (data is short[,] shortData)
            {
                int width = shortData.GetLength(0);
                int height = shortData.GetLength(1);
                var bytes = new byte[width * height];

                short min = short.MaxValue, max = short.MinValue;
                foreach (var v in shortData) { if (v < min) min = v; if (v > max) max = v; }
                double range = max - min;
                if (range == 0) range = 1;

                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = (byte)((shortData[i % width, i / width] - min) / range * 255);
                }

                var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
                bitmap.WritePixels(new Int32Rect(0, 0, width, height), bytes, width, 0);
                return bitmap;
            }
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
