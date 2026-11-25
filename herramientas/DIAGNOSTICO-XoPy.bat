@echo off
rem ============================================================================
rem  DIAGNOSTICO DE XoPy
rem  Para cuando haces doble clic y NO PASA NADA.
rem
rem  Genera un informe en el Escritorio: xopy-diagnostico.txt
rem  Puedes pegarlo tal cual en la conversacion.
rem
rem  No instala nada ni cambia nada del sistema: solo mira y apunta.
rem ============================================================================
setlocal
set INFORME=%USERPROFILE%\Desktop\xopy-diagnostico.txt

echo.
echo === DIAGNOSTICO DE XoPy ===
echo Generando informe, espera unos segundos...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='SilentlyContinue';" ^
  "$out = '%INFORME%';" ^
  "function W($t){ $t | Out-File -FilePath $out -Append -Encoding utf8 };" ^
  "Remove-Item $out -Force -ErrorAction SilentlyContinue;" ^
  "W ('XoPy - informe de diagnostico ' + (Get-Date));" ^
  "W ('Windows: ' + (Get-CimInstance Win32_OperatingSystem).Caption + ' build ' + [Environment]::OSVersion.Version);" ^
  "W ('Arquitectura: ' + $env:PROCESSOR_ARCHITECTURE);" ^
  "W '';" ^
  "W '--- ARCHIVOS DESCARGADOS (Descargas y Escritorio) ---';" ^
  "Get-ChildItem @(\"$env:USERPROFILE\Downloads\",\"$env:USERPROFILE\Desktop\") -Filter 'XoPy*' -ErrorAction SilentlyContinue | ForEach-Object {" ^
  "  $z = Get-Item -Path ($_.FullName + ':Zone.Identifier') -Stream Zone.Identifier -ErrorAction SilentlyContinue;" ^
  "  $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash;" ^
  "  W ('  ' + $_.Name + '  ' + $_.Length + ' bytes  bloqueado_por_internet=' + [bool]$z);" ^
  "  W ('     SHA256: ' + $h) };" ^
  "W '';" ^
  "W '--- XoPy INSTALADO ---';" ^
  "$exe = Join-Path $env:ProgramFiles 'XoPy\XoPy.exe';" ^
  "W ('  instalado=' + (Test-Path $exe) + '  ruta=' + $exe);" ^
  "if (Test-Path $exe) { W ('  version=' + (Get-Item $exe).VersionInfo.FileVersion + '  tamano=' + (Get-Item $exe).Length) };" ^
  "W '';" ^
  "W '--- PROCESOS XoPy EN MARCHA ---';" ^
  "$p = Get-Process XoPy -ErrorAction SilentlyContinue;" ^
  "if ($p) { $p | ForEach-Object { W ('  PID ' + $_.Id + ' responde=' + $_.Responding + ' ventana=' + $_.MainWindowHandle) } } else { W '  ninguno' };" ^
  "W '';" ^
  "W '--- WINDOWS DEFENDER: AMENAZAS DETECTADAS ---';" ^
  "$t = Get-MpThreatDetection -ErrorAction SilentlyContinue | Sort-Object InitialDetectionTime -Descending | Select-Object -First 10;" ^
  "if ($t) { $t | ForEach-Object { W ('  ' + $_.InitialDetectionTime + '  ' + ($_.Resources -join ' ') + '  accion=' + $_.ThreatStatusID) } } else { W '  sin detecciones registradas' };" ^
  "$amenazas = Get-MpThreat -ErrorAction SilentlyContinue | Select-Object -First 10;" ^
  "if ($amenazas) { $amenazas | ForEach-Object { W ('  AMENAZA: ' + $_.ThreatName + ' -> ' + ($_.Resources -join ' ')) } };" ^
  "W '';" ^
  "W '--- ERRORES DEL SISTEMA RELACIONADOS CON XoPy (ultimos 20) ---';" ^
  "Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddDays(-7)} -ErrorAction SilentlyContinue |" ^
  "  Where-Object { $_.Message -like '*XoPy*' -or $_.ProviderName -like '*.NET*' } | Select-Object -First 20 |" ^
  "  ForEach-Object { W ('  ' + $_.TimeCreated + ' [' + $_.ProviderName + '] ' + ($_.Message -split \"`n\")[0]) };" ^
  "W '';" ^
  "W '--- REGISTRO DE ARRANQUE DE XoPy ---';" ^
  "$log = Join-Path $env:LOCALAPPDATA 'XoPy\startup.log';" ^
  "if (Test-Path $log) { W ('  ' + $log); Get-Content $log -Tail 40 | ForEach-Object { W ('  ' + $_) } } else { W '  no existe: XoPy no ha llegado a ejecutarse nunca' };" ^
  "W '';" ^
  "W '--- PRUEBA DE EJECUCION ---';" ^
  "$cand = @(\"$env:USERPROFILE\Downloads\XoPy.exe\", \"$env:USERPROFILE\Desktop\XoPy.exe\", $exe) | Where-Object { Test-Path $_ } | Select-Object -First 1;" ^
  "if ($cand) { W ('  probando: ' + $cand);" ^
  "  try { $pr = Start-Process $cand -ArgumentList '--safe' -PassThru -ErrorAction Stop; Start-Sleep -Seconds 20; $pr.Refresh();" ^
  "    W ('  proceso_vivo=' + (-not $pr.HasExited) + ' ventana=' + $pr.MainWindowHandle);" ^
  "    if ($pr.HasExited) { W ('  codigo_de_salida=' + $pr.ExitCode) };" ^
  "    Stop-Process -Id $pr.Id -Force -ErrorAction SilentlyContinue }" ^
  "  catch { W ('  NO SE PUDO EJECUTAR: ' + $_.Exception.Message) } }" ^
  "else { W '  no se encontro ningun XoPy.exe para probar' };" ^
  "W '';" ^
  "W '--- FIN ---'"

echo.
echo Informe generado en:
echo   %INFORME%
echo.
echo Abrelo, copia el contenido y pegalo en la conversacion.
echo.
start "" notepad "%INFORME%"
pause
