# Recuperar archivos borrados o movidos por error

Guía corta y por orden de eficacia. **Importante: deja de escribir en el disco afectado**
hasta recuperar lo que puedas. Cuanto menos se escriba, más se puede recuperar.

## 1. Primero: puede que NO se haya borrado nada

Antes de nada, busca los archivos en el destino al que se copiaron o movieron:

- En XoPy, abre **Registro** y busca `Copia nativa:`. Cada línea es
  `origen -> destino`, la ruta exacta a la que fue cada archivo.
- En `%LOCALAPPDATA%\XoPy\transfer.log` está lo mismo, con fecha y hora.
- En `%LOCALAPPDATA%\XoPy\mover-*.txt` (versión 2.5.6 en adelante) hay una lista
  con `origen -> destino` de cada archivo que se iba a **mover**, escrita *antes*
  de tocar el disco.

Copia esas rutas a un papel o a otro equipo. Son tu mapa.

## 2. Papelera de reciclaje

Abre la Papelera (`Win + E` → Papelera de reciclaje) y ordena por
«Fecha de eliminación». Restaura lo que aparezca. XoPy solo usa la Papelera si
activaste «Enviar a la Papelera de reciclaje» en Preferencias; si no, sigue al paso 3.

## 3. Historial de versiones de Windows (Windows 10/11)

Si la carpeta estaba en `C:\Users\...` con **Protección del sistema** activa:

1. Clic derecho en la carpeta que la contenía → **Propiedades**.
2. Pestaña **Versiones anteriores**.
3. Elige la más reciente y **Restaurar** (o «Abrir» para copiar archivos sueltos).

También sirve: botón derecho en el archivo → **Restaurar versiones anteriores**.

## 4. Copias de seguridad

- **Archivos de Windows (Historial de archivos)**: Panel de control →
  Historial de archivos → Restaurar archivos personales.
- **OneDrive**: onedrive.com → Papelera de reciclaje (guarda 30 días).
- Si usas un programa de copias (Macrium, Acronis, Veeam…), restaura solo esa carpeta.

## 5. Recuperar archivos borrados del disco (última red, muy eficaz si no se escribió nada)

Programas que **solo leen**: úsalos desde otro disco o desde un USB, nunca instalados
en el disco afectado.

| Herramienta | Coste | Notas |
|---|---|---|
| **Recuva** (Piriform) | Gratis | Fácil, interfaz en español, buen resultado con NTFS. |
| **PhotoRec / TestDisk** | Gratis y libre | Más potente y más técnico. Recupera por tipos de archivo. |
| **Disk Drill** | Gratis hasta 500 MB | Interfaz clara; el límite puede quedarse corto. |
| **EaseUS Data Recovery** | Prueba gratuita | Permite ver qué se puede recuperar antes de pagar. |

Pasos: conecta el disco → ejecuta el programa **desde otro disco** → elige el disco
afectado → **solo lectura / escaneo** → selecciona los archivos → guárdalos en **otro**
disco distinto.

Si el disco es un SSD con TRIM activo, la recuperación por software funciona peor:
cuanto antes lo intentes, mejor.

## 6. Si el disco tiene daños, no lo "arregles" a ciegas

Si sospechas fallo físico (ruidos, errores de lectura), clona el disco antes de nada con
**ddrescue** y trabaja sobre el clon.

---

## Qué se ha cambiado en XoPy para que no vuelva a pasar

- **Mover** solo borra el original si el destino existe, tiene el mismo tamaño y no es el
  propio archivo. Si no se puede confirmar, el original **se queda** y se avisa.
- Mover a la **misma carpeta** está bloqueado: no crea un duplicado ni borra nada.
- Las carpetas de origen **nunca** se borran si dentro queda algún archivo, ni si hubo
  cualquier error.
- Antes de mover se escribe `%LOCALAPPDATA%\XoPy\mover-FECHA.txt` con todas las rutas
  `origen -> destino`.
- El desinstalador tiene una salvaguarda: si la carpeta de la aplicación no es la de
  XoPy (o está dentro de Escritorio, Documentos, Descargas, Juegos, Series…), no borra nada.
