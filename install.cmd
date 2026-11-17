@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem ===========================================================================
rem  TranslatePop 一次性安装
rem
rem  1. 把 TranslatePop.new.exe 换到 dist\TranslatePop.exe
rem  2. 建一个「最高权限」计划任务 TranslatePopElevated（不会自己触发，只被 /Run）
rem  3. 保留你正在用的配置（热键等），并补上 elevate=auto
rem
rem  只有运行本脚本的这一刻会弹一次 UAC。之后双击 dist\TranslatePop.exe：
rem  不弹 UAC、不弹「无法验证发布者」，同时仍是管理员权限，
rem  能照常翻译以管理员运行的 IDEA。
rem ===========================================================================

if defined TPPOP_ELEVATED goto :elevated

powershell.exe -NoProfile -Command "if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { exit 0 } else { exit 1 }"
if "%errorlevel%"=="0" goto :elevated

echo 需要管理员权限，正在请求提权（只会弹这一次 UAC）...
powershell.exe -NoProfile -Command "$env:TPPOP_ELEVATED='1'; Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b 0

:elevated
echo ============================================================
echo  TranslatePop 一次性安装（当前已是管理员）
echo ============================================================
echo.

if not exist "TranslatePop.new.exe" goto nonew

echo [1/5] 结束正在运行的 TranslatePop ...
taskkill /IM TranslatePop.exe /F >nul 2>&1
taskkill /IM TranslatePopDev.exe /F >nul 2>&1
ping -n 3 127.0.0.1 >nul

echo [2/5] 替换 dist\TranslatePop.exe ...
if exist "dist\TranslatePop.exe" (
  move /y "dist\TranslatePop.exe" "dist\TranslatePop.exe.old" >nul 2>&1
)
if exist "dist\TranslatePop.exe" goto busy
copy /y "TranslatePop.new.exe" "dist\TranslatePop.exe" >nul
if not exist "dist\TranslatePop.exe" goto copyfail
echo     已替换。

echo [3/5] 保留现有配置 ...
if not exist "dist\data" mkdir "dist\data"
if exist "dist\data\config.ini" goto cfgkeep
if exist "%APPDATA%\TranslatePop\config.ini" goto cfgmigrate
echo     首次运行，程序会自己生成默认配置
goto cfgelev

:cfgkeep
copy /y "dist\data\config.ini" "dist\data\config.ini.bak" >nul 2>&1
echo     沿用 dist\data\config.ini（已备份为 config.ini.bak）
goto cfgelev

:cfgmigrate
echo     没有 dist\data\config.ini，从 %%APPDATA%%\TranslatePop 复制一份 ...
copy /y "%APPDATA%\TranslatePop\config.ini" "dist\data\config.ini" >nul

:cfgelev
if not exist "dist\data\config.ini" goto cfgdone
findstr /C:"elevate=" "dist\data\config.ini" >nul 2>&1
if not errorlevel 1 goto cfgdone
echo.>>"dist\data\config.ini"
echo elevate=auto>>"dist\data\config.ini"
echo     已补上 elevate=auto
:cfgdone
echo     其它设置（endpoint / 翻译方向 / 卡片样式）保持不变。


echo [4/5] 建立「静默提权」计划任务 ...
set "OUT=%TEMP%\TranslatePop-install.txt"
if exist "%OUT%" del /q "%OUT%" >nul 2>&1
start "" "dist\TranslatePop.exe" --install-task --out "%OUT%"

set /a n=0
:waitloop
if exist "%OUT%" goto gotit
set /a n+=1
if %n% geq 40 goto waittimeout
ping -n 2 127.0.0.1 >nul
goto waitloop

:gotit
ping -n 2 127.0.0.1 >nul
type "%OUT%"
findstr /C:"install-task=OK" "%OUT%" >nul
if errorlevel 1 goto installfail
echo     任务已建立，程序已以管理员身份启动。
goto verify

:waittimeout
echo [x] 等待超时：程序没有回写结果。
goto installfail

:installfail
echo.
echo [x] 计划任务没有建成功。
echo     请按顺序排查：
echo       1. 日志：dist\data\log.txt 里搜 install-task
echo       2. 任务：schtasks /Query /TN TranslatePopElevated /V /FO LIST
echo       3. 手工重建（本窗口已是管理员，直接粘贴执行）：
echo          schtasks /Create /TN TranslatePopElevated /TR "%~dp0dist\TranslatePop.exe" /SC MONTHLY /M 12 /D 31 /ST 23:59 /RL HIGHEST /F
echo    （不要加 /Change /DISABLE：禁用的任务 schtasks /Run 会拒绝启动）
pause
exit /b 1

:verify
echo [5/5] 校验任务 ...
schtasks /Query /TN TranslatePopElevated /FO LIST 2>nul
if errorlevel 1 (
  ping -n 3 127.0.0.1 >nul
  schtasks /Query /TN TranslatePopElevated /FO LIST 2>nul
)

echo.
echo ============================================================
echo  完成，可以关掉这个窗口了。
echo.
echo  之后：双击 %~dp0dist\TranslatePop.exe
echo        - 不弹 UAC
echo        - 不弹「无法验证发布者」
echo        - 仍是管理员权限，能翻译以管理员运行的 IDEA
echo.
echo  自检：右下角出现托盘图标、全程零弹窗 = 成功。
echo.
echo  想改回每次都弹 UAC：dist\data\config.ini 改成 elevate=on
echo  完全不要管理员权限：  elevate=off（但翻译不了管理员程序）
echo ============================================================
echo.
pause
exit /b 0

:busy
echo [x] 替换失败：dist\TranslatePop.exe 仍被占用。
echo     请在托盘图标上右键退出程序，然后重跑本脚本。
pause
exit /b 1

:copyfail
echo [x] 复制 TranslatePop.new.exe 失败。
pause
exit /b 1

:nonew
echo [x] 找不到 TranslatePop.new.exe，先编译：build.cmd
pause
exit /b 1
