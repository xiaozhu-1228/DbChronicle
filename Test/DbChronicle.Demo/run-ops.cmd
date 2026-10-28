@echo off
chcp 65001 >nul
cd /d "%~dp0"
if not exist "bin\Debug\net8.0\DbChronicle.Demo.dll" (
    echo 未找到构建产物，请先执行: dotnet build
    pause
    exit /b 1
)
if "%~1"=="" (
    dotnet "bin\Debug\net8.0\DbChronicle.Demo.dll"
) else (
    dotnet "bin\Debug\net8.0\DbChronicle.Demo.dll" ops --dialect=%~1
)
set code=%ERRORLEVEL%
echo.
echo exit=%code%
pause
exit /b %code%
