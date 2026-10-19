# Características de MegaCopier

Todo lo que hace MegaCopier, por temas. Para usarlo, la [guía](Guia.md); para compararlo con otros copiadores, la
[comparativa](Comparativa-copiadores.md); y si tienes dudas, las [preguntas frecuentes](Preguntas-frecuentes.md).

## Copia que no se pierde

- **Reanudación tras un apagón** dentro del propio archivo: se copia a un `.mcpart` por bloques; cada bloque se vacía al
  disco y solo después se anota con su suma en un diario (`.mcjournal`). Al volver, se comprueba lo ya copiado y se sigue.
  Como máximo se pierde un bloque.
- Las copias interrumpidas aparecen al arrancar y se continúan con un clic, o solas.
- **Pausar, saltar el archivo actual, detener y seguir más tarde**; reintentos automáticos y lista de fallidos.
- **Espera** a que vuelva el disco o la red, o a que haya espacio, en vez de fallar.
- **Comprueba que cabe** antes de copiar (y al añadir a una copia en marcha): si no, cancelar o forzar la copia, que
  llena el disco y espera a que haya sitio.
- Carpetas **«todo o nada»**: o se copia entera o no queda nada a medias.
- **Modo rescate** para discos dañados: salva lo legible y deja en ceros lo ilegible.
- **Archivos en uso** (Outlook, bases de datos) copiados desde una instantánea de Windows (VSS).
- **Modo copia de seguridad**: salta los permisos, con permiso de administrador.
- **Archivos cifrados con EFS** copiados tal cual, sin descifrarlos.

## Comprobación y protección

- **Verificación** releyendo el destino sin caché, con **reparación** de los bloques dañados.
- 11 algoritmos de suma: CRC32, xxHash3/64/128, MD5, SHA-1, SHA-256/512, SHA3-256/512 y BLAKE3.
- Crear y comprobar archivos de sumas (`.sha256`, `.md5`, `.sfv`, `.b3`, `.xxh3`…).
- **Guardián de integridad**: guarda la suma dentro de cada archivo y revisa las carpetas cada cierto tiempo contra la
  corrupción silenciosa del disco.
- **Escudo contra ransomware**: si muchos archivos que van a sobrescribir una copia buena parecen cifrados de repente, la
  copia se detiene antes de estropear también la copia buena.
- **Datos de recuperación** (paridad Reed-Solomon, como PAR2): si con el tiempo el disco o el USB estropea bloques de la
  copia, MegaCopier los reconstruye sin necesitar el original (`mcopy repair`).
- **Modo forense**: manifiesto con la SHA-256 de cada archivo (origen y copia), quién y cuándo, firmado con ECDSA.
  Cualquiera puede comprobarlo después con `mcopy verify-manifest`.

## Velocidad

- E/S directa al disco, asíncrona y en paralelo, con colas por disco físico.
- Archivos pequeños en paralelo e IoRing para leer muchos a la vez.
- **Autoajuste por disco**: mide cada disco y elige el tamaño de bloque y las colas.
- **Clonado instantáneo** en ReFS y unidades de desarrollo de Windows 11.
- **Copia en el propio servidor** y compresión SMB en red.
- **Copia delta**: al reemplazar archivos grandes solo se escriben los bloques que cambiaron, y se puede deshacer.
- **Varios orígenes a la vez**: si el mismo contenido está en dos NAS o dos PC, los archivos grandes se leen de todos.
- Hasta 255 conexiones con servidores y nubes, **ajustadas solas** según la velocidad.
- ZIP y 7z **comprimidos en paralelo** en varios núcleos.
- Exclusión opcional de Windows Defender (con permiso de administrador).
- Límite de velocidad, prioridad baja, pausa con batería o con juegos a pantalla completa, horario.

## Qué y cómo copiar

- Modos: copiar, mover, **espejo**, solo actualizar, solo carpetas y **organizar fotos por fecha** de la cámara.
- **Simulación** («qué pasaría») y después «hacerlo de verdad».
- **Filtros** por nombre, tamaño, fecha y atributos, también **en lenguaje normal** («fotos de más de 2 MB del último mes
  sin los .tmp»), y **fotos y capturas por el texto que contienen** («con el texto "factura"», con el OCR de Windows,
  sin internet).
- **Conflictos**: preguntar (con miniaturas), sobrescribir, saltar, el más reciente o renombrar con un patrón.
- Omitir **fotos duplicadas** (mismo contenido, cualquier nombre).
- Permisos, propietario, flujos alternativos, atributos, fechas, enlaces simbólicos, uniones, enlaces duros, archivos
  dispersos y rutas de más de 260 caracteres.
- **Carpeta de versiones** para lo que se sobrescribe o se quita, con **limpieza automática** (días, semanas y meses).
- **Copias de seguridad sin duplicados**: instantáneas por trozos de contenido; diez copias de algo que apenas cambia
  ocupan poco más que una, y se puede restaurar cualquiera (`mcopy backup`, `snapshots`, `restore`).
- **Cifrado**: copia cifrada con AES-256 (`.mcenc`), a **ZIP cifrado** (AES de WinZip, que abren 7-Zip y WinRAR) o a
  **7z cifrado**, que oculta también los nombres de los archivos.
- **Borrado seguro**: una pasada de ceros, aleatoria, DoD de 3 pasadas o DoD de 7 pasadas.

## Comportamiento y errores
- **Si un archivo sigue fallando, preguntar qué hacer**: reintentar, omitir, omitir todos los siguientes o detener (o
  decidirlo de antemano: omitir, detener o reintentar al final).
- **Añadir las copias nuevas a una que ya está en marcha**: nunca, si van a la misma carpeta, si usan los mismos discos o
  siempre, preguntando antes o no.
- **Informe de errores** automático al terminar con problemas, en la carpeta de destino o en una fija.
- **Expulsar la unidad de destino** (USB, tarjeta) al terminar, también a la fuerza.
- **Al detener una copia, borrar lo que quedó a medias** (salvo si paró por un error).
- Atributos y permisos que se conservan **al mover**, aparte de los de copiar.
- Icono de la bandeja opcional, empezar las copias minimizadas, recordar la posición y el tamaño de la ventana y
  ventana translúcida.

## Sincronizar
- **Espejo** de una carpeta en otra.
- **Sincronización en los dos sentidos**, que recuerda el estado anterior, detecta borrados y **renombrados o movidos**
  (los renombra en el otro lado sin volver a copiarlos); lo sustituido va a la Papelera.
- **Vista de comparación** lado a lado antes de sincronizar, eligiendo qué se hace.
- **Sincronización en tiempo real**: vigila una carpeta y copia cada cambio al momento.

## Dónde copiar

- Discos, USB y carpetas de red.
- **Móviles y cámaras** por USB (MTP), en los dos sentidos.
- **LocalSend** (la app gratuita de iPhone y Android): recibir del móvil y **enviar al móvil**.
- Otro PC con MegaCopier por la red local, cifrado y con código.
- **Servidores**: FTP, FTPS, SFTP, WebDAV (Nextcloud, ownCloud, NAS…) y S3 y compatibles (AWS, Backblaze B2, Cloudflare R2,
  Wasabi, MinIO).
- **Nubes**: OneDrive, Google Drive y Dropbox, con inicio de sesión en el navegador ([cómo conectarlas](Nube.md)).
  Las contraseñas y los tokens se guardan en el Administrador de credenciales de Windows.

## Uso diario

- Sustituye **Ctrl+V**, el **menú contextual** (también el corto de Windows 11) y el **arrastre** con botón izquierdo y
  derecho del Explorador. Un doble clic en la bandeja lo activa o desactiva.
- **Varios destinos a la vez**, favoritos, recientes y las carpetas abiertas en el Explorador, Total Commander y Directory Opus.
- **Cola por disco**: en el mismo disco, de una en una; en discos distintos, a la vez. Reordenar arrastrando.
- **Línea de tiempo** de la cola y **bandeja de arrastre** para ir juntando archivos.
- **Tareas programadas** (una vez, diarias, semanales o mensuales) que se hacen al encender el PC si estaba apagado.
- **Historial** con deshacer y «copiar de vuelta».
- **Aviso SMART** de discos que empiezan a fallar.
- Avisos al terminar por **correo** y **webhook**; al terminar, apagar, suspender, hibernar o ejecutar un programa.
- **Control desde el móvil**: página web en la red local, con código, para ver, pausar, seguir o detener copias.
- **Asistentes de IA**: servidor MCP (`mcopy mcp`) para que Claude, Copilot y otros pidan copias y copias de seguridad
  ([cómo configurarlo](Integraciones.md)).
- Informes HTML y CSV, listas de copia guardadas y registros.
- **Consola** `mcopy` con todas las funciones y **modo portátil** (ZIP, sin instalar).
- **14 idiomas**: español, inglés, francés, alemán, italiano, portugués de Brasil, ruso, ucraniano, polaco, neerlandés,
  turco, chino, japonés y coreano.
- **73 temas** que colorean toda la interfaz (hasta las barras de desplazamiento), con degradados y texturas (brasas, madera, papel, fibra de carbono, aurora, galaxia, metal, ondas, neón y lluvia digital), y **23 conjuntos de iconos** (Fluent, Color, Mosaico, Suave, Degradado, Neón, Windows 10 y otros inspirados en Android, macOS, Ubuntu, KDE, GNOME, Windows XP, 7 y 8, Steam…) que se combinan con cualquier tema; **22 estilos de barra de progreso** (Aero, Ola, Neón, Fuego, LED, Terminal…) con colores, grosor, redondeo, velocidad, brillo y efectos según el estado, distintos para cada barra; editor de colores y modo claro u oscuro; accesible con lectores de pantalla.
- **Ajustes como la Configuración de Windows 11**: secciones a la izquierda con sus iconos, buscador de ajustes y animaciones.
- **Sonidos propios** (campanillas originales) al terminar, en los errores y cuando hace falta decidir algo, activados por defecto; se pueden cambiar por los de Windows o por un archivo tuyo.
- Sin telemetría: no envía nada a ningún sitio salvo lo que configures tú.
