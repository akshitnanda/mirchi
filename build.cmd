@echo off
setlocal
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /out:Mirchi.exe Mirchi.cs
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)
echo Built %CD%\Mirchi.exe
