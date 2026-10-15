@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
rem App name (Korean) lives in app.name so this file stays ASCII-only.
set /p APPNAME=<app.name
rem usage: build.bat [outdir]   (default: bin)
set OUT=%~1
if "%OUT%"=="" set OUT=bin
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
set CSC=%FW%\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found.
  exit /b 1
)

rem Text recognition (OCR) uses the Windows 10 OCR engine; compiling it needs the Windows SDK metadata file.
set WINMD=
for /d %%D in ("%ProgramFiles(x86)%\Windows Kits\10\UnionMetadata\10*") do if exist "%%D\Windows.winmd" set WINMD=%%D\Windows.winmd
if "%WINMD%"=="" (
  echo Windows.winmd not found. Install the Windows 10/11 SDK.
  exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"

rem If the exe is running, rename it to .old so the build can write a new one (the running app keeps working).
powershell -NoProfile -ExecutionPolicy Bypass -File tools\free_locked.ps1 "%OUT%" "%APPNAME%.exe"

rem The app icon is the official Mong Capture icon: a copy of the branding icon folder file (see CHANGELOG).
if not exist res\app.ico (
  echo res\app.ico not found. Copy the Mong Capture icon from the branding folder.
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 ^
  /win32icon:res\app.ico /win32manifest:res\app.manifest /resource:res\app.ico,MongCapture.app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:"%WINMD%" /r:"%FW%\System.Runtime.WindowsRuntime.dll" /r:"%FW%\System.Runtime.dll" ^
  /r:"%FW%\System.Threading.Tasks.dll" /r:"%FW%\System.IO.dll" /r:"%FW%\System.Runtime.InteropServices.WindowsRuntime.dll" ^
  /out:"%OUT%\%APPNAME%.exe" src\*.cs
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK: %OUT%\%APPNAME%.exe
