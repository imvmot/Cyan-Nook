@echo off
REM Unityroomアップロード用ビルドファイル準備
REM unityroom\build\ 内の .unityweb ファイルを .gz にリネーム

set DIR=unityroom\build

if not exist "%DIR%" (
    echo Error: %DIR% not found. Build for Unityroom first.
    exit /b 1
)

echo Renaming build files in %DIR%...

ren "%DIR%\unityroom.data.unityweb"            "unityroom.data.gz"
ren "%DIR%\unityroom.framework.js.unityweb"    "unityroom.framework.js.gz"
ren "%DIR%\unityroom.wasm.unityweb"            "unityroom.wasm.gz"

echo.
echo Done! Files in %DIR%:
dir /B "%DIR%"
