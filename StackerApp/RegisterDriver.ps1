# Скрипт регистрации ASCOM-драйвера UVC Stacker
# Запускать от имени администратора

$assemblyPath = "$PSScriptRoot\Stacker.ASCOM\bin\Release\net8.0-windows\Stacker.ASCOM.dll"

if (-not (Test-Path $assemblyPath)) {
    Write-Host "Сборка не найдена. Сначала выполните: dotnet build -c Release" -ForegroundColor Red
    exit 1
}

try {
    # Загрузка сборки
    $assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
    
    # Получение типа Camera
    $cameraType = $assembly.GetType("Stacker.ASCOM.Camera")
    
    if ($null -eq $cameraType) {
        throw "Тип Stacker.ASCOM.Camera не найден"
    }
    
    # Регистрация через regasm эквивалент
    $regasmPath = "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
    
    if (Test-Path $regasmPath) {
        & $regasmPath /codebase $assemblyPath
        Write-Host "Драйвер успешно зарегистрирован через regasm" -ForegroundColor Green
    } else {
        throw "RegAsm.exe не найден"
    }
    
    # Дополнительная регистрация в ASCOM
    $ascomKey = "HKCU:\Software\ASCOM\Stacker.ASCOM.Camera"
    New-Item -Path $ascomKey -Force | Out-Null
    Set-ItemProperty -Path $ascomKey -Name "IsEnabled" -Value 1
    Set-ItemProperty -Path $ascomKey -Name "DeviceName" -Value "UVC Stacker Camera"
    Set-ItemProperty -Path $ascomKey -Name "DeviceDescription" -Value "Виртуальная камера с накоплением кадров для PHD2"
    Set-ItemProperty -Path $ascomKey -Name "DriverVersion" -Value "1.0.0"
    
    Write-Host "Драйвер зарегистрирован в ASCOM Platform" -ForegroundColor Green
    
} catch {
    Write-Host "Ошибка регистрации: $_" -ForegroundColor Red
    exit 1
}
