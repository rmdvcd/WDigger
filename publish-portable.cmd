@echo off
setlocal
cd /d "%~dp0"

echo Publicando WDigger portable para Windows x64...
dotnet publish "src\WDigger\WDigger.csproj" -c Release -p:PublishProfile=Portable-win-x64
if errorlevel 1 (
    echo.
    echo ERROR: no se pudo publicar WDigger.
    exit /b 1
)

echo.
echo Publicacion completada: %CD%\artifacts\portable\WDigger.exe
echo Copia ese archivo a un USB y ejecutalo. Acepta el aviso de UAC.
endlocal
