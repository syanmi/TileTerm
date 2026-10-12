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
echo (this window closes with the app; it stays open only on a non-zero exit code)
echo.

"%EXE%"
set "APP_EXIT=%ERRORLEVEL%"

rem Normal exit closes this window too. Keep it open on a crash / non-zero
rem exit so the exit code (and any console output) can still be read.
rem (No parenthesized block here: a ")" inside an echo would end the block early.)
if "%APP_EXIT%"=="0" goto :done
echo.
echo TileTerm exited abnormally. (exit code: %APP_EXIT%)
pause

:done
endlocal & exit /b %APP_EXIT%
