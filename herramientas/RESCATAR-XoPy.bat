@echo off
rem ============================================================================
rem  RESCATE DE XoPy
rem  Para cuando el equipo se queda "pensando" y no abre nada.
rem
rem  Qué hace, por orden:
rem    1. Cierra XoPy (es lo que puede tener el teclado interceptado).
rem    2. Quita el registro de la extension del menu contextual, que es la DLL
rem       que el Explorador carga dentro de si mismo.
rem    3. Quita el arranque automatico con Windows.
rem    4. Reinicia el Explorador de Windows para que suelte la DLL.
rem
rem  NO borra ningun archivo tuyo. NO desinstala XoPy.
rem
rem  Como ejecutarlo si el escritorio no responde:
rem    Ctrl + Shift + Esc  ->  Archivo  ->  Ejecutar nueva tarea
rem    Marca "Crear esta tarea con privilegios administrativos"
rem    Escribe la ruta de este archivo y acepta.
rem ============================================================================

echo.
echo === RESCATE DE XoPy ===
echo.

echo [1/4] Cerrando XoPy...
taskkill /IM XoPy.exe /F >nul 2>&1
if errorlevel 1 (echo       no habia ningun XoPy abierto.) else (echo       cerrado.)

echo [2/4] Quitando la extension del menu contextual...
reg delete "HKCU\Software\Classes\Directory\Background\shellex\ContextMenuHandlers\XoPyPaste" /f >nul 2>&1
reg delete "HKCU\Software\Classes\Directory\shellex\ContextMenuHandlers\XoPyPaste" /f >nul 2>&1
reg delete "HKCU\Software\Classes\CLSID\{3B1E92A7-7AD6-4E71-A218-75449CE13061}" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved" /v "{3B1E92A7-7AD6-4E71-A218-75449CE13061}" /f >nul 2>&1
echo       hecho.

echo [3/4] Quitando el arranque automatico...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v XoPy /f >nul 2>&1
echo       hecho.

echo [4/4] Reiniciando el Explorador de Windows...
taskkill /IM explorer.exe /F >nul 2>&1
timeout /t 2 /nobreak >nul
start "" explorer.exe
echo       hecho.

echo.
echo ============================================================
echo  Listo. El equipo deberia responder con normalidad.
echo.
echo  XoPy sigue instalado, pero SIN el menu contextual y SIN
echo  interceptar Ctrl+V. Puedes abrirlo normalmente y volver a
echo  activar la integracion desde Preferencias cuando quieras.
echo ============================================================
echo.
pause
