# Novedades

**Español** · [English](CHANGELOG.en.md)

Todos los cambios importantes de MegaCopier. El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y las versiones, [SemVer](https://semver.org/lang/es/).

## [Sin publicar]

### Resumen

- Puedes buscar, descargar e instalar actualizaciones desde Ajustes sin interrumpir tus copias. El mismo botón acompaña cada paso, con progreso, avisos de Windows y un indicador que se adapta al conjunto de iconos elegido.
- Decide cuánto automatizar: buscar cada dos días, descargar las versiones detectadas automáticamente o instalarlas cuando MegaCopier esté en la bandeja y no tenga trabajo pendiente. Por defecto solo se activa la búsqueda; las descargas respetan tus permisos para Wi-Fi, cable y redes de uso medido.
- Consulta las principales novedades de la versión disponible y abre el registro completo con sus imágenes, dentro de la aplicación. El historial incluye versiones anteriores y conserva español para la interfaz española e inglés para las demás.
- Los informes de errores opcionales ayudan a mejorar MegaCopier sin adjuntar archivos, nombres ni rutas. Puedes elegir durante la instalación y en Ajustes si enviarlos en segundo plano o revisar cada informe antes de enviarlo.
- El instalador organiza primero los accesos directos opcionales y después la integración. Presenta una imagen lateral de MegaCopier y la política de privacidad antes de instalar; la búsqueda automática y los informes se pueden desmarcar.
- Ajustes navega con transiciones fluidas y permite volver a las copias con el botón superior. Los botones laterales del ratón también sirven para retroceder y avanzar, y Sonidos separa avisos, volumen, personalización y silencio durante juegos o presentaciones.
- Se corrigen el cierre al arrancar tras consultar la red, la ayuda «Atrás» que aparecía fuera de su botón y los recortes o desalineaciones de cabeceras e indicadores. Las actualizaciones verifican el manifiesto firmado y la suma del paquete antes de instalarlo.

### Añadido

- **Centro de actualizaciones:** buscar, descargar, cancelar, verificar e instalar con una sola acción de tamaño fijo a la derecha y una barra compartida para búsqueda y descarga. El modo portátil ofrece el ZIP solo si existe una versión nueva.
- **Automatización independiente:** búsqueda cada 48 horas y recuperación de comprobaciones vencidas al volver Internet. Solo una versión descubierta por una búsqueda automática puede descargarse automáticamente; una búsqueda manual ofrece Descargar. Descargar e instalar vienen desactivados. La instalación automática espera a la bandeja, sin copias activas, pendientes o pausadas, diálogos ni sincronización en directo.
- **Permisos de red:** Wi-Fi y cable sin uso medido por defecto; datos móviles y redes de uso medido requieren permiso. Los cambios se aplican también a la descarga en curso. La interfaz no muestra el tipo de conexión actual.
- **Avisos nativos de Windows:** nueva versión disponible, descarga lista y actualización completada. Abren Actualizaciones; el aviso pendiente de actualización completada desaparece al entrar en esa sección.
- **Novedades e historial:** resumen editorial de la versión instalada o de la nueva versión disponible, acceso al registro completo con enlaces e imágenes y ruta Actualizaciones > Historial de actualizaciones > MegaCopier y versión. Las capturas quedan a la izquierda y las notas guardadas se pueden consultar sin Internet. Los registros históricos completos están también en inglés.

<img src="docs/capturas/actualizaciones.png" width="820" alt="Centro de actualizaciones de MegaCopier">

<img src="docs/capturas/historial-ruta.png" width="820" alt="Ruta del historial y registro completo de una versión">

- **Informes opcionales y privados:** elección en el instalador y en Ajustes > Avanzado, vista previa exacta del informe, registro local y envío automático sin preguntas cuando se autoriza. Si está desactivado, el diálogo permite enviar una vez o habilitar futuros envíos. Detecta cierres inesperados y los fallos nativos disponibles de esa sesión; un apagón también puede dejar una marca de cierre inesperado.
- **Receptor configurado en Cloudflare y GitHub privado:** trazas filtradas sin mensajes libres, nombres ni rutas, cola autorizada, reintentos limitados y agrupación de fallos repetidos. Desactivar el envío elimina los pendientes locales; no retira informes ya recibidos. La política explica los proveedores y límites de conservación.

<img src="docs/capturas/ajustes-informes.png" width="820" alt="Control de informes y accesos a registros en Ajustes">

- **Instalador reorganizado:** accesos al escritorio, menú Inicio y preparación de un acceso para anclar desde Windows, todos desmarcados por defecto. Integración reúne Explorador, inicio con Windows, búsqueda automática y los informes al final. La casilla de informes aparece seleccionada con explicación debajo; las actualizaciones silenciosas conservan la preferencia. Incluye imagen lateral propia y política de privacidad antes de instalar.

<img src="docs/capturas/instalador-informes.png" width="598" alt="Opciones de accesos directos e integración del instalador">

### Cambiado

- **Sonidos en cuatro tarjetas:** avisos con campana; volumen con icono, nombre, barra y porcentaje en una línea; personalización por evento; y silencio durante juegos, presentaciones o «No molestar», con interruptor activado por defecto en ajustes nuevos. Respeta las preferencias existentes.

<img src="docs/capturas/sonidos-tarjetas.png" width="820" alt="Avisos, volumen, personalización y silencio en tarjetas separadas">

- **Documentación en dos idiomas:** enlaces visibles a la presentación y al historial completo en inglés y español. Las publicaciones futuras muestran ambos idiomas y empiezan por las principales novedades; los assets de notas separados conservan el documento completo para la aplicación. La guía editorial permite resúmenes desarrollados de la longitud necesaria y agrupa arreglos menores sin inventar cifras.
- **Traducciones revisadas:** los 14 idiomas comparten las mismas claves y parámetros. El ejemplo de ruta de dispositivos rápidos procede de los recursos traducidos; los filtros en idiomas distintos del español y el inglés usan un ejemplo de extensión y tamaño que el analizador admite, sin una frase inglesa sin traducir.

### Arreglado

- **Arranque:** consultar la conexión del actualizador fuera del hilo de la interfaz evita el cierre de WinUI al ejecutar el EXE.
- **Sin Internet:** se conservan el último resultado conocido y las novedades. El aviso de red aparece al buscar o descargar manualmente y se oculta al salir de la sección. Volver a conectarse permite reintentar; no se consulta una versión en cada cambio de conexión si todavía no vence el plazo de 48 horas.
- **Acción al volver a Actualizaciones:** vuelve a ofrecer Buscar actualizaciones cuando no hay una versión nueva conocida. «Estás al día» aparece en el botón tras una búsqueda manual sin novedades; una versión disponible conserva Descargar o Instalar y sus principales cambios.
- **Navegación:** la selección lateral y el contenido se desplazan desde el primer cambio, incluso con pulsaciones rápidas. Volver y avanzar invierten la transición; funcionan también Alt+Izquierda/Derecha y los botones laterales del ratón. Ajustes en la barra superior regresa a las copias o al inicio y restaura el tamaño anterior.
- **Ayuda emergente:** el mensaje Atrás deja de aparecer al mover el ratón fuera del botón Volver, incluida la ventana sin foco.
- **Alineación:** cabecera y ruta del historial conservan su altura con el cuadro de búsqueda, dejan espacio para las letras con descendentes y alinean ópticamente los separadores. El texto y la flecha de cada tarjeta del historial quedan centrados juntos a la derecha.
- **Indicadores:** la notificación del botón Ajustes ya no se recorta con iconos personalizados y la del menú lateral queda alineada a la derecha. Las flechas giran durante búsqueda y descarga; los conjuntos de iconos conservan su estilo en el check verde, la atención amarilla y la X roja por búsqueda automática desactivada o error de conexión tras una acción.
- **Instalación y preferencias:** casilla y explicación de informes visibles sin cortes; interruptor primero en Ajustes y accesos a registros dispuestos horizontalmente. Las opciones de red también se muestran en una fila con separación.

### Seguridad

- **Actualizaciones verificadas:** manifiesto ECDSA P-256 y SHA-256 del instalador y del ZIP; sin clave o firma válida no se ejecuta el paquete. Clave pública de producción incluida en la app y privada custodiada fuera del repositorio y como secreto de GitHub Actions.
- **Firma de Windows pendiente:** solicitud enviada a SignPath Foundation el 21 de octubre de 2026, con recepción confirmada. La aprobación y el certificado Authenticode siguen pendientes; esta configuración no convierte los ejecutables actuales en firmados ni garantiza eliminar los avisos de SmartScreen.

## [1.7.1] - 2026-10-20

### Arreglado

- **Al abrir MegaCopier con una copia a una carpeta que ya existía**, con la vista completa, la copia acababa en error
  («El parámetro no es correcto») en lugar de preguntar qué hacer: la pregunta llegaba antes de que la ventana
  estuviera lista. Ahora espera a la ventana y pregunta.
- **«El archivo ya existe»**: la miniatura de la derecha salía recortada por el borde derecho.

## [1.7.0] - 2026-10-20

### Añadido

- **La ventana ya no se apaga al usar otra**: con el fondo de serie (el del tema, en los temas Fluent) es Mica y, mientras
  usas otra ventana, pasa a Mica Alt en lugar de quedarse de un color liso. Al volver, Mica otra vez. Con otro fondo
  elegido en Ajustes se comporta como las ventanas de Windows.
- En el menú de la bandeja, «Iniciar con Windows» lleva su icono, con el conjunto de iconos elegido.

### Cambiado

- **Continuar un archivo grande sigue escribiendo al momento**: antes, antes de seguir, se releía del origen todo lo ya
  copiado para calcular la suma del archivo entero, y con varios GB en una memoria USB parecía que la copia volvía a
  empezar (no lo hacía). Ahora la suma solo se calcula si algo la usa (verificar, el archivo de sumas, guardar la suma en
  el archivo o el modo forense), y a la vez que se copia el resto. Sin nada de eso, la lista no muestra la suma de ese
  archivo, igual que con los archivos clonados.
- **«El archivo ya existe» suena solo una vez por copia**: con «Uno a uno», cada archivo repetido volvía a sonar.
- **«El archivo ya existe» tiene siempre el mismo tamaño**, algo más ancho que antes: ya no crece ni encoge con la ruta,
  con que el archivo tenga miniatura o con que sean iguales.
- **Fondo de la ventana** (Ajustes › Interfaz): el del tema, Mica, Mica Alt, Acrílico y Translúcida (el acrílico más
  claro y transparente). Desaparece la opción repetida: la antigua «Translúcida» era el mismo acrílico.
- **Menús, listas de opciones y diálogos de acrílico**, con cualquier tema y fondo de ventana: los menús (Nueva, Al
  terminar, «…», clic derecho) siguen de acrílico aunque la ventana no tenga el foco; las listas desplegables (todas: las
  de Ajustes, las de los diálogos, el filtro de la lista de archivos) dejan ver, difuminado, lo que hay detrás, como los
  menús; y los diálogos (como «ya existe en el destino» o «El archivo ya existe») difuminan la ventana que tapan, en vez
  de verse de un gris liso con la zona de los botones de otro tono. Con la transparencia de Windows desactivada o sin
  acrílico, se ven de un color liso del tema.
- El menú de la bandeja es de acrílico como los demás menús, sea cual sea el fondo de la ventana, y «Acerca de» y la
  bandeja de arrastre, también de acrílico.
- **Abrir Ajustes desde la ventana pequeña** la hace crecer animada, como al encoger, sin el recuadro negro; también al
  pasar sola a la vista completa con una segunda copia. «Más» crece sin dar un salto al principio.
- **La vista con la lista de archivos («Más») se abre de serie con el tamaño justo** (760 × 660); puedes agrandarla. La
  vista con la lista de copias sigue igual.
- **En la ventana pequeña, al pasar de una copia a otra**, la tarjeta se desliza hacia un lado u otro según el botón.
- **El disco de destino va a la derecha** de su fila (el de origen, a la izquierda): el nombre de lo que se copia tiene
  más sitio, y si el destino es largo se acerca al centro.
- **Las cifras ya no bailan**: archivos, tamaños, velocidad y tiempo restante guardan su sitio y se alinean a la
  derecha, con todos los números del mismo ancho.
- El botón Pausar / Reanudar es del color de los demás botones.
- **«Al pulsar la X» y «Al minimizar» van en tarjetas separadas**, cada una con su ayuda (i).
- El idioma, las unidades de tamaño y el icono y el progreso en la bandeja pasan a Ajustes › General.

### Arreglado

- **Pegar desde la barra superior del Explorador** usa MegaCopier y la carpeta de la pestaña activa, aunque haya una
  subcarpeta seleccionada. Comparte el ajuste de interceptar el pegado con Ctrl+V y el menú contextual.
- **Un archivo individual que ya existe vuelve a preguntar**, también si tiene el mismo tamaño y fecha. Ofrece
  Reemplazar, Conservar ambos y Omitir, sin opciones para otros archivos. Reanudar conserva la detección de lo ya copiado.
- **Arrastrar entre ventanas con copias en marcha**: Esc permanece pulsada mientras se confirma que el Explorador
  canceló su arrastre, y su bucle de entrada sigue recibiendo actividad durante esa espera.
- **Pegar con clic derecho en el Explorador** usa también MegaCopier: se reconoce la orden de los menús moderno y
  clásico, se resuelve el destino y se cierra el menú antes de iniciar la copia. Los casos no reconocidos conservan
  el comportamiento del Explorador.
- **Arrastres rápidos del Explorador**: podían pasar al copiador de Windows porque al soltar el ratón Windows aún no
  había anunciado el inicio del arrastre. Ahora se comprueba esa transición brevemente, sin convertir la selección
  con recuadro en una copia. También se admiten los gestos de ratones remotos y herramientas de accesibilidad.
- **Soltar en unidades del panel lateral** funciona también cuando el Explorador antepone una raíz como
  «Escritorio (OneDrive)» a «Este equipo».
- **Pegados con la interfaz ocupada**: una petición ya encolada no vuelve a Windows por superar cinco segundos de
  espera, evitando que ambos copiadores puedan ejecutar la misma operación.
- **La velocidad y el tiempo restante con límite de velocidad**: en `mcopy`, con el límite puesto, la velocidad salía al
  doble de la real y el tiempo restante a la mitad.
- **La ventana vuelve a su tamaño al quitarse la lista de copias**: al elegir «Añadir a esta copia» o al quedar una sola
  copia, se quedaba grande. Ahora vuelve al tamaño de antes (el de serie o el que le diste), salvo que la cambiaras
  mientras se veía la lista. Con «Copia aparte» sigue mostrando la lista, como antes.
- **Con «Uno a uno» en la ventana pequeña**, tras pulsar «Reemplazar» (u otra opción) el siguiente «El archivo ya existe»
  podía salir descolocado, arriba y fuera de la vista: la ventana encogía y volvía a crecer entre un archivo y otro.
- **MegaCopier se cerraba de golpe** a veces al pulsar «Nueva» o al cerrar el menú de la bandeja, sobre todo tras
  cambiar de tema (por ejemplo, de «Fluent oscuro» a «Azul clásico» tras haber abierto «Nueva»). Al cambiar el acento,
  MegaCopier recargaba los recursos de los controles de Windows y los menús ya abiertos antes quedaban rotos; ahora el
  acento se aplica sin recargarlos. También se ha quitado la sonda de fallos de WinUI añadida en la 1.3
  (`crashstack.log`).
- **«Fluent claro» con Windows en modo oscuro** se veía con el fondo oscuro y los textos sin contraste: ahora la ventana
  es clara de verdad.
- **Con el alto contraste de Windows**, los colores de los temas propios ya no tapan los del sistema en botones, listas,
  menús y diálogos.
- Los iconos de la lista de secciones de Ajustes y de los menús a veces se quedaban con los del conjunto anterior hasta
  cambiar de tema.
- **Ctrl+V a veces abría MegaCopier diciendo «No hay copias»** con la copia en marcha (se veía su progreso en la barra
  de tareas): la copia se quedaba en una ventana que se estaba cerrando.
- La pregunta de «¿añadirla a la copia en marcha?» ya no agranda la ventana pequeña: sale en ella, del tamaño que tenga.
- **Arrastrar con copias en marcha** podía acabar en el copiador de Windows: Windows quita sin avisar el gancho del ratón
  (y el del teclado, el de Ctrl+V) si una vez tarda en responder, por ejemplo en una pausa de memoria con copias grandes.
  MegaCopier ahora se da cuenta y lo vuelve a poner (queda anotado en `drag.log`), y evita esas pausas largas.

## [1.6.0] - 2026-10-19

Una versión para que todo se vea y funcione como esperas. Puedes tener cada copia en su propia ventana; los iconos del
conjunto que elijas llegan por fin a todas partes (menús, pestañas, bandeja); el menú de la bandeja es de WinUI; y la
ventana puede llevar fondo Mica, Mica Alt o acrílico. Además, «Uno a uno» y «Sobrescribir» hacen por fin lo que dicen,
el aviso de «ya existe» enseña las fotos en miniatura y el arrastre en el Explorador ya no deja el botón «pegado».

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.6.0/docs/capturas/ventanas-independientes.png" width="520" alt="Dos ventanas de MegaCopier, una por copia, cada una con su progreso">

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.6.0/docs/capturas/archivo-existe.png" width="420" alt="El archivo ya existe: las dos fotos en miniatura, con la nota «Son iguales: mismo tamaño y fecha»">

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.6.0/docs/capturas/menus-iconos.png" width="420" alt="El menú «Nueva» con los iconos del conjunto macOS"> <img src="https://github.com/cbatista96/MegaCopier/raw/v1.6.0/docs/capturas/menu-bandeja.png" width="260" alt="El menú de la bandeja con el aspecto de WinUI 3 y sus iconos">

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.6.0/docs/capturas/iconos-bandeja-temas.png" width="520" alt="Iconos de diez conjuntos y, a la derecha, sus iconos de progreso de la bandeja: en marcha, en pausa, con errores y terminada">

### Añadido

- **Cada copia en su propia ventana** (Ajustes › Comportamiento): una ventana por copia, que empieza ya sin esperar a
  las demás. Es la misma aplicación: el tema, los iconos y los ajustes son los mismos en todas. Cada ventana tiene su
  icono de progreso en la bandeja y su botón en la barra de tareas. Si la copia nueva va al mismo disco que otra,
  «Copia nueva al mismo disco» decide: preguntar (añadirla a la copia de esa ventana o abrirla **en una ventana
  nueva**), añadirla siempre o abrir siempre una ventana nueva. Está desactivado de serie.
- **Fondo de la ventana** (Ajustes › Interfaz): el del tema (como hasta ahora), translúcida, **Mica**, **Mica Alt**,
  **Acrílico de escritorio (base)** o **Acrílico de escritorio (delgado)**, con cualquier tema. Sustituye a «Ventana
  translúcida» (quien la tenía activada sigue igual). El editor de temas también ofrece el acrílico delgado.
- **Menú de la bandeja de WinUI**: el clic derecho en el icono de MegaCopier, y en el de cada copia, abre un menú con el
  aspecto del resto del programa (esquinas redondeadas, tema claro u oscuro) y con iconos.
- En el aviso de «el archivo ya existe», una nota cuando los dos archivos son iguales (mismo tamaño y fecha).

### Cambiado

- **El icono de Pausar se transforma en el de Reanudar** (las dos barras se juntan en el triángulo, y al revés), con
  el color y el fondo del conjunto de iconos. El texto cambia sin moverse: ya no se desliza ni se monta uno encima del
  otro.
- **Todos los iconos siguen el conjunto elegido**: también los de los menús («Nueva», «Al terminar», «…», el clic
  derecho en la lista de copias), las pestañas (Archivos, Destino, Opciones, Estado, Registro), el filtro y las
  casillas de la lista de archivos, el círculo de progreso del archivo en curso y el menú de la bandeja.
- **Los iconos de progreso de la bandeja siguen el conjunto de iconos**: su forma (redondeada, círculo, cuadrada…),
  sus colores y su acabado (fondo de color, brillo, relieve, contorno o neón). **En pausa muestran el símbolo de pausa**
  en lugar del porcentaje.
- Los iconos de MegaCopier se marcan en Windows como «a la vista» para que salgan junto al reloj y no tras la flecha
  «˄»: el principal la primera vez (después manda lo que elijas) y los del progreso de cada copia siempre. Windows
  aplica este ajuste al iniciar sesión, así que se nota desde la próxima vez que entres en Windows.
- El aviso de «el archivo ya existe» enseña la miniatura de las fotos y los vídeos, y en lo demás (PDF, música,
  documentos…) el icono del programa que los abre, como el Explorador.
- **Ventana pequeña**: los archivos copiados y los tamaños se ven siempre (antes se ocultaban al estrecharla), y «Al
  terminar» conserva su texto mientras quepa. Al terminar una copia, su resumen (archivos, tamaño, tiempo y velocidad)
  va junto a «Abrir carpeta», así la tarjeta no crece ni tapa el paso entre copias.
- **«Acerca de MegaCopier» es una ventana propia**, pequeña y con fondo Mica, en lugar de un diálogo apretado dentro de
  la ventana pequeña.
- El aviso rojo con las copias activas va en la esquina del botón de la lista: se ve igual con cualquier conjunto de
  iconos.
- La lista de copias se abre y se cierra deslizándose sin que el texto parpadee.

### Arreglado

- **«Uno a uno» no preguntaba nada** y **«Sobrescribir» se saltaba los archivos** que ya estaban copiados (mismo
  tamaño y fecha). Ahora, si lo eliges en el aviso de «ya existe en el destino», «Sobrescribir» vuelve a copiarlos todos
  y «Uno a uno» pregunta por cada archivo, también por los iguales. «Pasar» sigue dejando los que ya están.
- En la ventana pequeña, el aviso de cada archivo salía cortado: ahora la ventana crece lo necesario.
- **Arrastrar en el Explorador**: al soltar, a veces no pasaba nada hasta el siguiente clic y entonces copiaba el
  copiador de Windows; y soltar sobre una unidad o carpeta del panel izquierdo de la misma ventana nunca lo hacía
  MegaCopier. Mientras el Explorador arrastra, solo atiende a otros programas si el ratón se mueve; MegaCopier ahora lo
  mantiene despierto mientras decide (sin mover el puntero), decide cada arrastre por separado y, si decidir tarda casi
  dos segundos, deja que suelte el Explorador: el botón nunca se queda «pegado». Cada arrastre queda anotado en
  `drag.log` (en la carpeta de datos) con lo que se hizo y cuánto tardó.
- Abrir Ajustes desde la ventana pequeña (o pasar sola a la vista completa) ya no deja ver un recuadro negro mientras
  la ventana crece.
- Con «Copia nueva al mismo disco» en «Añadirla a la copia en marcha», lo nuevo iba a la carpeta de la otra copia en
  lugar de a la suya.

### Quitado

- La opción «Ventana translúcida»: ahora es una de las de «Fondo de la ventana».

## [1.5.0] - 2026-10-19

Una versión para copiar varias cosas a la vez sin pensarlo: si lo nuevo va al mismo disco que una copia en marcha,
MegaCopier pregunta si sumarlo a ella, y si va a otro disco empieza a la vez. Cada copia tiene su propio icono de
progreso en la bandeja, la X cancela de verdad, el arrastre también vale sobre el panel izquierdo del Explorador, las
opciones difíciles se explican con ejemplos, los paquetes de iconos se parecen por fin a su sistema (con siete nuevos) y
la ventana cabe entera en pantallas de 1366 × 768.

<img src="https://github.com/cbatista96/MegaCopier/raw/v1.5.0/docs/capturas/misma-unidad.png" width="520" alt="Ya hay una copia hacia N: ¿Añadir «Fotos» al final de esa copia? Con Añadir a esta copia, Copia aparte y Cancelar">

### Añadido

- **Copias nuevas al mismo disco**: si copias algo a un disco que ya está recibiendo otra copia, MegaCopier sale al
  frente y pregunta, con cada opción explicada: *Añadir a esta copia* (se copia al final de su lista, cada cosa a su
  carpeta, comprobando antes el espacio), *Copia aparte* (espera a que termine la otra; *Comenzar* la arranca ya) o
  *Cancelar*. Si va a **otro disco**, empieza a la vez sin esperar, porque cada disco copia a su velocidad. Las dos
  cosas se cambian en Ajustes › Comportamiento.

  <img src="https://github.com/cbatista96/MegaCopier/raw/v1.5.0/docs/capturas/varias-copias.png" width="620" alt="Dos copias: la primera copiando y la segunda, aparte, en cola con el botón Comenzar; a la izquierda la lista compacta de copias">

- **Iconos de progreso en la bandeja**: uno por cada copia en marcha, con su porcentaje y una barra que sube con los
  colores del tema (ámbar en pausa, rojo con errores). Al terminar se llena, muestra una marca y se quita solo. Un clic
  abre esa copia; el clic derecho la pausa, la reanuda o la detiene. Se activa o desactiva en Ajustes › Interfaz.

  <img src="https://github.com/cbatista96/MegaCopier/raw/v1.5.0/docs/capturas/bandeja-progreso.png" width="400" alt="Iconos de progreso ampliados: 0, 7, 45, 73 en pausa, 99, terminada y 50 con errores">

- **Qué hacen la X y el botón de minimizar**, en Ajustes › Interfaz: la X cancela las copias (como *Detener*), las
  detiene para continuarlas después, solo oculta la ventana o sale de MegaCopier; minimizar la deja en la barra de
  tareas o la esconde en la bandeja.
- **Número de copias activas** en rojo sobre el botón de la lista de copias cuando la lista está cerrada.
- **Clic derecho en la lista de copias**: pausar, reanudar, detener, *Archivar en el historial* (también con Supr),
  *Eliminar* (también del historial) y abrir la carpeta de destino.
- **Ayudas ⓘ con ejemplos** en las opciones más difíciles de cada copia: qué se copia, cómo se copia, al terminar,
  comandos al terminar (con sus variables), filtros, reglas y sincronización y seguridad.

  <img src="https://github.com/cbatista96/MegaCopier/raw/v1.5.0/docs/capturas/ayuda-opciones.png" width="520" alt="Ayuda de Comandos al terminar con sus variables y ejemplos">

- **Iconos en el menú *Al terminar*** (suspender, hibernar, apagar, cerrar sesión, cerrar MegaCopier).
- **Pantallas pequeñas**: en pantallas bajas, como 1366 × 768, la ventana se ve igual pero algo más pequeña, para caber
  entera. En pantallas grandes no cambia nada. Se puede desactivar en Ajustes › Interfaz.
- **Paquetes de iconos nuevos**: Material You (con el color del tema), One UI, Nothing OS, Windows 95 (con relieve),
  Windows Vista (cristal oscuro), Ciberpunk y Holográfico.
- *Acerca de*: «Desarrollado por WML», con enlace a su web.

### Cambiado

- **Arrastrar desde el Explorador** también funciona al soltar sobre las carpetas y unidades del panel de la izquierda
  (*Este equipo*, *Disco local (C:)*, *Descargas*…), sobre las unidades de *Este equipo* y desde resultados de búsqueda
  o *Inicio*. Arrastrar una carpeta desde el panel de la izquierda también lo hace MegaCopier.
- **La X de la ventana cancela las copias** (como *Detener* en cada una), en lugar de dejarlas copiando a escondidas.
  Se puede cambiar en Ajustes.
- El botón **Pausa** ahora dice **Pausar** y **Continuar** pasa a **Reanudar**; *Empezar ya* pasa a **Comenzar**. El
  icono cambia deslizándose, sin deformarse.
- **La barra de herramientas** (*Nueva*, pausar y reanudar todas, historial, ajustes…) se ve también en la ventana
  pequeña.
- **Ajustes agranda la ventana** hasta el ancho de sus tarjetas y, al volver, la ventana recupera el tamaño que tenía
  (también la vista pequeña).
- **Lista de copias más compacta**: arriba solo lo que hace («Copiando (29,7 %)», o cuándo terminó) y debajo el origen y
  el destino, con el número de archivos y el tamaño. Se abre y se cierra deslizándose. Las copias terminadas se ven
  mientras quede alguna activa; al acabar todas, pasan al historial.
- **Paquetes de iconos rehechos** para que cada uno se parezca a su sistema: GNOME con círculos azules, Linux Mint en
  verdes, Fedora con aros azules, Arch con mosaicos planos, Steam sobre su azul oscuro, Android con círculos planos y
  Windows 8 plano. Ningún paquete deja ya un hueco: si su fuente no tiene un icono, se usa el de Windows 11.

  <img src="https://github.com/cbatista96/MegaCopier/raw/v1.5.0/docs/capturas/iconos-sistemas.png" width="620" alt="Galería de paquetes de iconos: Material You, One UI, Nothing OS, macOS, GNOME, Linux Mint, Fedora, Arch, Windows 95, Vista, Steam, Ciberpunk y más">

- El lema de *Acerca de*: «el copiador definitivo».

### Arreglado

- El icono del botón Pausar/Reanudar se veía borroso y deformado tras pulsarlo.
- Al pasar el ratón por los colores de las barras (Ajustes › Interfaz) se volvían negros; ahora se rodean de un aro.
- El botón *Volver* de las páginas se anunciaba como «Page_Back» a los lectores de pantalla.

### Quitado

- El texto de versión al pie de Ajustes › Avanzado (sigue en *Acerca de*).
- «Añadir las copias nuevas a una que ya está en marcha»: lo sustituyen las opciones de copias nuevas al mismo disco y a
  otro disco.

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

[Sin publicar]: https://github.com/cbatista96/MegaCopier/compare/v1.7.1...HEAD
[1.7.1]: https://github.com/cbatista96/MegaCopier/compare/v1.7.0...v1.7.1
[1.7.0]: https://github.com/cbatista96/MegaCopier/compare/v1.6.0...v1.7.0
[1.6.0]: https://github.com/cbatista96/MegaCopier/compare/v1.5.0...v1.6.0
[1.5.0]: https://github.com/cbatista96/MegaCopier/compare/v1.4.0...v1.5.0
[1.4.0]: https://github.com/cbatista96/MegaCopier/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/cbatista96/MegaCopier/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/cbatista96/MegaCopier/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/cbatista96/MegaCopier/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/cbatista96/MegaCopier/releases/tag/v1.0.0
