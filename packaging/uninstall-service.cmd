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
if not "%errorlevel%"=="0" (
    echo 服务未安装，无需卸载。
    pause
    exit /b 0
)

echo 停止服务...
sc stop %SVC% >nul 2>&1

rem 等待服务完全停止，避免出现“标记为删除”导致删除失败
for /l %%i in (1,1,20) do (
    sc query %SVC% | findstr /i "STOPPED" >nul && goto stopped
    ping -n 2 127.0.0.1 >nul
)
:stopped

echo 删除服务...
sc delete %SVC%
ping -n 3 127.0.0.1 >nul

sc query %SVC% >nul 2>&1
if "%errorlevel%"=="0" (
    echo [警告] 服务仍然存在（可能已被标记为删除）。请重启电脑后再确认。
) else (
    echo 完成。数据保留在 %ProgramData%\EverDefault。
)
pause
