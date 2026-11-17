@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto nocsc

rem Build to project root first: dist has a Medium integrity label, so a
rem low-integrity process cannot create files there directly.
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /debug- /win32manifest:src\app.manifest /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:TranslatePop.new.exe src\TranslatePop.cs
if errorlevel 1 goto buildfail

if not exist dist mkdir dist
if not exist "dist\TranslatePop.exe" goto copydirect
move /y "dist\TranslatePop.exe" "dist\TranslatePop.exe.old" >nul 2>&1
if exist "dist\TranslatePop.exe" goto busy

:copydirect
copy /y "TranslatePop.new.exe" "dist\TranslatePop.exe" >nul
if errorlevel 1 goto busy
echo [ok] dist\TranslatePop.exe
exit /b 0

:busy
echo [warn] dist\TranslatePop.exe is in use or not writable.
echo        New build is at TranslatePop.new.exe - quit the tray app and run install.cmd
exit /b 1

:buildfail
echo [x] build failed
exit /b 1

:nocsc
echo [x] csc.exe missing (.NET Framework 4.x)
exit /b 1
