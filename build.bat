@echo off
setlocal
cd /d "%~dp0"
title Build DeveloppeTube

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERREUR] dotnet introuvable : installe le SDK .NET 8.
    pause
    exit /b 1
)

echo.
echo === DeveloppeTube : publication win-x64, exe unique ===
echo.

rem ferme l'exe s'il tourne (sinon fichier verrouille)
taskkill /f /im DeveloppeTube.exe >nul 2>nul

dotnet publish DeveloppeTube.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeAllContentForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none ^
  -o "%~dp0exe_final"

if errorlevel 1 (
    echo.
    echo [ERREUR] La publication a echoue, regarde les messages ci-dessus.
    pause
    exit /b 1
)

echo.
echo === OK : "%~dp0exe_final\DeveloppeTube.exe" ===
dir "%~dp0exe_final\DeveloppeTube.exe" | find "DeveloppeTube.exe"
echo.
pause
