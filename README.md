# XoPy

![Interfaz de XoPy](docs/xopy-preview.png)

XoPy es un gestor de copias gratuito y sin versiones de pago para **Windows 10/11 de 64 bits**, inspirado en TeraCopy:
misma idea de interfaz compacta, misma forma de trabajar y las mismas opciones que se usan a diario,
pero sin pagos, sin límites de prueba y sin funciones bloqueadas.

## Descargar el instalador

**[XoPy-Setup-win-x64.exe](https://github.com/Quevedo-crack/XoPy/releases/latest/download/XoPy-Setup-win-x64.exe)**

También está disponible la versión portable (un único `.exe`, sin instalar):
<https://github.com/Quevedo-crack/XoPy/releases/latest/download/XoPy.exe>

El instalador es autocontenido: no hace falta tener .NET instalado. Está compilado para
Windows x64 y funciona en Windows 11 (AMD Ryzen, Intel, etc.) sin requisitos adicionales.

Preferencias e integración con el Explorador:

![Preferencias de XoPy](docs/xopy-options-0.png)

Así se ve una verificación correcta (SHA-256 comparado con el original):

![Verificación en XoPy](docs/xopy-verify.png)

Y el historial, que se puede editar sin tocar los archivos:

![Historial de XoPy](docs/xopy-history.png)

## Automatización y línea de comandos

XoPy acepta órdenes para usarlo desde scripts o desde el Explorador:

```
XoPy.exe --copy  --to "D:\Copias" "C:\Datos\informe.pdf"
XoPy.exe --move  --to "D:\Copias" "C:\Datos\carpeta"
XoPy.exe --test  --to "D:\Copias" "C:\Datos\informe.pdf"
XoPy.exe --copy  --overwrite --verify --algo sha256 --to "D:\Copias" "C:\Datos\informe.pdf"
XoPy.exe --paste --to "D:\Copias"
XoPy.exe --copy --engine robocopy --to "D:\Copias" "C:\Datos\carpeta"
XoPy.exe --multi --copy --to "D:\Copias" "C:\Datos\informe.pdf"
```

- `--overwrite`, `--skip`, `--rename` fuerzan la política de conflictos de esa ejecución.
- `--verify` y `--algo crc32|md5|sha1|sha256|sha512` activan la verificación al copiar.
- `--to2 "E:\Espejo"` añade un segundo destino (se puede repetir) y `--checksums` genera el archivo
  de checksums al terminar.
- `--engine native|robocopy|managed` elige el motor de copia solo para esa ejecución.
- `--multi` abre una ventana nueva de XoPy (permite dos copias a la vez).
- `--locked` fuerza la lectura de archivos bloqueados, `--no-resume` desactiva la reanudación y
  `--secure N` sobrescribe N veces antes de borrar. `--script "C:\ruta\tarea.ps1"` se ejecuta al finalizar.
- `--ask-destination` abre el selector de carpeta antes de empezar (es lo que hace «Copiar a… con XoPy»).

## Funciones

- **Copia, mueve, elimina y verifica** archivos o carpetas completas (con subcarpetas).
- **Motor nativo de Windows** (`CopyFileExW` + `CopyProgressRoutine`): la copia la hace el propio
  sistema, con pausa, saltar archivo y límite de velocidad, y sin dobles lecturas. Si algo falla,
  XoPy reintenta con la copia administrada y con permisos de copia de seguridad.
- **Cola jerárquica**: al añadir una carpeta se ve la carpeta y dentro cada archivo, con sus
  subcarpetas. Puedes expandirla o contraerla, **reordenar** (botones ↑/↓ o Alt+↑/Alt+↓) sin separar
  un archivo de su carpeta, **ordenar** por nombre, tamaño, tipo o carpeta de destino (menú Cola o el
  botón ⇅) y **quitar** elementos de la cola antes de transferir.
- **La estructura se conserva**: copiar `C:\Fotos` crea `Destino\Fotos\sub\archivo.jpg`, nunca
  archivos sueltos en la raíz del destino. Las carpetas vacías también se crean.
- **Varios destinos por tarea**: cada archivo se copia a la vez en el destino principal y en los
  destinos adicionales configurados en Opciones.
- **Archivos bloqueados**: se abren con permisos de copia de seguridad (`SeBackupPrivilege`) para
  poder copiar archivos en uso.
- **Reanudar**: si una copia se interrumpe, el siguiente intento continúa desde el byte donde se
  quedó el archivo `.xopy-part`. La cola se guarda sola al cerrar y se puede reabrir desde
  Archivo → «Abrir la última cola y reanudar».
- **Checksums y tareas finales**: genera archivos `.sfv/.md5/.sha1/.sha256/.sha512`, ejecuta un
  script al terminar (PowerShell, BAT o EXE) y permite el borrado seguro con 1–7 pasadas.
- **Informes HTML y CSV** de la transferencia en curso y del historial.
- **Ajustes por defecto para equipos modernos** (4 transferencias simultáneas y búfer de 4 MB,
  pensados para un Ryzen 7 con 24 GB de RAM y NVMe). Todo se puede cambiar en Opciones.
- **Pausar, reanudar, saltar el archivo actual y detener** en cualquier momento. Los archivos a medio
  copiar se escriben en un temporal `.xopy-part` que se elimina si cancelas; si la carpeta no permite
  crear el temporal (permisos, rutas largas o unidades de red) se copia directamente para no fallar.
- **Verificación real**: CRC32, MD5, SHA-1, SHA-256 y SHA-512, comparando origen y destino.
- **Límite de velocidad**, tamaño de búfer y transferencias simultáneas configurables.
- **Resolución de conflictos**: preguntar, sobrescribir, omitir, cambiar nombre, sobrescribir si
  es más antiguo o si es más pequeño.
- **Gestión de errores**: reintentos automáticos, omitir el archivo o detener la transferencia.
- **Conservar fechas** y atributos, y opción de enviar a la **Papelera de reciclaje**.
- **Favoritos**, historial de transferencias con exportación a CSV/HTML y sonido al terminar.
- **Historial editable**: borra una entrada o todo el historial desde el propio diálogo; solo se
  quita el registro, nunca el archivo copiado.
- **Quitar de la lista ≠ borrar**: `Supr` o el botón de la papelera quitan el elemento de la cola
  y no tocan el archivo original. Para borrar de verdad está «Eliminar» y el menú «Eliminar del disco…».
- **Pestañas Lista de archivos / Destino / Opciones / Estado / Registro**, encabezado «Copiando /
  Destino» con porcentaje por archivo, gráfica de velocidad y tabla de categorías (Correctos,
  Verificados, Fallidos, Total). Sin parpadeo: doble búfer y actualizaciones incrementales.
- **Icono de bandeja con el porcentaje** de la copia, como SuperCopier.
- **Registro técnico** en `%LOCALAPPDATA%\XoPy\transfer.log` para diagnosticar cualquier error.
- **Acciones al terminar**: apagar el equipo, cerrar XoPy o abrir la carpeta de destino.
- Icono de progreso en el **área de notificación** y modo minimizado.
- Temas **oscuro** y **claro**.

## Novedades de la versión 2.4.0

- **XoPy sustituye al copiador de Windows**: `Ctrl+C`, `Ctrl+X` y `Ctrl+V` en el Explorador y en el
  escritorio abren XoPy con su ventana y su progreso. Si venías de «Cortar», se mueve y se borra el
  origen. Se puede desactivar en Preferencias → Integración para volver al copiador de Windows.
- **Ya no aparece en «Enviar a»** ni como una opción suelta: el pegado es la forma normal de usarlo.
- **Motor elegible**: Nativo (CopyFileEx), **Robocopy** o Administrado, en Preferencias → Transferencia.
  Robocopy es el más rápido con miles de archivos, redes y rutas largas.
- **Mover borra siempre el origen**, incluidos los archivos y las carpetas que quedan vacías.
- **Tiempo estimado** en la barra de título, en el encabezado y en el pie («quedan 00:02:03»).
- **En pausa la velocidad es 0 B/s** (antes seguía mostrando la última velocidad).
- **Porcentaje más grande** en el icono de la bandeja y en el botón de la barra de tareas, con barra
  de progreso propia en la barra de tareas.
- **Clic izquierdo en el icono de la bandeja abre la ventana**.
- **Nueva copia con otra en curso**: XoPy pregunta si añadirla a la actual o abrir otra ventana para
  copiar las dos a la vez.
- **Columna de trabajos plegable** (`«` en la barra de la lista) para dejar la ventana más pequeña.
- **Ordenar por origen, destino, tamaño, tipo, nombre y estado**, en ambos sentidos.
- **Reiniciar XoPy como administrador** desde Herramientas para copiar a carpetas protegidas.
- Corregido el bloqueo de la ventana al abrirse y el reordenado de los elementos de primer nivel.
- Pruebas automáticas nuevas: motor Robocopy con hash, mover carpetas completas borrando el origen,
  ordenar por origen/destino y el pegado en el Explorador.

## Integración con el Explorador de Windows

El instalador añade estas acciones al menú contextual (se pueden desactivar en Preferencias):

- **Copiar con XoPy** (usa el último destino configurado)
- **Copiar a… con XoPy** (pregunta la carpeta de destino)
- **Mover con XoPy** / **Mover a… con XoPy**
- **Verificar con XoPy**
- **Pegar con XoPy** y **Abrir XoPy aquí** (en el fondo de una carpeta)
- **Enviar a → Copiar con XoPy / Mover con XoPy** (permite enviar varios archivos a la vez)

En Windows 11 estas acciones aparecen en **Mostrar más opciones** (`Mayús+F10`), igual que en
TeraCopy. Si XoPy ya está abierto, la orden se ejecuta en la ventana existente.

Además XoPy se abre en una sola ventana: si ya está abierto, las nuevas órdenes del Explorador se
añaden a la ventana existente.

> Windows no permite que un programa normal intercepte `Ctrl+C` / `Ctrl+V` del Explorador (eso
> requiere una extensión COM firmada, que es lo que usa TeraCopy). Por eso la integración de XoPy
> se hace con el menú contextual, que es la forma fiable y sin riesgo de romper el sistema.

## Uso rápido

1. Instala XoPy y ábrelo.
2. Añade archivos o carpetas con los botones, con `Ctrl+O`, o arrastrándolos a la ventana.
3. Elige la carpeta de destino en **Copiar a:** (o usa un favorito).
4. Pulsa **Copiar**, **Mover**, **Eliminar** o **Verificar**.
5. Sigue el progreso y la velocidad en la barra inferior. Puedes pausar o detener cuando quieras.

### Atajos de teclado

| Atajo | Acción |
| --- | --- |
| `Ctrl+O` | Añadir archivos |
| `Ctrl+Shift+O` | Añadir carpeta |
| `F5` | Copiar |
| `Ctrl+F5` | Mover |
| `F6` | Verificar |
| `Espacio` | Pausar / reanudar |
| `Esc` | Detener |
| `Supr` | Quitar de la lista |

## Compilar

Requisitos: Windows 10/11 y .NET 8 SDK.

```powershell
dotnet publish .\XoPy\XoPy.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Para generar el instalador, compila `installer\XoPy.iss` con Inno Setup 6:

```powershell
& "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe" installer\XoPy.iss
```

El flujo **Build Windows x64** de GitHub Actions hace ambas cosas automáticamente y publica
el instalador como artefacto. Al crear una etiqueta `v*` también se publica en *Releases*.

## Estructura

| Archivo | Contenido |
| --- | --- |
| `XoPy/MainForm.cs` | Ventana principal estilo TeraCopy |
| `XoPy/CopyEngine.cs` | Motor de copia, movimiento, borrado y verificación |
| `XoPy/SettingsStore.cs` | Preferencias e historial en `%LOCALAPPDATA%\XoPy` |
| `XoPy/ShellIntegration.cs` | Menú contextual del Explorador y arranque con Windows |
| `XoPy/Dialogs.cs` | Preferencias, conflictos, errores e historial |
| `installer/XoPy.iss` | Instalador de Inno Setup |
