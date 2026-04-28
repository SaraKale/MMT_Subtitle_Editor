@echo off
chcp 65001

set PROJECT="MMT Subtitle Editor.csproj"
set FRAMEWORK=net8.0-windows
set CONFIG=Release

echo === Windows x64 ===
dotnet publish %PROJECT% -c %CONFIG% -f %FRAMEWORK% -r win-x64 ^
--self-contained false ^
/p:PublishSingleFile=true ^
/p:TrimMode=full ^
/p:IncludeNativeLibrariesForSelfExtract=true ^
/p:Optimize=true ^
/p:DebugType=None ^
/p:DebugSymbols=false
  
echo === macOS Intel ===
dotnet publish %PROJECT% -c %CONFIG% -f %FRAMEWORK% -r osx-x64 ^
--self-contained false ^
/p:PublishSingleFile=true ^
/p:TrimMode=full ^
/p:IncludeNativeLibrariesForSelfExtract=true ^
/p:Optimize=true ^
/p:DebugType=None ^
/p:DebugSymbols=false

echo === macOS ARM (M1/M2) ===
dotnet publish %PROJECT% -c %CONFIG% -f %FRAMEWORK% -r osx-arm64 ^
--self-contained false ^
/p:PublishSingleFile=true ^
/p:TrimMode=full ^
/p:IncludeNativeLibrariesForSelfExtract=true ^
/p:Optimize=true ^
/p:DebugType=None ^
/p:DebugSymbols=false

echo === Linux x64 ===
dotnet publish %PROJECT% -c %CONFIG% -f %FRAMEWORK% -r linux-x64 ^
--self-contained false ^
/p:PublishSingleFile=true ^
/p:TrimMode=full ^
/p:IncludeNativeLibrariesForSelfExtract=true ^
/p:Optimize=true ^
/p:DebugType=None ^
/p:DebugSymbols=false

echo Done!
pause
