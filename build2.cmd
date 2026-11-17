@echo off
rem 只编译调试版：dist\TranslatePopDev.exe（不自动提权，便于对比 UIPI 行为）
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto nocsc
if not exist dist mkdir dist

rem 先编译到项目根，再拷进 dist：dist 有 Medium 完整性标签，
rem 低权限进程无法直接在其中创建文件。
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /debug- /win32manifest:src\app.dev.manifest /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:TranslatePopDev.new.exe src\TranslatePop.cs
if errorlevel 1 goto buildfail
copy /y "TranslatePopDev.new.exe" "dist\TranslatePopDev.exe" >nul
if errorlevel 1 echo [warn] dist\TranslatePopDev.exe not writable; build left at TranslatePopDev.new.exe
echo [ok] dist\TranslatePopDev.exe
exit /b 0

:buildfail
echo [x] build failed
exit /b 1

:nocsc
echo [x] csc.exe missing (.NET Framework 4.x)
exit /b 1
