using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
                // Инициализация ядра
                _frameStacker = new FrameStacker();
                
                // Инициализация ASCOM драйвера
                _ascomCamera = new Camera(); 
                
                // Подписка на события ядра
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
            if (_cameraController != null && _cameraController.IsConnected)
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

                // Получаем первый доступный режим камеры
                var modes = await _cameraController.GetAvailableModesAsync();
                if (modes.Count > 0)
                {
                    await _cameraController.ConnectAsync(selectedCamera.Id, modes[0]);
                }
                
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
                BtnConnect.Content = e.Connected ? "Отключить" : "Подключить";
                GrpCameraSettings.IsEnabled = !e.Connected;
            });
        }

        private void Controller_FrameCaptured(object? sender, CapturedFrame e)
        {
            _frameStacker?.AddFrame(e.ImageData, e.CaptureTime);
            
            Dispatcher.Invoke(() =>
            {
                ImgCurrent.Source = ImageHelper.ToBitmapSource(e.ImageData);
                LblFps.Text = $"FPS: {e.Fps:F1}";
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
                LblAscomStatus.Text = "ASCOM: Отключено";
                return;
            }

            var state = _ascomCamera.CameraState;
            LblAscomStatus.Text = $"ASCOM: {state}";
            
            if (state == ASCOM.DeviceInterface.CameraStates.cameraExposing)
            {
                var progress = _ascomCamera.PercentCompleted;
                PrbExposure.Value = progress;
                LblExposureInfo.Text = $"Экспозиция: {_ascomCamera.LastExposureDuration:F2}s ({progress}%)";
            }
            else
            {
                PrbExposure.Value = 0;
                if (_ascomCamera.ImageReady)
                {
                    LblExposureInfo.Text = "Изображение готово";
                }
            }
        }

        private void UpdateStatus(string message, bool isError)
        {
            Dispatcher.Invoke(() =>
            {
                LblStatus.Text = message;
                LblStatus.Foreground = isError ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Black;
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
                    BtnConnectAscom.Content = "Подключить ASCOM";
                    UpdateStatus("ASCOM отключено", false);
                }
                else
                {
                    _ascomCamera.Connected = true;
                    BtnConnectAscom.Content = "Отключить ASCOM";
                    UpdateStatus("ASCOM подключено", false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения ASCOM: {ex.Message}", "Ошибка");
            }
        }
    }

    public class CameraInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }

    public static class ImageHelper
    {
        public static BitmapSource ToBitmapSource(short[,] data)
        {
            int width = data.GetLength(0);
            int height = data.GetLength(1);
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray16, null);
            
            // Упрощенная копия данных (в продакшене использовать unsafe или Marshal)
            var flatData = new short[width * height];
            Buffer.BlockCopy(data, 0, flatData, 0, flatData.Length * sizeof(short));
            
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), flatData, width * sizeof(short), 0);
            return bitmap;
        }
    }
}
