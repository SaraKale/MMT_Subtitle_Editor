@echo off
setlocal

REM ============================================================
REM  Build script for MMT Subtitle Editor (Windows x64)
REM ============================================================
REM  PublishSingleFile=true
REM      Bundle managed assemblies into one exe.
REM  PublishTrimmed=false
REM      No trimming. Avalonia/SukiUI rely heavily on reflection;
REM      trimming makes the app fail to start.
REM  IncludeNativeLibrariesForSelfExtract=false
REM      Keep native DLLs OUTSIDE the bundle, next to the exe.
REM      This avoids extracting to %TEMP%\.net at startup, which
REM      fails with "error code: 5" (access denied) on machines
REM      where that directory is blocked by AV or a bad ACL.
REM  Optimize=true / DebugType=None / DebugSymbols=false
REM      Smaller, optimized release build without symbols.
REM ============================================================

cd /d "%~dp0"

set PROJECT=MMT Subtitle Editor.csproj
set FRAMEWORK=net8.0-windows
set CONFIG=Release

echo === Windows x64 ===
dotnet publish "%PROJECT%" -c %CONFIG% -f %FRAMEWORK% -r win-x64 ^
 --self-contained false ^
 /p:PublishSingleFile=true ^
 /p:PublishTrimmed=false ^
 /p:IncludeNativeLibrariesForSelfExtract=false ^
 /p:EnableCompressionInSingleFile=false ^
 /p:Optimize=true ^
 /p:DebugType=None ^
 /p:DebugSymbols=false

if errorlevel 1 (
    echo.
    echo *** BUILD FAILED, exit code %errorlevel% ***
    pause
    exit /b %errorlevel%
)

echo.
echo Build finished. Output:
echo   bin\Release\net8.0-windows\win-x64\publish\
echo.
echo NOTE: native DLLs (libSkiaSharp.dll, libHarfBuzzSharp.dll,
echo       av_libglesv2.dll, libonigwrap.dll) sit next to the exe.
echo       Ship the WHOLE folder, not just the exe.
pause
