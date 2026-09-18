@echo off
cd /d "%~dp0"
echo 以控制台模式启动服务（关闭此窗口即停止监控）。
echo 之后可运行 EverDefault.App.exe 连接本服务。
echo.
EverDefault.Service.exe --console
