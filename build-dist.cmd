@echo off
setlocal
pushd "%~dp0"

set "CONFIG=Release"
set "DIST=%~dp0dist"

echo ============================================
echo  Build EverDefault (%CONFIG%) and refresh dist
echo ============================================
echo.

echo [1/3] Building solution...
dotnet build EverDefault.slnx -c %CONFIG% -v minimal
if errorlevel 1 (
    echo.
    echo [FAILED] Build failed. dist was not updated.
    popd
    exit /b 1
)

echo.
echo [2/3] Preparing dist directory...
if not exist "%DIST%" mkdir "%DIST%"

echo.
echo [3/3] Copying files to dist...

set "APP=src\EverDefault.App\bin\%CONFIG%\net48"
set "CORE=src\EverDefault.Core\bin\%CONFIG%\net48"
set "IPC=src\EverDefault.Ipc\bin\%CONFIG%\net48"
set "PERSIST=src\EverDefault.Persistence\bin\%CONFIG%\net48"
set "REG=src\EverDefault.Registry\bin\%CONFIG%\net48"
set "SVC=src\EverDefault.Service\bin\%CONFIG%\net48"
set "PACK=packaging"

for %%F in (
    "%APP%\EverDefault.App.exe"
    "%APP%\EverDefault.App.exe.config"
    "%APP%\Newtonsoft.Json.dll"
    "%CORE%\EverDefault.Core.dll"
    "%IPC%\EverDefault.Ipc.dll"
    "%PERSIST%\EverDefault.Persistence.dll"
    "%REG%\EverDefault.Registry.dll"
    "%SVC%\EverDefault.Service.exe"
    "%SVC%\EverDefault.Service.exe.config"
    "%PACK%\README.txt"
    "%PACK%\install-service.cmd"
    "%PACK%\run-console.cmd"
    "%PACK%\uninstall-service.cmd"
    "%PACK%\sample-rules.json"
) do (
    copy /y "%%~F" "%DIST%\" >nul
    if errorlevel 1 (
        echo.
        echo [FAILED] File is in use: %%~nxF
        echo          Exit EverDefault.App.exe ^(tray icon - right click - Exit^) and stop the
        echo          EverDefault service, then run this script again.
        popd
        exit /b 1
    )
)

echo.
echo [DONE] dist updated:
echo   %DIST%
echo.
dir /b "%DIST%"
echo.
popd
exit /b 0
