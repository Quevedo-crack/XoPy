# XoPy

XoPy es un gestor de transferencias para Windows con una interfaz WinForms original y un motor implementado en C#.

## Funciones implementadas en el código

- Añadir múltiples archivos y carpetas con diálogos o arrastrándolos a la ventana/cola.
- Copiar o mover en una cola secuencial.
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

## UltraCopier y licencias

UltraCopier es un proyecto independiente escrito en C++/Qt y publicado bajo GPLv3: https://github.com/alphaonex86/Ultracopier. Su motor no se integra directamente en este proyecto C#/WinForms; XoPy mantiene una implementación C# propia y no contiene código fuente de UltraCopier. La apariencia de XoPy es una composición original, no una copia de sus imágenes. El archivo `README.sources` de UltraCopier indica que varias imágenes proceden de Oxygen/KDE, por lo que no se incorporan sin verificar los términos específicos de cada recurso.

La aplicación todavía necesita compilación y pruebas funcionales antes de considerarse estable. El motor trabaja en serie; no implementa ejecución paralela, limitador de velocidad ni la integración con el sistema de archivos de UltraCopier.
