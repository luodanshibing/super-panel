@echo off
cd /d %~dp0
echo [1/2] Building QuickPanel (Release, single-file exe)...
dotnet publish src\QuickPanel\QuickPanel.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
if errorlevel 1 (
  echo.
  echo BUILD FAILED. Check .NET 8 SDK is installed:  winget install Microsoft.DotNet.SDK.8
  pause
  exit /b 1
)
echo.
echo [2/2] Build OK. Output:
echo   src\QuickPanel\bin\Release\net8.0-windows\win-x64\publish\QuickPanel.exe
echo.
pause
