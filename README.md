# XoPy

XoPy es un gestor de transferencias para Windows con una interfaz WinForms original y un motor implementado en C#.

## Funciones

- Añadir múltiples archivos y carpetas con diálogos o arrastrándolos a la ventana/cola.
- Copiar o mover trabajos en una cola secuencial.
- Pausar, cancelar y continuar archivos parciales cuando se mantiene el mismo origen, destino y el origen no cambia.
- Elegir tratamiento de conflictos: cambiar nombre, sobrescribir u omitir.
- Verificación SHA-256 opcional.
- Cola ordenable, reordenamiento manual, multiselección, quitar seleccionados, limpiar finalizadas y reintentar fallidas/canceladas.
- Guardar y cargar colas JSON.
- Progreso por trabajo y general, minimizar a la bandeja del sistema.
- Protección contra copiar una carpeta dentro de sí misma o copiar sobre el mismo archivo de origen.

## Requisitos

- Windows 10/11
- .NET 8 SDK o Visual Studio 2022 con la carga de trabajo de escritorio .NET

Abrir `XoPy.sln` y ejecutar el proyecto `XoPy`.

## Referencias visuales y licencias

La paleta azul y verde de XoPy es una interpretación propia de una interfaz para transferencias, no una copia exacta de TeraCopy. El icono de XoPy es original. No se incluyen logotipos, iconos ni recursos gráficos de TeraCopy.

UltraCopier (https://github.com/alphaonex86/Ultracopier) es un programa C++/Qt publicado bajo GPLv3. Este repositorio no incorpora su motor ni su código fuente; XoPy tiene una implementación C# independiente. La documentación de UltraCopier informa que parte de sus imágenes procede de Oxygen/KDE, así que no se reutilizan recursos de terceros sin verificar su licencia particular.

La aplicación necesita compilación y pruebas funcionales antes de considerarse estable. El motor trabaja en serie; no implementa ejecución paralela, limitador de velocidad ni integración de sistema de archivos.
