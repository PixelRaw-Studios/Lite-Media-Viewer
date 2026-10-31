@echo off
cd /d %~dp0
set CSC=%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
echo Using %CSC%
"%CSC%" /nologo /target:winexe /optimize+ /out:LiteMediaViewer.exe /win32icon:icon.ico /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.VisualBasic.dll Viewer.cs
if errorlevel 1 (echo BUILD FAILED) else (echo OK: LiteMediaViewer.exe created)
pause
