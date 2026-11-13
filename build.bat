@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ============================================
echo  Altyazi+ (Burak Topcu) - derleme
echo ============================================

where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo [HATA] .NET 8 SDK bulunamadi.
  echo Kurulum: https://dotnet.microsoft.com/download/dotnet/8.0  ^(SDK x64^)
  echo Kurduktan sonra bu dosyayi tekrar calistirin.
  pause
  exit /b 1
)

dotnet publish src\Ps5Altyazi\Ps5Altyazi.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
if errorlevel 1 (
  echo.
  echo [HATA] Derleme basarisiz. Yukaridaki hata mesajlarini kopyalayip gonderin.
  pause
  exit /b 1
)

echo.
echo [TAMAM] Uygulama hazir: publish\AltyaziPlus.exe
echo Simdi baslatiliyor...
start "" "publish\AltyaziPlus.exe"
pause
