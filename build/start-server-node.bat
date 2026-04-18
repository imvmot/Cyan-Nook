@echo off
chcp 65001 >nul 2>&1
title Cyan Nook - Local Server (Node.js)

set PORT=8080

echo ============================================
echo   Cyan Nook - Local WebGL Server (Node.js)
echo   http://localhost:%PORT%
echo   Press Ctrl+C to stop
echo ============================================
echo.

where node >nul 2>&1
if errorlevel 1 (
    echo ERROR: Node.js is not installed.
    echo Install Node.js 18+ from https://nodejs.org/
    pause
    exit /b 1
)

pushd "%~dp0"
node server.js
popd

echo.
echo Server stopped.
pause
