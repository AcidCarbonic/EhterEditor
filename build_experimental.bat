@echo off
setlocal enabledelayedexpansion
title Build & Run Pure C# Native Ether Editor

echo ========================================================
echo   Ether Editor Native (Pure C# WPF Desktop Application)
echo   Developer Build Script - Auto Detect MSBuild
echo ========================================================
echo.

:: 1. Turn off any running EtherEditorNative process
taskkill /f /im EtherEditorNative.exe >nul 2>&1

:: 2. Locate MSBuild executable
set MSBUILD=

:: Check Visual Studio 2022 / 2019 / 2017 via vswhere
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" (
    for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
        set "MSBUILD=%%i"
    )
)

:: Fallback to .NET Framework 64-bit MSBuild
if not defined MSBUILD (
    if exist "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" (
        set "MSBUILD=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
    ) else if exist "C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" (
        set "MSBUILD=C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
    )
)

if not defined MSBUILD (
    echo [ERROR] Visual Studio hoac .NET Framework MSBuild khong duoc tim thay tren may tinh!
    echo Vui long cai dat Visual Studio 2019/2022 hoac .NET Framework 4.8 SDK.
    pause
    exit /b 1
)

echo [INFO] Su dung MSBuild tai: "%MSBUILD%"
echo [INFO] Dang bien dich du an EtherEditorNative.csproj...
echo.

"%MSBUILD%" "%~dp0EtherEditorNative.csproj" /t:Rebuild /p:Configuration=Release /nologo /verbosity:minimal

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ========================================================
    echo [ERROR] BIEN DICH THAT BAI! Vui long kiem tra log o tren.
    echo ========================================================
    pause
    exit /b %ERRORLEVEL%
)

if exist "%~dp0bin\Release\EtherEditorNative.exe" (
    echo.
    echo ========================================================
    echo   [THANH CONG] Bien dich thanh cong 100%!
    echo   Executing: bin\Release\EtherEditorNative.exe
    echo ========================================================
    echo.
    start "" "%~dp0bin\Release\EtherEditorNative.exe"
) else (
    echo [ERROR] Khong tim thay file EtherEditorNative.exe sau khi bien dich.
    pause
)
