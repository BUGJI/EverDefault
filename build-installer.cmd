@echo off
setlocal
pushd "%~dp0"

echo ============================================
echo  Build EverDefault installer (Inno Setup)
echo ============================================
echo.

echo [1/2] Building dist payload...
call build-dist.cmd
if errorlevel 1 (
    echo.
    echo [FAILED] build-dist.cmd did not succeed. Installer not built.
    popd
    exit /b 1
)

echo.
echo [2/2] Compiling installer...

set "ISCC="
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 5\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 5\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 5\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 5\ISCC.exe"

where ISCC.exe >nul 2>nul
if not errorlevel 1 if not defined ISCC set "ISCC=ISCC.exe"

if not defined ISCC (
    echo [FAILED] Inno Setup ^(ISCC.exe^) was not found.
    echo          Install Inno Setup 6 from https://jrsoftware.org/isdl.php and run again.
    popd
    exit /b 1
)

echo Using compiler: %ISCC%
"%ISCC%" "installer\EverDefault.iss"
if errorlevel 1 (
    echo.
    echo [FAILED] Inno Setup compile failed.
    popd
    exit /b 1
)

echo.
echo [DONE] Installer written to:
echo   %~dp0installer\Output
echo.
dir /b "installer\Output"
echo.
popd
exit /b 0
