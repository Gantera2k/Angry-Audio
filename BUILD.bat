@echo off
echo Building Angry Audio...
echo.
echo --- Step 1: Building Angry Audio.exe ---
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:winexe /out:"Angry Audio.exe" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll ^
  /win32icon:"Angry Audio.ico" ^
  AppVersion.cs Audio.cs Controls.cs CorrectionToast.cs ^
  DarkTheme.cs DarkMessage.cs StarRenderer.cs StarBackground.cs ^
  Dpi.cs FadeOverlay.cs InstanceDialog.cs Logger.cs Mascot.cs MicStatusOverlay.cs ^
  OptionsForm.cs Program.cs PushToTalk.cs Settings.cs ToastStack.cs TrayApp.cs UpdateDialog.cs WelcomeForm.cs
if %ERRORLEVEL% NEQ 0 (echo Build FAILED. & pause & exit /b 1)
echo Build successful: Angry Audio.exe
echo.
echo --- Step 2: Building Angry_Audio_Setup.exe ---
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:winexe /out:"Angry_Audio_Setup.exe" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll ^
  /win32icon:"Angry Audio.ico" ^
  /win32manifest:app.manifest ^
  /resource:"Angry Audio.exe",app.exe ^
  /resource:"Angry Audio.ico",app.ico ^
  /resource:version.txt,version.txt ^
  Installer.cs Mascot.cs AppVersion.cs DarkTheme.cs DarkMessage.cs StarRenderer.cs StarBackground.cs Dpi.cs Logger.cs
if %ERRORLEVEL% NEQ 0 (echo Installer build FAILED. & pause & exit /b 1)
echo Build successful: Angry_Audio_Setup.exe
echo.
echo Done! Distribute Angry_Audio_Setup.exe only.
pause
