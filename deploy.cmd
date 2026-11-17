@echo off
rem 轻量入口：真正的安装逻辑在 install.cmd（自提权、替换 exe、建静默提权任务、迁移配置）
cd /d "%~dp0"
call "%~dp0install.cmd"
