# MegaCopier

Copiador de archivos para Windows 11 cuya prioridad es que **una copia interrumpida (apagón, cuelgue, USB desconectado) continúe desde donde quedó, sin corromper el archivo**.

Estado: **fase 2 — app WinUI 3 con proceso residente en la bandeja**. La integración con el Explorador (Ctrl+C / Ctrl+V), las funciones avanzadas, la personalización completa y el instalador llegan en las siguientes fases.

## La app

`MegaCopier.exe` es una app WinUI 3 (Windows App SDK 2.x) compilada con Native AOT, **sin MSIX**: se distribuye
como un programa normal y lleva su propio runtime.

- **Proceso residente**: se queda en la bandeja del sistema (cerrar la ventana solo la oculta). Instancia única por
  usuario; cualquier otro proceso le entrega copias por una tubería con nombre (`MegaCopier-<SID>`, JSON simple).
- **Arranque instantáneo de copias**: la ventana se crea oculta al arrancar, así que cuando llega una copia solo hay que mostrarla.
- **Cola por disco físico**: dos copias que tocan el mismo disco esperan turno; en discos distintos van en paralelo.
- **Tras un apagón o un cierre**: al volver a abrirse, las copias a medias aparecen como *interrumpidas* (con aviso
  del sistema) y se continúan desde el último bloque confirmado con un clic, o automáticamente si se activa en Ajustes.
- **Ajustes**: tema (sistema/claro/oscuro), verificar después de copiar, qué hacer si el archivo existe, continuar
  automáticamente, iniciar con Windows (solo bandeja) y avisar al terminar. Se guardan en `%LOCALAPPDATA%\MegaCopier\settings.json`.

Línea de comandos de la app: `MegaCopier.exe` (abrir), `MegaCopier.exe --background` (solo bandeja),
`MegaCopier.exe copy --dest <destino> [--verify] <origen>...`.

Tiempos medidos el 15/10/2026 (app publicada con AOT):

| Situación | Tiempo |
|---|---|
| App en la bandeja: petición de copia → copia entregada | 4–33 ms |
| App en la bandeja: petición de copia → ventana visible | 32–76 ms |
| App cerrada: petición de copia → app arrancada y ventana visible | 279–477 ms |

## Cómo funciona la reanudación

1. Nunca se escribe en el nombre final: se copia a `archivo.ext.mcpart`.
2. El archivo se copia por bloques (64 MB por defecto). Al cerrar cada bloque:
   - los datos se vacían al disco (`FlushFileBuffers`);
   - **solo después** se anota el bloque y su hash XxHash128 en `archivo.ext.mcjournal`.
3. Al reanudar manda el journal, no el tamaño del archivo:
   - se descartan los registros rotos (cada uno lleva su CRC32);
   - se comprueba que el origen no haya cambiado (tamaño, fecha e identificador NTFS);
   - se releen del disco los últimos bloques confirmados y se compara su hash, por si el disco "mintió" al vaciar.
4. Al terminar se aplican fechas y atributos, se renombra de forma atómica al nombre final y se borra el journal.

Tras un apagón se pierde como máximo un bloque. Con `--verify`, además, se relee todo el destino **sin caché** y se reparan los bloques dañados copiándolos de nuevo desde el origen.

Los archivos menores de 32 MB no usan journal (se recopian enteros si se cortan), pero también se escriben en `.mcpart` y se vacían al disco antes de renombrarlos, así que nunca queda un archivo "completo" lleno de ceros.

Para una carpeta, volver a lanzar el trabajo omite los archivos ya idénticos (mismo tamaño y fecha) y continúa los grandes a medias.

## Rendimiento

El motor usa E/S asíncrona sin caché con **4 lecturas y 4 escrituras simultáneas** de bloques de 4 MB
(un NVMe necesita varias peticiones a la vez para rendir). El hash se calcula en orden mientras las escrituras
siguen en curso, y la verificación relee todo el destino en una sola pasada con lecturas en paralelo.

Medido el 15/10/2026 copiando 4 GB de datos aleatorios, contando hasta que los datos están realmente en el disco:

| Escenario | MegaCopier | robocopy |
|---|---|---|
| Entre dos NVMe (990 EVO → 990 EVO Plus) | 3.050–3.900 MB/s | 1.800–2.290 MB/s |
| Verificación (relectura sin caché + hash) | ~6.800 MB/s | — |
| Mismo NVMe (lectura y escritura en el mismo disco) | 460–1.140 MB/s | 460–900 MB/s |

En el mismo disco manda el propio SSD: tras agotar su caché SLC baja a ~500 MB/s sostenidos con cualquier programa.
Confirmar cada 1024 MB en lugar de cada 64 MB solo gana un 5–10 %, así que se mantienen 64 MB (menos pérdida tras un apagón).

Opciones para ajustar a mano: `--block-mb`, `--queue`, `--chunk-mb`.

## Compilar

Requisitos (gratis, sin abrir Visual Studio): .NET 10 SDK y las Build Tools de C++ (para el ejecutable nativo).

```powershell
.\build.ps1
```

Genera:

- `artifacts\app\MegaCopier.exe` — la app (~65 MB con el runtime de WinUI incluido; el instalador comprimido rondará los 20 MB).
- `artifacts\cli\mcopy.exe` — herramienta de consola (ejecutable nativo, ~3 MB, arranca en ~12 ms).

La consola se llama `mcopy` y no `megacopier` porque Windows no distingue mayúsculas y chocaría con `MegaCopier.exe`.

## Uso de la herramienta de consola

```text
mcopy copy <origen>... <carpeta-destino> [--verify] [--conflict overwrite|skip|newer|rename|fail] [--force] [--chunk-mb n] [--block-mb n] [--queue n]
mcopy send <origen>... <carpeta-destino> [--verify]     Entrega la copia a la app (la arranca si no está abierta)
mcopy resume [id]
mcopy jobs
```

`copy` copia en la propia consola; `send` usa la app, igual que hará la integración con el Explorador.
`Ctrl+C` pausa la copia; `mcopy resume` la continúa. Los trabajos pendientes se guardan en `%LOCALAPPDATA%\MegaCopier\jobs`
y los comparten la consola y la app.

Para probar la reanudación a mano: `--crash-after-mb 2000` mata el proceso tras confirmar 2000 MB de un archivo.
Matar el proceso no equivale a un apagón (Windows sigue vaciando su caché); para una prueba real hay que apagar
de golpe una máquina virtual de Hyper-V durante la copia.

## Estructura

- `src/MegaCopier.Engine` — motor (journal, E/S sin caché, planificación, conflictos, reintentos), cola de trabajos por disco y protocolo IPC.
- `src/MegaCopier.App` — app WinUI 3: proceso residente, bandeja (Win32 directo, sin librerías), ventana y ajustes.
- `src/MegaCopier.Cli` — herramienta de consola `mcopy`.
- `tests/MegaCopier.Engine.Tests` — pruebas que simulan cortes en cada punto crítico, journal roto, datos perdidos, origen modificado, corrupción en la verificación, la cola y el IPC.
- `tools/IconGen` — genera `Assets\MegaCopier.ico` por código (`dotnet run tools/IconGen/icongen.cs -- <salida.ico>`).
