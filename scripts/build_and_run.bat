@echo off
setlocal

rem Resolve the repo root from this script's own location, so it works
rem no matter which directory it is launched from (double-click included).
set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%.."
set "SLN=%REPO_ROOT%\TileTerm.sln"
set "EXE=%REPO_ROOT%\src\TileTerm\bin\Debug\net9.0-windows\TileTerm.exe"

echo ============================================
echo  TileTerm: build and run
echo ============================================
echo.

dotnet build "%SLN%" -c Debug
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. See the errors above.
    pause
    exit /b 1
)

if not exist "%EXE%" (
    echo.
    echo [ERROR] Build succeeded but the executable was not found: %EXE%
    pause
    exit /b 1
)

echo.
echo Build OK. Launching TileTerm...
echo (this window will come back when you close the app)
echo.

"%EXE%"

echo.
echo TileTerm exited. (exit code: %ERRORLEVEL%)
pause

endlocal
