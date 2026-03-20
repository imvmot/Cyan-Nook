@echo off
REM Unityroomアップロード用ビルドファイル準備
REM build/Build/ から .unityweb ファイルをコピーし、拡張子を .gz に変換

set SRC=build\Build
set DST=unityroom

if not exist "%DST%" mkdir "%DST%"

echo Copying build files to %DST%...

copy /Y "%SRC%\build.data.unityweb"            "%DST%\build.data.gz"
copy /Y "%SRC%\build.framework.js.unityweb"    "%DST%\build.framework.js.gz"
copy /Y "%SRC%\build.wasm.unityweb"            "%DST%\build.wasm.gz"
copy /Y "%SRC%\build.loader.js"                "%DST%\build.loader.js"

echo.
echo Done! Files in %DST%:
dir /B "%DST%"
