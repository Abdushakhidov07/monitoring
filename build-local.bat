@echo off
chcp 65001 >nul
echo ============================================
echo   Сборка FreeMon в один .exe
echo ============================================
echo.
echo Требуется установленный .NET 8 SDK:
echo https://dotnet.microsoft.com/download/dotnet/8.0
echo (нужен именно SDK, не Runtime)
echo.

dotnet publish FreeMon.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish

if %errorlevel% neq 0 (
  echo.
  echo [ОШИБКА] Сборка не удалась. Проверь, что установлен .NET 8 SDK.
  pause
  exit /b 1
)

echo.
echo Готово! Файл здесь:  %cd%\publish\FreeMon.exe
echo.
echo Для счётчика FPS положи рядом с FreeMon.exe файл PresentMon.exe:
echo https://github.com/GameTechDev/PresentMon/releases
echo (переименуй скачанный PresentMon-...-x64.exe в PresentMon.exe)
echo.
pause
