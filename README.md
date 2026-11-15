# XoPy

XoPy es un gestor de copias y movimientos para Windows, pensado inicialmente para uso personal.

## Funciones

- Añadir archivos y carpetas desde los diálogos o arrastrándolos a la cola.
- Copiar o mover trabajos a una carpeta de destino.
- Cola ordenable, multiselección, reordenamiento manual y eliminación de elementos.
- Reintentar trabajos fallidos o cancelados y limpiar los finalizados.
- Pausar, cancelar y continuar una transferencia; al cancelar se conserva el parcial para reanudar cuando coincidan el origen y el destino.
- Política de conflictos configurable: cambiar nombre, sobrescribir u omitir.
- Verificación SHA-256 opcional, activada por defecto.
- Guardar y cargar colas JSON; los trabajos que estaban activos al guardar vuelven a pendientes al cargarse.
- Indicador de progreso general y minimización a la bandeja del sistema.
- Protección contra destinos de carpeta dentro del origen y destinos iguales al archivo de origen.

## Ejecutar y compilar

- Windows 10/11
- .NET 8 SDK o Visual Studio 2022 con la carga de trabajo de escritorio .NET
- Abrir `XoPy.sln` y ejecutar el proyecto `XoPy`.

La aplicación está en desarrollo. La reanudación parcial solo se conserva al cancelar una transferencia y es válida mientras el archivo de origen no cambie. La compilación y las pruebas funcionales siguen pendientes.
