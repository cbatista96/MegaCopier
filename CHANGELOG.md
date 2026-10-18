# Novedades

Todos los cambios importantes de MegaCopier. El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y las versiones, [SemVer](https://semver.org/lang/es/).

## [Sin publicar]

## [1.1.0] - 2026-10-17

Reconstruimos los ajustes con el diseño de la Configuración de Windows 11, añadimos 41 temas nuevos, conjuntos de iconos
que se combinan con cualquier tema, sonidos propios y nuevas opciones de comportamiento y de errores.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/ajustes-madera.png" width="820" alt="Ajustes con el tema Madera y los iconos Mosaico">

### Añadido

#### Ajustes renovados con el diseño de Windows 11

- **Secciones a la izquierda**, cada una con su icono: General, Interfaz, Sonidos, Copia, Comportamiento, Errores, Cola y
  rendimiento, Red y dispositivos, Automatización y Avanzado. Antes era una sola página muy larga.
- **Buscador de ajustes**: escribe «sonido», «USB» o «errores» y aparecen solo las opciones que lo mencionan, sean de la
  sección que sean.
- **Cada opción tiene su icono** y, al pulsarla, el icono da un pequeño salto. Al cambiar de sección, el contenido entra
  deslizándose.
- En una ventana estrecha, la lista de secciones se queda solo con los iconos.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/ajustes.png" width="820" alt="Ajustes en el tema Fluent oscuro">

#### 41 temas nuevos (52 en total)

- Diseñados para Windows 11, con paletas propias, degradados y texturas: desde oscuros intensos hasta claros suaves.
- **Oscuros**: Combustión, Caja negra, Metro oscuro, Robot verde, Cielo de otoño, Ámbar, Granate, Código verde, Vapor, Metro
  gris, Televisión, Reproductor, Humo, Berenjena, Cobalto, Esmeralda, Mandarina, Aurora, Galaxia, Neón y Burbuja azul.
- **Claros**: Aire, Metro claro, Pez azul, Línea plana, Plus, Cristal, Cielo, Menta, Cuaderno, Madera, Brillo, Nieve, Burbuja
  lima, Tóxico, Naranja tierra, Aero, Moderno 8, Mensajero, Luna y Plata.
- **Fondos con degradado y texturas**: brasas (Combustión), fibra de carbono (Caja negra), lluvia digital (Código verde),
  aurora boreal (Aurora), galaxia (Galaxia), rejilla de neón (Neón), ondas (Burbuja azul), madera (Madera), papel de
  cuaderno (Cuaderno) y metal cepillado (Cristal). Están hechas con código (`tools/TextureGen`), sin imágenes de terceros.
- **El tema colorea toda la ventana**: botones, desplegables, cuadros de texto, interruptores, casillas,
  listas, el menú lateral, los menús, los diálogos, los avisos y hasta las **barras de desplazamiento**.

<table>
<tr>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/combustion.png" alt="Combustión"><br>Combustión</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/codigo-verde.png" alt="Código verde"><br>Código verde</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/aurora.png" alt="Aurora"><br>Aurora</td>
</tr>
<tr>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/galaxia.png" alt="Galaxia"><br>Galaxia</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/neon.png" alt="Neón"><br>Neón</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/caja-negra.png" alt="Caja negra"><br>Caja negra</td>
</tr>
<tr>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/madera.png" alt="Madera"><br>Madera</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/cuaderno.png" alt="Cuaderno"><br>Cuaderno</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/cristal.png" alt="Cristal"><br>Cristal</td>
</tr>
<tr>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/burbuja-azul.png" alt="Burbuja azul"><br>Burbuja azul</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/berenjena.png" alt="Berenjena"><br>Berenjena</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/humo.png" alt="Humo"><br>Humo</td>
</tr>
<tr>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/sc3-azul.png" alt="Cobalto"><br>Cobalto</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/aero.png" alt="Aero"><br>Aero</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/temas/luna.png" alt="Luna"><br>Luna</td>
</tr>
</table>

#### Conjuntos de iconos, aparte del tema

- **Siete estilos**, todos con los iconos modernos de Windows 11, que se combinan con cualquier tema (*Ajustes > Interfaz >
  Iconos*):
  - **Fluent**: como Windows.
  - **Color**: cada icono con su color (reproducir verde, pausa ámbar, detener rojo, carpetas amarillas…).
  - **Mosaico**: icono blanco sobre un cuadrado de color, como la Configuración de Windows 11.
  - **Suave**: icono de color sobre un fondo suave del mismo color.
  - **Degradado**: con los colores de las barras del tema.
  - **Neón**: color vivo con un halo de luz.
  - **Windows 10**: los iconos clásicos.
- Cambian a la vez todos los iconos: la barra de herramientas, los botones de cada copia, los ajustes, las listas y los
  diálogos.

<table>
<tr>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/iconos/color.png" alt="Iconos Color"><br>Color</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/iconos/mosaico.png" alt="Iconos Mosaico"><br>Mosaico</td>
</tr>
<tr>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/iconos/degradado.png" alt="Iconos Degradado"><br>Degradado</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/iconos/neon.png" alt="Iconos Neón"><br>Neón</td>
</tr>
</table>

#### Nuevas opciones de comportamiento y de errores

- **Si un archivo sigue fallando, preguntar qué hacer** (*Ajustes > Errores*): la copia se para y explica el motivo («otro
  programa tiene el archivo abierto»…), con **Reintentar**, **Omitir**, **Omitir también los siguientes errores de esta
  copia** y **Detener la copia** (se puede reanudar). Sigue existiendo decidirlo de antemano: omitir y seguir, detener o
  reintentar al final.
- **Reintentos y espera entre intentos** en la misma sección.
- **Informe de errores automático**: al terminar una copia con errores se guarda un archivo de texto con cada archivo que
  falló y por qué, en la carpeta de destino o en una carpeta fija, con el nombre que quieras (por defecto `errorlog.txt`).

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/ajustes-errores.png" width="820" alt="Ajustes de errores">

- **Añadir las copias nuevas a una que ya está en marcha** (*Ajustes > Comportamiento*): nunca, si van a la misma carpeta,
  si usan los mismos discos o siempre. Con **Preguntar antes de añadirlas** pide confirmación; si dices que no, la copia
  nueva sigue por su cuenta.
- **Al detener una copia, borrar lo que quedó a medias** (los `.mcpart`), con la opción de no hacerlo si la copia paró por un
  error. Solo actúa cuando pulsas Detener; nunca al cerrar MegaCopier ni tras un apagón.
- **Al terminar, expulsar la unidad de destino** si es una memoria USB, una tarjeta o un disco USB y ninguna otra copia la
  está usando, con la opción de **forzarlo** aunque algún programa la tenga abierta.
- **Al mover, conservar también** atributos, permisos, propietario y auditoría, con sus propias casillas, aparte de las de
  copiar.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/ajustes-comportamiento.png" width="820" alt="Ajustes de comportamiento">

- En *Ajustes > Interfaz*:
  - **Mostrar el icono en la bandeja** (si lo ocultas, MegaCopier sigue en marcha y se abre ejecutándolo otra vez).
  - **Iniciar las copias con la ventana minimizada**: aparece en la barra de tareas sin taparte nada.
  - **Recordar la ventana**: abrirla donde estaba y con el mismo tamaño.
  - **Ventana translúcida**: fondo acrílico más transparente, con cualquier tema.
- En *Ajustes > Avanzado*: la **carpeta de datos** a la vista con un botón para abrirla, y **Restablecer los ajustes** de
  fábrica (las copias, el historial y los temas propios se conservan).

#### Sonidos propios

- MegaCopier tiene ahora **sus propios sonidos**, campanillas originales hechas con código (`tools/SoundGen`), en vez de los
  avisos de Windows: **copia terminada**, **todas terminadas**, **error** y **hace falta tu decisión**.
- Vienen **activados por defecto**. En *Ajustes > Sonidos* puedes cambiar cada uno por el de Windows, por uno de
  `C:\Windows\Media` o por un archivo tuyo, y probarlo con ▶.

### Cambiado

- Una copia en cola muestra la barra quieta y vacía mientras espera su turno, en vez de la animación de «cargando».
- Las opciones de la ventana, los temas y los iconos están juntos en *Ajustes > Interfaz*.
- Documentación nueva y completa: guía de uso paso a paso, preguntas frecuentes, guía de versiones y capturas de todas
  las vistas, de los temas y de los conjuntos de iconos.
- `tools\publicar-version.ps1` publica una versión nueva con un solo comando.

### Arreglado

- La barra de progreso ya no se sale de su sitio (sobre el icono de la lista o fuera del borde) cuando una copia está en
  cola, en pausa o preparándose.
- La ventana pequeña sin copias ya no corta el icono, los botones ni los enlaces de abajo: crece lo necesario.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.1.0/docs/capturas/vacia.png" width="480" alt="Ventana sin copias">

- Un texto sin traducir ya no puede cerrar MegaCopier al arrancar.

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

[Sin publicar]: https://github.com/cbatista96/MegaCopier/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/cbatista96/MegaCopier/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/cbatista96/MegaCopier/releases/tag/v1.0.0
