using System.Reflection;
using System.Runtime.InteropServices;

namespace Stacker.ASCOM;

/// <summary>
/// Утилиты для регистрации ASCOM-драйвера в COM
/// </summary>
public static class RegistrationHelper
{
    private const string AscomDriverKeyName = @"Software\ASCOM\Stacker.ASCOM.Camera";
    
    /// <summary>
    /// Зарегистрировать драйвер в ASCOM Platform
    /// </summary>
    public static void RegisterDriver()
    {
        try
        {
            // Регистрация COM-объекта
            var assembly = Assembly.GetExecutingAssembly();
            var type = typeof(Camera);
            
            // Получаем ProgID и GUID из атрибутов
            var progIdAttr = (ProgIdAttribute)Attribute.GetCustomAttribute(type, typeof(ProgIdAttribute))!;
            var guidAttr = (GuidAttribute)Attribute.GetCustomAttribute(type, typeof(GuidAttribute))!;
            
            string progId = progIdAttr.Value;
            Guid clsid = new Guid(guidAttr.Value);
            
            // Регистрация в реестре Windows
            Microsoft.Win32.Registry.SetValue(
                $@"HKEY_CLASSES_ROOT\CLSID\{{{clsid}}}",
                "",
                "UVC Stacker Camera for PHD2");
            
            Microsoft.Win32.Registry.SetValue(
                $@"HKEY_CLASSES_ROOT\CLSID\{{{clsid}}}\InprocServer32",
                "",
                "mscoree.dll");
            
            Microsoft.Win32.Registry.SetValue(
                $@"HKEY_CLASSES_ROOT\CLSID\{{{clsid}}}\InprocServer32",
                "ThreadingModel",
                "Both");
            
            Microsoft.Win32.Registry.SetValue(
                $@"HKEY_CLASSES_ROOT\CLSID\{{{clsid}}}\InprocServer32",
                "Class",
                $"Stacker.ASCOM.Camera");
            
            Microsoft.Win32.Registry.SetValue(
                $@"HKEY_CLASSES_ROOT\CLSID\{{{clsid}}}\InprocServer32",
                "Assembly",
                assembly.FullName);
            
            Microsoft.Win32.Registry.SetValue(
                $@"HKEY_CLASSES_ROOT\CLSID\{{{clsid}}}\InprocServer32",
                "RuntimeVersion",
                "v4.0.30319");
            
            Microsoft.Win32.Registry.SetValue(
                $@"HKEY_CLASSES_ROOT\CLSID\{{{clsid}}}\InprocServer32",
                "CodeBase",
                assembly.Location);
            
            // Регистрация ProgID
            Microsoft.Win32.Registry.SetValue(
                $@"HKEY_CLASSES_ROOT\{progId}",
                "",
                "UVC Stacker Camera for PHD2");
            
            Microsoft.Win32.Registry.SetValue(
                $@"HKEY_CLASSES_ROOT\{progId}\CLSID",
                "",
                $"{{{clsid}}}");
            
            // Регистрация в ASCOM Chooser
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AscomDriverKeyName);
            key.SetValue("IsEnabled", 1);
            key.SetValue("DeviceName", "UVC Stacker Camera");
            key.SetValue("DeviceDescription", "Виртуальная камера с накоплением кадров для PHD2");
            key.SetValue("DriverVersion", "1.0.0");
            
            Console.WriteLine("Драйвер успешно зарегистрирован в ASCOM Platform");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка регистрации: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Отменить регистрацию драйвера
    /// </summary>
    public static void UnregisterDriver()
    {
        try
        {
            var type = typeof(Camera);
            var progIdAttr = (ProgIdAttribute)Attribute.GetCustomAttribute(type, typeof(ProgIdAttribute))!;
            var guidAttr = (GuidAttribute)Attribute.GetCustomAttribute(type, typeof(GuidAttribute))!;
            
            string progId = progIdAttr.Value;
            Guid clsid = new Guid(guidAttr.Value);
            
            // Удаление из реестра
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(AscomDriverKeyName, false);
            Microsoft.Win32.Registry.ClassesRoot.DeleteSubKeyTree($@"CLSID\{{{clsid}}}", false);
            Microsoft.Win32.Registry.ClassesRoot.DeleteSubKey(progId, false);
            
            Console.WriteLine("Драйвер успешно отменён в регистрации");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка отмены регистрации: {ex.Message}");
        }
    }
}
