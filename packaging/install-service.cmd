@echo off
setlocal
cd /d "%~dp0"
set SVC=EverDefault

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo [错误] 需要管理员权限。请右键此脚本 -^> "以管理员身份运行"。
    pause
    exit /b 1
)

sc query %SVC% >nul 2>&1
if "%errorlevel%"=="0" (
    echo 停止并删除旧服务...
    sc stop %SVC% >nul 2>&1
    timeout /t 2 /nobreak >nul
    sc delete %SVC% >nul 2>&1
)

echo 安装服务...
sc create %SVC% binPath= "\"%~dp0EverDefault.Service.exe\"" start= auto DisplayName= "EverDefault Registry Guard"
sc description %SVC% "监控并还原默认应用、This PC 命名空间与自定义注册表项。"
sc failure %SVC% reset= 86400 actions= restart/5000/restart/5000/restart/5000
sc start %SVC%

echo.
echo 完成。可运行 EverDefault.App.exe 打开托盘界面。
pause
