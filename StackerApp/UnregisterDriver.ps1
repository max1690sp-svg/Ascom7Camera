# Скрипт отмены регистрации ASCOM-драйвера UVC Stacker
# Запускать от имени администратора

$assemblyPath = "$PSScriptRoot\Stacker.ASCOM\bin\Release\net8.0-windows\Stacker.ASCOM.dll"

try {
    # Отмена регистрации через regasm
    $regasmPath = "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
    
    if (Test-Path $regasmPath) {
        & $regasmPath /unregister $assemblyPath
        Write-Host "Драйвер успешно отменён в регистрации через regasm" -ForegroundColor Green
    } else {
        Write-Host "RegAsm.exe не найден, пробуем вручную" -ForegroundColor Yellow
    }
    
    # Удаление из ASCOM
    $ascomKey = "HKCU:\Software\ASCOM\Stacker.ASCOM.Camera"
    if (Test-Path $ascomKey) {
        Remove-Item -Path $ascomKey -Recurse -Force
        Write-Host "Удалено из ASCOM Platform" -ForegroundColor Green
    }
    
    # Очистка CLSID из реестра
    $clsid = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890"
    $clsidPath = "HKCR:\CLSID\{$clsid}"
    if (Test-Path $clsidPath) {
        Remove-Item -Path $clsidPath -Recurse -Force
        Write-Host "CLSID удалён из реестра" -ForegroundColor Green
    }
    
    Write-Host "Отмена регистрации завершена" -ForegroundColor Green
    
} catch {
    Write-Host "Ошибка: $_" -ForegroundColor Red
    exit 1
}
