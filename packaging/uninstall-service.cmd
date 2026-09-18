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

echo 停止服务...
sc stop %SVC% >nul 2>&1
timeout /t 2 /nobreak >nul
echo 删除服务...
sc delete %SVC%
echo 完成。数据保留在 %ProgramData%\EverDefault。
pause
