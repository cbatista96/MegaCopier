# Novedades

Todos los cambios importantes de MegaCopier. El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y las versiones, [SemVer](https://semver.org/lang/es/).

## [Sin publicar]

### Añadido

- Tres vistas de la ventana de copia: la pequeña (la de siempre al copiar), la de la lista de archivos («Más») y la de
  varias copias, que añade a la izquierda la lista de copias solo cuando hay más de una.
- Se puede pulsar en el panel de la copia:
  - el título abre la carpeta que se está copiando;
  - el archivo en curso se muestra marcado en su carpeta;
  - el origen y el destino muestran, marcado en el Explorador, lo que se copia y su copia.
- Un solo selector para archivos y carpetas, con un botón «Seleccionar». Marcas una carpeta para elegirla entera, o
  entras en ella y marcas solo algunos archivos. Se usa en Copiar, Mover, Enviar, Comprobar, Crear sumas, Borrar,
  copiar a un móvil y el «+» de la lista.
- 16 conjuntos de iconos nuevos inspirados en otros sistemas:
  - Android y Android tonal;
  - macOS y macOS color;
  - Ubuntu, KDE Plasma, GNOME, elementary OS, Linux Mint, Fedora, Pop!_OS y Arch Linux;
  - Windows XP, Windows 7 Aero y Windows 8 Metro;
  - Steam.

  Cada uno tiene su forma (círculos, «squircles», contornos, mosaicos planos), su paleta y su brillo de cristal.
- 19 sonidos originales de MegaCopier para cualquier momento:
  - avisos cortos (bip, gota, burbujas, pop…);
  - notificaciones de 1,5 a 3 s (cristal, arpa, marimba, logro…);
  - melodías de hasta 4 s (aurora, amanecer, cascada, orbital).
- «Mostrar más / Mostrar menos» en las galerías de temas, iconos y estilos de barra: se ven los 12 primeros y el resto
  al desplegar.
- Animaciones al pulsar cualquier botón: el botón se hunde y su icono se mueve según lo que significa. El engranaje
  gira, reproducir avanza, pausa se aprieta y la papelera se sacude.
- Copias con «acceso denegado» resueltas solas: al terminar, Windows pide permiso de administrador una vez y esos
  archivos se copian, como en el Explorador (por ejemplo, los `.exe` de los juegos de Xbox). Se puede desactivar en
  *Ajustes > Errores*. Con MegaCopier ya abierto como administrador, se copian sin preguntar.

### Cambiado

- Arrastrar archivos con el botón izquierdo entre ventanas del Explorador copia con MegaCopier de serie, como Ctrl+V.
  También se activa al actualizar.
- La ventana pequeña:
  - se puede ensanchar, pero no hacer más alta;
  - no se maximiza;
  - sus botones (Continuar, Saltar, Detener…) y los chips «En pausa» y «Archivo» siempre muestran su texto.
- La vista con la lista tiene un tamaño mínimo con el que todo cabe, empieza con un tamaño cómodo y se puede maximizar.
- «Saltar» se ve también con la copia en pausa: descarta el archivo y la copia sigue en pausa, lista para empezar el
  siguiente.
- «Detener» la única copia la detiene y cierra la ventana. Lo copiado se conserva y al volver a abrir MegaCopier se
  ofrece continuarla.
- La pregunta «la carpeta ya existe» en la ventana pequeña aparece dentro de ella: las opciones en una fila y su
  explicación al pasar el ratón, sin tener que desplazarse.
- El menú «Nueva», ordenado en grupos y submenús:
  - Copiar y Mover;
  - Enviar y móviles;
  - Comprobar y reparar;
  - Copias de seguridad;
  - Borrar.

  Ya no repite cada opción para archivos y para carpetas.
- La lista de archivos:
  - la búsqueda queda pegada a la izquierda y encoge si falta sitio;
  - las casillas van siempre a su lado;
  - la barra de botones empieza a la altura de «Origen» y enseña siempre todos sus botones (más pequeños si la lista es
    baja);
  - el archivo en curso lleva un círculo que se llena con su progreso.
- En Ajustes, cada control (desplegable, interruptor, número, botón) y su ⓘ quedan centrados en su tarjeta.
- Cambiar de tema se hace con un fundido suave y la página ya no vuelve al principio.
- La vista previa de las barras se queda a media barra mientras «copia» (también después de verla «Terminada»).
- En Sonidos, «Elegir un archivo…» está al principio de la lista, con los sonidos agrupados en MegaCopier y Windows.
- «Archivos y carpetas» en Opciones lleva un solo icono.
- El menú «…» de la ventana pequeña lleva iconos en todas sus opciones.

### Arreglado

- Los sonidos propios de MegaCopier no sonaban: «MegaCopier (sonido propio)» usaba en realidad los de Windows, y la
  opción «El de Windows» no sonaba.
- El recuadro del tema, el conjunto de iconos o el estilo de barra elegidos ya no se corta por la izquierda.

## [1.2.0] - 2026-10-18

Renovamos las barras de progreso con 22 estilos animados y personalización completa, reconstruimos la lista de archivos de
cada copia (botones a la vista, selección múltiple, origen y destino, guardar y seguir donde quedó), añadimos la pregunta
de «la carpeta ya existe» con la opción de pasar lo que ya está, el límite de velocidad en vivo, 21 temas nuevos y una
ayuda ⓘ con ejemplos en las opciones más complejas.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.2.0/docs/capturas/barras.png" width="820" alt="Galería de estilos de barras de progreso">

### Añadido

#### Barras de progreso personalizables

- **22 estilos de barra**: Fluent, Aero (con el reflejo que pasa una y otra vez), Ola (el borde ondula como un líquido),
  Rayas en movimiento, Neón (con un halo que respira), Bloques, LED, Arcoíris, Latido, Cometa (con estela y chispas),
  Cristal, Fuego, Terminal (`[#####·····]` con cursor), Línea fina con marcador, Doble capa, Píxeles, Metal, Contorno,
  Degradado fluido, Burbujas, Semáforo y Segmentos.
- **Cada barra con su propio aspecto**: la del total, la del archivo y las mini-barras de la lista de copias.
- **Personalización completa** en *Ajustes > Interfaz > Barras de progreso*, con una vista previa en movimiento que
  enseña cómo queda copiando, en pausa, con error y al terminar:
  - colores del tema, propios (con muestras listas para usar y selector de color), semáforo o arcoíris;
  - grosor, redondeo, velocidad de los efectos y brillo;
  - porcentaje a la derecha, dentro de la barra u oculto;
  - avance suave, elástico o a saltos;
  - efectos según el estado: rayas ámbar en pausa, sacudida con error y destello verde al terminar;
  - borde fino y «Reducir animaciones» (también se activa solo si Windows tiene desactivados los efectos de animación).
- Los temas propios guardan también el aspecto de las barras y lo llevan consigo al exportarlos.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.2.0/docs/capturas/barras-copia.png" width="520" alt="Barras Aero y Rayas en movimiento copiando">

#### Lista de archivos de cada copia

- **Botones a la vista** junto a la lista: al principio, subir, bajar, al final, añadir archivos o carpetas, quitar,
  guardar la lista y cargar una lista. Con atajos: Alt+Inicio, Alt+↑, Alt+↓, Alt+Fin y Supr.
- **Selección múltiple** con Mayús y Ctrl: los botones mueven o quitan todos los elegidos a la vez, en su orden.
- **Columnas Origen, Tamaño y Destino** con la ruta completa de cada lado; las sumas (hash), en el menú «⋯».
- **Guardar la lista guarda lo que falta por copiar**, con su destino y sus opciones. Al cargarla, la copia empieza sola y
  sigue justo donde se quedó. Si el destino ya no está (una memoria USB que se quitó), MegaCopier lo avisa y deja elegir
  otro lugar para copiar el resto.
- **Soltar archivos en cualquier parte de una copia** (el panel o las pestañas) los añade al final de esa copia y al mismo
  destino; sobre la lista, una línea marca la posición exacta.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.2.0/docs/capturas/lista.png" width="820" alt="Lista de archivos con sus botones y varios archivos elegidos">

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.2.0/docs/capturas/destino-falta.png" width="620" alt="Aviso al cargar una lista cuyo destino ya no está">

#### Carpetas que ya existen en el destino

- Al copiar una carpeta que ya está en el destino con algunos de sus archivos, MegaCopier pregunta antes de empezar:
  **Pasar los que ya están** (comprueba cada archivo y copia solo los que faltan), **Sobrescribir**, **Renombrar** (copia
  en «Fotos (2)»), **Preguntar uno por uno** o **Cancelar la copia**. Es lo predeterminado y se puede cambiar en
  *Ajustes > Copia > Reglas*.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.2.0/docs/capturas/carpeta-existe.png" width="620" alt="La carpeta ya existe en el destino">

#### 21 temas nuevos (73 en total)

- **Oscuros**: Material oscuro, Cobre oscuro, Silicio oscuro, Brisa oscura, Pizarra, Arcade, Ártico, Vampiro, Capuchino,
  Noche de Tokio, Retro cálido, Solar oscuro, Átomo y Menta oscura.
- **Claros**: Material claro, Cobre claro, Silicio claro, Brisa clara, Tiza, Solar claro y Capuchino claro.

<table>
<tr>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.2.0/docs/capturas/temas/material-oscuro.png" alt="Material oscuro"><br>Material oscuro</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.2.0/docs/capturas/temas/silicio-claro.png" alt="Silicio claro"><br>Silicio claro</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.2.0/docs/capturas/temas/capuchino.png" alt="Capuchino"><br>Capuchino</td>
</tr>
<tr>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.2.0/docs/capturas/temas/arcade.png" alt="Arcade"><br>Arcade</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.2.0/docs/capturas/temas/vampiro.png" alt="Vampiro"><br>Vampiro</td>
<td><img src="https://github.com/cbatista96/MegaCopier/raw/v1.2.0/docs/capturas/temas/tiza.png" alt="Tiza"><br>Tiza</td>
</tr>
</table>

#### Ayuda en cada opción

- **Botón ⓘ en 26 opciones**: explica para qué sirve cada una y en qué casos conviene usarla, con ejemplos.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.2.0/docs/capturas/ajustes-ayuda.png" width="820" alt="Ayuda de la opción Verificar después de copiar">

### Cambiado

- **El límite de velocidad se cambia en vivo**: cada copia tiene en su pestaña Opciones un interruptor y un deslizador
  (de 256 KB/s a 1 GB/s) que se aplican al momento, sin pararla. Cambiar el límite en *Ajustes* afecta también a las copias
  en marcha que lo usaban.
- Casi todas las opciones de una copia en marcha se aplican al momento (qué hacer si el archivo existe, fechas, atributos,
  permisos, errores y reintentos); solo el modo, los filtros, el bloque, los hilos y la suma esperan a reanudarla.
- **«Volver» pasa a la izquierda**, junto al título, como en la Configuración de Windows 11, en Ajustes, Historial, Línea
  de tiempo y Comparar. También vuelve con Esc y con Alt+←.
- Descripciones de opciones reescritas en un lenguaje más claro (verificar, qué hacer si el archivo existe, tipo de
  comprobación, rendimiento y prioridad).
- Los temas que llevaban nombres de otros programas se llaman ahora Azul clásico, Gris clásico, Cobalto, Esmeralda y
  Mandarina.

### Arreglado

- Las ayudas con varias líneas (como los detalles de un archivo en la lista) mostraban «\n» en lugar de saltos de línea.
- La barra del archivo ya no se desliza hacia atrás al empezar el siguiente archivo: vuelve al principio de golpe.

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

[Sin publicar]: https://github.com/cbatista96/MegaCopier/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/cbatista96/MegaCopier/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/cbatista96/MegaCopier/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/cbatista96/MegaCopier/releases/tag/v1.0.0
