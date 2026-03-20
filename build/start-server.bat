@echo off
chcp 65001 >nul 2>&1
title Cyan Nook - Local Server

set PORT=8080

echo ============================================
echo   Cyan Nook - Local WebGL Server
echo   http://localhost:%PORT%
echo   Press Ctrl+C to stop
echo ============================================
echo.

powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0server.ps1" -Port %PORT%

echo.
echo Server stopped.
pause
