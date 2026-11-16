# XoPy

XoPy es un gestor gratuito de copias para Windows, pensado para uso personal. No tiene pagos, límites de prueba ni funciones Pro bloqueadas.

## Funciones

- Añadir varios archivos y carpetas, también mediante arrastrar y soltar. Las carpetas conservan su estructura y se pueden expandir en la cola.
- Copiar o mover, pausar, reanudar y cancelar transferencias.
- Ordenar y reordenar la cola sin separar los elementos de sus carpetas.
- Configurar transferencias paralelas, tamaño del búfer, límite de velocidad, verificación SHA-256 y resolución de conflictos.
- Guardar y cargar la cola junto con sus preferencias. Los archivos parciales se pueden reanudar al cargar la cola e iniciar de nuevo.
- Ver el porcentaje de copia en la ventana y en el icono del área de notificación de Windows.
- Consultar historial de transferencias y métricas de velocidad.

Todavía no están implementados la integración con el Explorador de Windows, la copia de archivos bloqueados, scripts de finalización, archivos checksum, borrado seguro, varios destinos por tarea, favoritos ni exportación HTML/CSV.

## Descargar el instalador de Windows

El flujo **Build Windows x64** compila un instalador autocontenido para Windows x64 en cada pull request y cada actualización de `main`.

1. Abre la pestaña **Actions** del repositorio y selecciona la ejecución más reciente de **Build Windows x64** que haya terminado correctamente.
2. Descarga el artefacto **XoPy-Setup-win-x64**.
3. Extrae el ZIP y ejecuta `XoPy-Setup-win-x64.exe`.

Los artefactos de Actions se conservan durante 30 días. No requieren instalar .NET en el equipo de destino.

## Compilar localmente

Requisitos: Windows 10/11 y .NET 8 SDK o Visual Studio 2022 con la carga de trabajo de escritorio .NET.

```powershell
dotnet publish .\XoPy\XoPy.csproj -c Release -r win-x64 --self-contained true
```

La aplicación publicada queda en `XoPy\bin\Release\net8.0-windows\win-x64\publish\`. Para crear un instalador, compila `installer\XoPy.iss` con Inno Setup 6.
