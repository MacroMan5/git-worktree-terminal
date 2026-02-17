@echo off
dotnet publish tmuxlike\tmuxlike.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    exit /b 1
)
if not exist "%USERPROFILE%\bin" mkdir "%USERPROFILE%\bin"
copy /Y tmuxlike\bin\Release\net8.0-windows7.0\win-x64\publish\tmuxlike.exe "%USERPROFILE%\bin\tmuxlike.exe"
echo.
echo Published to %USERPROFILE%\bin\tmuxlike.exe
