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
                
                // Инициализация ASCOM драйвера (теперь без аргументов)
                _ascomCamera = new Camera(); 
                
                // Подписка на события ядра для обновления UI
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
                // В реальной версии здесь будет поиск UVC камер через DirectShow/OpenCV
                // Для примера добавим тестовую камеру и заглушку
                var cameras = new List<CameraInfo>
                {
                    new CameraInfo { Id = "test", Name = "Тестовая камера (Симулятор)" },
                    // new CameraInfo { Id = "uvc1", Name = "USB Camera 1" } - раскомментировать при наличии
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

                // Выбор контроллера
                if (selectedCamera.Id == "test")
                {
                    _cameraController = new TestFrameGenerator();
                }
                else
                {
                    // Здесь будет реализация UvcCameraController
                    MessageBox.Show("Реальная UVC камера пока не подключена в демо-режиме.", "Инфо");
                    BtnConnect.IsEnabled = true;
                    return;
                }

                _cameraController.ConnectionStateChanged += Controller_ConnectionStateChanged;
                _cameraController.FrameCaptured += Controller_FrameCaptured;
                _cameraController.ErrorOccurred += Controller_ErrorOccurred;

                // Получаем первый доступный режим камеры (заглушка)
                var modes = await _cameraController.GetAvailableModesAsync();
                if (modes.Count > 0)
                {
                    await _cameraController.ConnectAsync(selectedCamera.Id, modes[0]);
                }
                else
                {
                     throw new Exception("Нет доступных режимов камеры");
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
                BtnConnect.Content = e.IsConnected ? "Отключить" : "Подключить";
                // GrpCameraSettings.IsEnabled = !e.IsConnected; // Проверить наличие в XAML
            });
        }

        private void Controller_FrameCaptured(object? sender, CapturedFrame e)
        {
            // Передача кадра в накопитель
            _frameStacker?.AddFrame(e.ImageData, e.Timestamp);
            
            Dispatcher.Invoke(() =>
            {
                // ImgCurrent.Source = ImageHelper.ToBitmapSource(e.ImageData); // Проверить наличие в XAML
                // LblFps.Content = $"FPS: {e.FramesPerSecond:F1}"; // Проверить наличие в XAML
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
                LblAscomStatus.Content = "ASCOM: Отключено";
                return;
            }

            // Чтение свойств ASCOM для отображения статуса
            var state = _ascomCamera.CameraState;
            LblAscomStatus.Content = $"ASCOM: {state}";
            
            if (state == ASCOM.DeviceInterface.CameraStates.cameraExposing)
            {
                var progress = _ascomCamera.PercentCompleted;
                // PrbExposure.Value = progress; // Проверить наличие в XAML
                // LblExposureInfo.Content = $"Экспозиция: {_ascomCamera.LastExposureDuration:F2}s ({progress}%)";
            }
            else
            {
                // PrbExposure.Value = 0;
                if (_ascomCamera.ImageReady)
                {
                    // LblExposureInfo.Content = "Изображение готово";
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
                // Запуск экспозиции через ASCOM интерфейс
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
        
        // Заглушки для отсутствующих обработчиков из XAML, чтобы компилятор не ругался
        private void BtnRefreshCameras_Click(object sender, RoutedEventArgs e) => LoadCamerasAsync().Wait();
        private void BtnDisconnect_Click(object sender, RoutedEventArgs e) => DisconnectCameraAsync().Wait();
        private void BtnApplyExposure_Click(object sender, RoutedEventArgs e) { /* Логика применения выдержки */ }
        private void BtnApplyGain_Click(object sender, RoutedEventArgs e) { /* Логика применения_gain */ }
        private void BtnApplyStackMode_Click(object sender, RoutedEventArgs e) { /* Логика применения режима сложения */ }
    }

    // Вспомогательный класс для списка камер
    public class CameraInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }

    // Простой конвертер для демонстрации (в реальном проекте вынести в отдельный файл)
    public static class ImageHelper
    {
        public static WriteableBitmap ToBitmapSource(short[,] data)
        {
            int width = data.GetLength(0);
            int height = data.GetLength(1);
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray16, null);
            
            // Копирование данных (требуется unsafe блок или Marshal для эффективности)
            // Здесь заглушка
            return bitmap;
        }
    }
}
