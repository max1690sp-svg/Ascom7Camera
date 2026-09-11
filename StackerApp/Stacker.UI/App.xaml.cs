using System.Windows;
using Stacker.ASCOM;

namespace Stacker.UI
{
    public partial class App : Application
    {
        private Camera? _ascomCamera;
        
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Инициализация ASCOM-драйвера при запуске
            InitializeAscomDriver();
        }
        
        protected override void OnExit(ExitEventArgs e)
        {
            CleanupAscomDriver();
            base.OnExit(e);
        }
        
        private void InitializeAscomDriver()
        {
            try
            {
                // В реальном приложении здесь будет общая шина данных между UI и ASCOM
                // Для демонстрации создаём отдельный экземпляр
                _ascomCamera = new Camera();
                
                // Регистрация драйвера в COM (только если нужно)
                // RegistrationHelper.RegisterDriver();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации ASCOM: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void CleanupAscomDriver()
        {
            try
            {
                _ascomCamera?.Dispose();
            }
            catch
            {
                // Игнорируем ошибки при закрытии
            }
        }
        
        public Camera? AscomCamera => _ascomCamera;
    }
}
