# Novedades

Todos los cambios importantes de MegaCopier. El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y las versiones, [SemVer](https://semver.org/lang/es/).

## [Sin publicar]

### Añadido
- 41 temas nuevos (52 en total), muchos inspirados en los clásicos de SuperCopier y rehechos al estilo de Windows 11:
  Combustión, Caja negra, Código verde, Aurora, Galaxia, Neón, Madera, Cuaderno, Cristal, Aero, Luna, Plata y más.
- Los temas pueden tener fondo en degradado y textura (brasas, madera, papel, fibra de carbono, aurora, galaxia, metal,
  ondas, rejilla de neón y lluvia digital).
- Los temas colorean también todos los controles: barras de desplazamiento, botones, desplegables, cuadros de texto,
  interruptores, casillas, listas, menús, diálogos y avisos.
- Siete conjuntos de iconos, independientes del tema: Fluent, Color, Mosaico, Suave, Degradado, Neón y Windows 10.
- Ajustes rehechos como la Configuración de Windows 11: secciones a la izquierda (General, Interfaz, Sonidos, Copia,
  Comportamiento, Errores, Cola y rendimiento, Red y dispositivos, Automatización y Avanzado), buscador de ajustes,
  iconos en cada opción y animaciones al cambiar de sección y al pulsar.
- Opciones al estilo de SuperCopier:
  - **Si un archivo sigue fallando: preguntar qué hacer** (reintentar, omitir, omitir todos o detener).
  - Añadir las copias nuevas a una que ya está en marcha (nunca, misma carpeta, mismos discos o siempre), preguntando o no.
  - Al detener una copia, borrar lo que quedó a medias (salvo si paró por un error).
  - Al terminar, expulsar la unidad de destino si es extraíble (y forzarlo).
  - Informe de errores automático en la carpeta de destino o en una carpeta fija, con el nombre que quieras.
  - Atributos y permisos que se conservan al mover, aparte de los de copiar.
  - Mostrar u ocultar el icono de la bandeja, empezar las copias con la ventana minimizada, recordar la posición y el
    tamaño de la ventana, y ventana translúcida.
  - Carpeta de datos a la vista y botón para restablecer los ajustes.
- Sonidos propios de MegaCopier (campanillas originales) para copia terminada, todas terminadas, error y atención; ya no
  se usan los avisos de Windows salvo que los elijas.

### Cambiado
- Documentación nueva y completa: guía de uso paso a paso, preguntas frecuentes, guía de versiones y capturas de todas
  las vistas y de los 11 temas.
- `tools\publicar-version.ps1` publica una versión nueva con un solo comando.
- Una copia en cola muestra la barra quieta y vacía mientras espera su turno, en vez de la animación de «cargando».

### Arreglado
- La barra de progreso ya no se sale de su sitio (sobre el icono de la lista o fuera del borde) al preparar una copia.
- La ventana pequeña sin copias ya no corta el icono, los botones ni los enlaces de abajo: crece lo necesario.

## [1.0.0] - 2026-10-17

Primera versión pública.

### Copia y fiabilidad
- Reanudación tras un apagón dentro del propio archivo, con diario por bloques y comprobación de lo ya copiado.
- Pausa, saltar, detener y seguir; reintentos; espera a que vuelva el disco, la red o el espacio.
- Carpetas «todo o nada», modo rescate, archivos en uso con VSS, modo copia de seguridad y copia de EFS sin descifrar.
- Escudo contra ransomware antes de sobrescribir copias.
- Datos de recuperación Reed-Solomon y `mcopy repair`.
- Modo forense con manifiesto SHA-256 firmado y `mcopy verify-manifest`.

### Velocidad
- E/S directa en paralelo, colas por disco, archivos pequeños en paralelo, IoRing y autoajuste por disco.
- Clonado en ReFS, copia en el servidor y compresión SMB, copia delta.
- Varios orígenes a la vez y conexiones con servidores que se ajustan solas (hasta 255).
- ZIP y 7z comprimidos en paralelo.

### Qué y cómo copiar
- Espejo, solo actualizar, solo carpetas, organizar fotos por fecha y simulación.
- Filtros en lenguaje normal y por el texto de las fotos (OCR de Windows).
- Versiones con limpieza automática y copias de seguridad sin duplicados (`mcopy backup`, `snapshots`, `restore`).
- Copia cifrada, a ZIP cifrado y a 7z cifrado; borrado seguro de 1, 3 o 7 pasadas.
- Sincronización en dos sentidos con detección de renombrados, vista de comparación y sincronización en tiempo real.

### Dónde copiar
- Móviles y cámaras (MTP), LocalSend en los dos sentidos y envío a otro PC con MegaCopier.
- FTP, FTPS, SFTP, WebDAV, S3, OneDrive, Google Drive y Dropbox.

### Uso diario
- Sustituye Ctrl+V, el menú contextual (también el corto de Windows 11) y el arrastre del Explorador.
- Varios destinos, favoritos, carpetas abiertas del Explorador, Total Commander y Directory Opus.
- Cola por disco, línea de tiempo, bandeja de arrastre, tareas programadas que se ponen al día, historial con deshacer.
- Aviso SMART, avisos por correo y webhook, control desde el móvil y servidor MCP para asistentes de IA.
- Consola `mcopy`, modo portátil, 14 idiomas, 11 temas y accesibilidad con lectores de pantalla.

[Sin publicar]: https://github.com/cbatista96/MegaCopier/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/cbatista96/MegaCopier/releases/tag/v1.0.0
