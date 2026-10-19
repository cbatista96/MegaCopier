# Novedades

Todos los cambios importantes de MegaCopier. El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y las versiones, [SemVer](https://semver.org/lang/es/).

## [Sin publicar]

## [1.4.0] - 2026-10-19

Una versión para que copiar sea más seguro y más estable: MegaCopier comprueba que lo que copias cabe antes de empezar
(y te deja forzarlo), «Detener» por fin cancela, las copias ya no se repiten al volver a abrir, dos copias en el mismo
disco pueden ir a la vez y el arrastre desde el Explorador es más fiable. Además se acaban los cierres repentinos y los
textos apagados al cambiar el tamaño de la ventana, y la lista de archivos gana columnas ajustables.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.4.0/docs/capturas/lista.png" width="620" alt="Lista de archivos con líneas divisorias entre Origen, Tamaño y Destino, y la copia en pausa">

### Añadido

- **Aviso de falta de espacio con dos salidas**: antes de copiar, si lo que se va a copiar no cabe en el destino,
  MegaCopier dice cuánto hace falta y cuánto hay libre, y ofrece *Cancelar* (no se copia nada) o *Forzar la copia*: copia
  hasta llenar el disco y entonces espera; al liberar espacio, sigue sola. Lo mismo al añadir archivos o carpetas a una
  copia en marcha (con lo que aún le queda por copiar). En la ventana pequeña la pregunta cabe dentro de ella.

  <img src="https://github.com/cbatista96/MegaCopier/raw/v1.4.0/docs/capturas/espacio.png" width="520" alt="No hay espacio suficiente: hacen falta 950 MB y hay 100 MB libres, con Forzar la copia y Cancelar">

- **Líneas divisorias en la lista de archivos**: entre Origen, Tamaño y Destino; se arrastran para darle a cada columna
  el ancho que quieras (se recuerda) y con doble clic vuelven al de siempre.
- **«Empezar ya»** en una copia que espera su turno porque otra usa el mismo disco: empieza en ese momento y las dos
  copian a la vez. *Continuar todas* también arranca las que esperaban.

  <img src="https://github.com/cbatista96/MegaCopier/raw/v1.4.0/docs/capturas/varias-copias.png" width="620" alt="Dos copias: la segunda espera turno y el botón Empezar ya la arranca">

- Al arrastrar algo sobre una copia, el aviso dice qué se añade: «Añadir «Vídeos» a esta copia» o «Añadir 3 elementos
  a esta copia».
- Los botones que solo llevan icono (la lista de copias, Ajustes, el «…» de la lista…) tienen nombre para los lectores
  de pantalla: el de su ayuda emergente.

### Cambiado

- **«Detener» cancela la copia** y la quita de la lista: ya no queda como copia para continuar al volver a abrir. Lo que
  quedó a medias se borra si así lo tienes en *Ajustes > Comportamiento*; si no, al volver a copiar lo mismo se sigue
  desde donde quedó.
- **Nunca la misma copia repetida**: si MegaCopier se cerró con varias copias guardadas de lo mismo, al abrirlo queda
  una sola; y si vuelves a copiar algo que estaba detenido, se continúa esa copia en lugar de crear otra.
- **Pausa y Continuar son un solo botón**, siempre del mismo tamaño, y su icono se transforma del uno al otro.
- **La ventana pequeña vuelve siempre a su ancho** al pulsar «Menos», y «Menos» tiene su animación de vuelta: la barra
  de herramientas y las pestañas suben mientras la ventana encoge.
- **La lista de archivos**: la barra de botones empieza a la altura del primer archivo y los archivos van más cerca de
  ella; al subir o bajar archivos siguen elegidos (y la lista ya no parpadea).
- **Pestañas y menús animados**: la línea de la pestaña elegida se desliza de una a otra, igual que la marca de la
  sección en Ajustes y de la copia en la lista de copias; los iconos de las pestañas se mueven según su significado.
- **La lista de copias flotante** (con la ventana estrecha) tiene fondo opaco y un borde, en lugar de acrílico con sombra.
- **Menús «…»** sin el hueco de la izquierda (lo activado lleva una marca a la derecha); «Mostrar las sumas» tiene icono.
- **Ajustes**:
  - las galerías enseñan filas completas (15 temas con cinco por fila) antes de «Mostrar más»;
  - en la tarjeta Colores, las muestras van separadas del texto y el desplegable y la ⓘ, centrados;
  - «Sincronización y seguridad» va en su propia tarjeta, separada de los filtros;
  - la ⓘ de «Al mover, conservar también» queda centrada en su tarjeta;
  - «Conexiones a la vez» queda alineado y los números de todos los campos, centrados.
- **Arrastrar con el botón izquierdo entre ventanas del Explorador, más fiable**: lo arrastrado se lee mientras arrastras
  (al soltar solo falta mirar el destino), se reconoce al momento si sueltas sobre una carpeta, y MegaCopier comprueba
  mejor que el Explorador canceló su copia antes de hacer la suya, para que nunca copien los dos. Los motivos por los que
  un arrastre se deja al Explorador quedan en `drag.log`, en la carpeta de datos.

### Arreglado

- MegaCopier podía cerrarse de golpe al cambiar el tamaño de la ventana o usar «Más» y «Menos» con una copia en pausa
  (un fallo de WinUI al animar las filas de la lista de archivos). Si alguna vez vuelve a cerrarse así, deja en
  `crashstack.log`, en la carpeta de datos, lo necesario para saber por qué.
- Al cambiar el tamaño de la ventana, usar «Más» y «Menos» o con una copia en pausa, algunos textos, iconos y botones
  se quedaban dibujados apagados hasta pasar el ratón por encima.
- Al agrandar la ventana ya no asoma una franja negra a la derecha: sale del color del tema.
- Ctrl+V y el arrastre en el Explorador podían dejar de pasar por MegaCopier mientras la ventana estaba ocupada.
- Al reabrir MegaCopier tras cerrarlo desde la bandeja con una copia en marcha, aparecían varias copias de la misma
  carpeta, y al continuarlas sonaba el aviso de cada una.
- Con dos copias en el mismo disco, no se podía continuar la segunda mientras la primera estaba en pausa.
- La lista de archivos de una copia que esperaba su turno se quedaba vacía al empezar.

## [1.3.0] - 2026-10-18

Renovamos la ventana de copia con tres vistas (la pequeña, la de la lista y la de varias copias), reconstruimos el menú
«Nueva» con un único selector para archivos y carpetas, añadimos 16 conjuntos de iconos al estilo de otros sistemas y 19
sonidos originales, y pulimos los detalles: botones con animación, rutas que se pueden pulsar, preguntas que caben en la
ventana pequeña, ajustes perfectamente alineados y copias con «acceso denegado» que se resuelven solas.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.3.0/docs/capturas/lista.png" width="620" alt="Vista con la lista de archivos, la búsqueda a la izquierda y el círculo de progreso del archivo en curso">

### Añadido

#### Tres vistas de la ventana de copia

- **Pequeña**, la de siempre al copiar: solo las barras y los botones. Se puede ensanchar, pero no hacer más alta (no
  habría nada más que ver) ni maximizar.
- **Con la lista** («Más»): los archivos, el destino, las opciones, la gráfica y el registro. Empieza con un tamaño cómodo,
  tiene un mínimo con el que todo cabe y se puede maximizar.
- **Varias copias**: la misma vista, con la lista de copias a la izquierda. Aparece sola cuando hay más de una copia a la
  vez.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.3.0/docs/capturas/compacta.png" width="520" alt="Ventana pequeña en pausa, con Continuar, Saltar y Detener">

#### Todo el panel se puede pulsar

- **El título** («Copiando «Juegos»») abre la carpeta que se está copiando.
- **El archivo** en curso se muestra marcado en su carpeta.
- **El origen** muestra lo que se copia, marcado en la carpeta donde está.
- **El destino** muestra la copia ya en el destino, marcada.

#### Un solo selector para archivos y carpetas

- Es el diálogo «Abrir» de Windows con un botón más, **«Seleccionar»**:
  - marcas una carpeta y la eliges entera;
  - entras en ella y marcas solo algunos archivos;
  - sin nada marcado, eliges la carpeta que estás viendo.
- Se usa en Copiar, Mover, Enviar a otro equipo, Comprobar, Crear archivo de sumas, Borrar, copiar a un móvil y el botón
  «+» de la lista de archivos.

#### 16 conjuntos de iconos inspirados en otros sistemas (23 en total)

- **Android** y **Android tonal**: círculos de color o tonos suaves, con la paleta de Material.
- **macOS** y **macOS color**: cuadrados muy redondeados con degradado y reflejo, o los colores del sistema de Apple.
- **Ubuntu, KDE Plasma, GNOME, elementary OS, Linux Mint, Fedora, Pop!_OS y Arch Linux**: la forma y la paleta de cada
  escritorio, con contornos, círculos con brillo y fondos suaves.
- **Windows XP, Windows 7 Aero y Windows 8 Metro**: brillo de cristal, Aero y mosaicos planos.
- **Steam**: fondos suaves con sus azules y verdes.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.3.0/docs/capturas/iconos-sistemas.png" width="820" alt="Galería de conjuntos de iconos con Android, macOS, Ubuntu, KDE, GNOME y elementary OS">

#### 19 sonidos originales

- Sintetizados para MegaCopier, sin muestras de terceros, y para cualquier momento (copia terminada, con errores, todas
  terminadas o cuando hace falta decidir algo):
  - **avisos cortos** de menos de un segundo: bip, doble bip, pop, gota, burbujas y toque;
  - **notificaciones** de 1,5 a 3 segundos: pregunta, aviso, alerta grave, marimba, destellos, cristal, eco, arpa y logro;
  - **melodías** de hasta 4 segundos: cascada, aurora, orbital y amanecer.

#### Más detalles

- **«Mostrar más / Mostrar menos»** en las galerías de temas, iconos y estilos de barra: se ven los 12 primeros y el resto
  al desplegar.
- **Animaciones al pulsar** cualquier botón: el botón se hunde y su icono se mueve según lo que significa. El engranaje
  gira, reproducir avanza, pausa se aprieta, la papelera se sacude y el historial gira hacia atrás.
- **Copias con «acceso denegado» resueltas solas**: algunos archivos solo se leen con permisos de administrador (por
  ejemplo, los `.exe` de los juegos de Xbox y de la Microsoft Store; ni la copia de Windows puede leerlos sin ellos).
  MegaCopier copia todo lo demás y al final pide permiso una sola vez, como el Explorador. Se puede desactivar en
  *Ajustes > Errores*; con MegaCopier abierto como administrador, se copian sin preguntar.

### Cambiado

- **Arrastrar con el botón izquierdo** entre ventanas del Explorador copia con MegaCopier de serie, igual que Ctrl+V.
  También se activa al actualizar; se puede desactivar en *Ajustes > General*.
- **«Saltar» también en pausa**: descarta el archivo y la copia sigue en pausa, lista para empezar el siguiente.
- **«Detener» la única copia** la detiene y cierra la ventana. Lo copiado se conserva y al volver a abrir MegaCopier se
  ofrece continuarla.
- **La pregunta «la carpeta ya existe» en la ventana pequeña** aparece dentro de ella: las opciones en una fila, con su
  explicación al pasar el ratón, sin tener que desplazarse.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.3.0/docs/capturas/carpeta-pequena.png" width="520" alt="La carpeta ya existe, preguntado dentro de la ventana pequeña">

- **La ventana pequeña** muestra siempre el texto de sus botones (Continuar, Saltar, Detener…) y los chips «En pausa» y
  «Archivo» con su icono.
- **El menú «Nueva»**, ordenado en grupos y submenús, sin opciones repetidas para archivos y para carpetas:
  - Copiar, Mover, Copiar a y Cargar una lista;
  - Enviar a otro equipo, Móviles, cámaras y servidores, Comparar y sincronizar;
  - Comprobar y reparar, Copias de seguridad;
  - Borrar y Borrar de forma segura.
- **La lista de archivos**:
  - la búsqueda queda pegada a la izquierda, crece hasta un máximo y encoge si falta sitio;
  - *Ocultar los terminados* y *Seguir al actual* van siempre a su lado, en la misma fila;
  - la barra de botones empieza a la altura de «Origen» y enseña siempre todos sus botones (más pequeños si la lista es
    baja);
  - el archivo en curso lleva un círculo que se va llenando con su progreso y se cierra al terminarlo.
- **Ajustes, alineados**: cada control (desplegable, interruptor, número, botón) y su ⓘ quedan centrados en su tarjeta, y
  los campos de número tienen el valor centrado. Las horas del límite por horario pasan a dos líneas si no caben.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.3.0/docs/capturas/ajustes-centrado.png" width="820" alt="Ajustes con los controles y la ayuda centrados en cada tarjeta">

- **Cambiar de tema** se hace con un fundido suave y la página ya no vuelve al principio.
- **La vista previa de las barras** se queda a media barra mientras «copia», también después de verla «Terminada».
- **Sonidos**: «Elegir un archivo…» está el primero de la lista, con los sonidos agrupados en MegaCopier y Windows.
- «Archivos y carpetas» en Opciones lleva un solo icono, y el menú «…» de la ventana pequeña lleva iconos en todas sus
  opciones.

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

[Sin publicar]: https://github.com/cbatista96/MegaCopier/compare/v1.4.0...HEAD
[1.4.0]: https://github.com/cbatista96/MegaCopier/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/cbatista96/MegaCopier/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/cbatista96/MegaCopier/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/cbatista96/MegaCopier/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/cbatista96/MegaCopier/releases/tag/v1.0.0
