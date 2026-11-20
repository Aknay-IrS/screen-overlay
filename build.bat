@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find csc.exe. .NET Framework 4.x is built into Windows 10/11 - make sure it is enabled.
  pause
  exit /b 1
)

rem Close a running copy so the exe can be replaced
taskkill /im OverlayLock.exe >nul 2>&1

echo Building OverlayLock.exe ...
"%CSC%" /nologo /target:winexe /optimize+ /out:OverlayLock.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll OverlayLock.cs
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)

echo Creating "Lock Screen" shortcut on your Desktop ...
rem Paths are passed through environment variables so folder names with quotes can't break the command
set "OL_DIR=%~dp0"
powershell -NoProfile -NonInteractive -Command ^
  "$d=$env:OL_DIR; $s=(New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Lock Screen.lnk'));" ^
  "$s.TargetPath=(Join-Path $d 'OverlayLock.exe'); $s.Arguments='/lock'; $s.WorkingDirectory=$d;" ^
  "$s.IconLocation=(Join-Path $env:WINDIR 'System32\imageres.dll')+',54'; $s.Save()"

echo.
echo Done. Starting OverlayLock - look for the shield icon in the system tray.
start "" "%~dp0OverlayLock.exe"
endlocal
