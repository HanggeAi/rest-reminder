@echo off
rem 使用 Windows 自带的 .NET Framework 编译器构建，无需安装任何开发环境
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /win32icon:xiu.ico /resource:xiu.ico /out:RestReminder.exe /optimize+ /codepage:65001 /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll RestReminder.cs
if errorlevel 1 (
  echo 编译失败
) else (
  echo 构建成功: RestReminder.exe
)
pause
