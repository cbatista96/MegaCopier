# MegaCopier frente a los demás copiadores

Investigación hecha el 16 de octubre de 2026 y ampliada el 24 de octubre de 2026 con el historial completo de TeraCopy
(de la 1.2 de 2007 a la 4.0.5 de 2026), las opciones de Ultracopier y SuperCopier, y una búsqueda nueva de copiadores,
programas de sincronización y tecnologías de copia. Fuentes al final. Lo que tiene MegaCopier está comprobado en su código
y en sus pruebas automáticas, no de memoria.

Programas estudiados: **TeraCopy 4**, **FastCopy 5.12**, **Ultracopier 3**, **SuperCopier**, **Robocopy** (viene con
Windows), **el Explorador de Windows 11**, **FreeFileSync**, **GS RichCopy 360**, **SyncBackPro**, **GoodSync**,
**Bvckup 2**, **Copy Handler**, **ExtremeCopy**, **Unstoppable Copier**, **RichCopy** (de Microsoft, abandonado en 2009),
**Total Commander**, **Directory Opus**, **XYplorer**, **Files**, **rclone**, **Syncthing**, **Resilio Sync** y los de
transferencia acelerada por UDP (**Aspera**, **Signiant**, **FileCatalyst**, **Raysync**).

---

## 1. Lo que está hecho pero no se ha podido probar en real

Todo está programado y probado con pruebas automáticas (servidores, móviles y nubes de prueba en el propio equipo). Lo de
esta tabla, además, necesita un equipo, una cuenta o un permiso que el PC de desarrollo no tiene. Si algo falla, MegaCopier
avisa o copia de la forma normal: nunca se pierde la copia por culpa de ellas.

| Función | Qué hace, en fácil | Qué hace falta para probarla |
|---|---|---|
| **Clonado en ReFS** | En discos ReFS (las "unidades de desarrollo" de Windows 11), copiar dentro del mismo disco es instantáneo. | Crear una unidad de desarrollo (pide permiso de administrador). |
| **VSS** (archivos en uso) | Copia archivos que otro programa tiene abiertos (Outlook, bases de datos) desde una "foto" del disco. | Aceptar el aviso de Windows "¿Quieres permitir…?". |
| **Modo copia de seguridad** | Copia carpetas protegidas saltándose los permisos. | Aceptar el aviso de administrador. |
| **Exclusión de Windows Defender** | Defender deja de analizar lo que escribe MegaCopier (más rápido con muchos archivos). | Aceptar el aviso de administrador. |
| **SMB** (red) | Copia en el propio servidor (ODX) y compresión en redes lentas. | Una carpeta compartida en otro PC o un NAS. |
| **Menú corto de Windows 11** | "Pegar con MegaCopier" sin "Mostrar más opciones". | Instalar MegaCopier con el instalador (registra el paquete). |
| **Arrastre con botón izquierdo** | MegaCopier copia lo que arrastras entre ventanas del Explorador. | Arrastrar de verdad con el ratón. |
| **Móviles y cámaras (MTP)** | Copiar a o desde el móvil por USB. | Un móvil conectado. |
| **SFTP** | Servidores SSH. | Un servidor SFTP real (se probó con el protocolo, no con un servidor). |
| **OneDrive, Google Drive y Dropbox** | Copiar a y desde la nube con inicio de sesión en el navegador. | Registrar la app en cada servicio (ver `docs/Nube.md`) y una cuenta. |
| **EFS** | Copiar archivos cifrados con EFS sin descifrarlos. | Una cuenta de Windows que ya use EFS (crear la clave tocaría el almacén de certificados). |
| **Total Commander y Directory Opus** | Sus carpetas abiertas como destinos. | Tener esos programas instalados. |
| **Control desde el móvil y LocalSend** | Ver y controlar copias desde el navegador del móvil; enviar al móvil con LocalSend. | Un móvil en la misma red (las pruebas usan solo este equipo para no abrir el cortafuegos). |

---

## 2. Lo que ya tiene MegaCopier

✅ = lo tiene. Entre paréntesis, quién más lo tiene.

### Copia y fiabilidad
- ✅ **Reanudar tras un apagón** desde el último bloque verificado, también dentro de un mismo archivo enorme. *(Nadie más igual; Robocopy /Z reanuda, pero sin verificar)*
- ✅ Pausa, continuar, saltar el archivo actual, parar y reanudar más tarde *(TeraCopy, Ultracopier, Copy Handler, ExtremeCopy)*
- ✅ Reintentos automáticos (cuántos y cada cuánto) y lista de fallidos para reintentar solo esos *(TeraCopy, Robocopy)*
- ✅ Ante un error: preguntar, omitir, detener o **dejarlo para el final** *(Ultracopier)*
- ✅ Esperar a que vuelva el disco o la red, o a que haya espacio libre *(TeraCopy; Robocopy /LFSM)*
- ✅ Comprobar antes de empezar que cabe todo, y al añadir a una copia en marcha *(TeraCopy, Ultracopier)*
- ✅ Aviso antes de empezar si un archivo de más de 4 GB no cabe en FAT32 *(TeraCopy)*
- ✅ No dejar copiar una carpeta dentro de sí misma *(TeraCopy)*
- ✅ **Copiar una unidad entera** (`E:\` completa) a una carpeta con su nombre, sin la Papelera ni «System Volume Information»; mover o borrar una unidad, nunca *(TeraCopy, FastCopy, Robocopy)*
- ✅ **Nombres que el destino no admite, arreglados solos**: sin espacios ni puntos al final, sin caracteres prohibidos ni nombres reservados (CON, NUL…), recortados si son largos para el disco (255 bytes en red) *(TeraCopy 4)*
- ✅ Al cancelar, borrar lo que quedó a medias (salvo si paró por un error) *(TeraCopy, Ultracopier)*
- ✅ Retomar solas las copias interrumpidas al arrancar *(Copy Handler)*
- ✅ Carpetas "todo o nada" (o se copia entera o no queda nada a medias)
- ✅ Escribir en un archivo temporal y cambiarle el nombre al final, para no dejar nunca un archivo roto con el nombre bueno *(XYplorer "Safe Overwrite")*
- ✅ Reservar el tamaño del archivo antes de escribirlo, para que no quede troceado en el disco *(TeraCopy, FastCopy)*
- ✅ Modo rescate para discos dañados *(Unstoppable Copier)*
- ✅ Copiar archivos en uso con instantáneas VSS *(TeraCopy 4, GS RichCopy, SyncBackPro, Bvckup 2, GoodSync, FreeFileSync)*
- ✅ Pedir permiso de administrador solo cuando hace falta, en un único paso para carpetas protegidas y archivos en uso *(TeraCopy, GoodSync)*
- ✅ Copia delta (solo lo que cambió) con posibilidad de deshacer *(Bvckup 2, GoodSync, SyncBackPro)*
- ✅ Modo copia de seguridad, saltándose permisos, con permiso de administrador *(Robocopy /B)*
- ✅ Archivos cifrados con EFS copiados tal cual *(Robocopy /EFSRAW)*
- ✅ Evitar que el PC se suspenda mientras copia *(TeraCopy, FastCopy)*
- ✅ **Escudo contra ransomware:** si lo que va a sobrescribir una copia buena parece cifrado de repente, se para *(nadie entre los copiadores)*
- ✅ **Datos de recuperación** (paridad Reed-Solomon, como PAR2): si el disco estropea la copia, se repara sin el original *(nadie entre los copiadores)*

### Verificación y prueba
- ✅ Verificar releyendo el destino sin caché, y reparar los bloques dañados *(TeraCopy, FastCopy, Total Commander, GoodSync)*
- ✅ Verificar también el origen y los archivos omitidos *(FastCopy "Perfect verify", TeraCopy 4)*
- ✅ 11 algoritmos: CRC32, xxHash3/64/128, MD5, SHA-1, SHA-256/512, SHA3-256/512, BLAKE3 *(TeraCopy tiene 17, pero los que faltan están en desuso)*
- ✅ Crear y comprobar archivos de sumas (.sfv, .md5, .sha256, .b3, .xxh3…) *(TeraCopy, FastCopy)*
- ✅ Guardar el hash dentro del propio archivo (flujo alternativo) y revisar las carpetas cada cierto tiempo contra la corrupción silenciosa *(TeraCopy, FastCopy, SyncBackPro)*
- ✅ Copiar las sumas de la lista al portapapeles *(TeraCopy)*
- ✅ **Modo forense:** manifiesto SHA-256 firmado (ECDSA) de lo copiado, que cualquiera puede comprobar con `mcopy verify-manifest` *(nadie entre los copiadores)*

### Velocidad
- ✅ E/S sin caché, en paralelo y con colas por disco *(FastCopy, TeraCopy, Robocopy /J)*
- ✅ Archivos pequeños en paralelo *(Robocopy /MT, TeraCopy, GS RichCopy)*
- ✅ Tamaño de bloque y número de lecturas y escrituras a la vez, por copia *(TeraCopy, Ultracopier, FastCopy)*
- ✅ Usar la caché de Windows en lugar de la E/S directa (para antivirus o unidades raras) *(TeraCopy, Ultracopier)*
- ✅ Clonado instantáneo en ReFS/Dev Drive *(el Explorador de Windows 11, Robocopy)*
- ✅ Copia en el servidor (ODX) y compresión SMB *(el Explorador, Robocopy /COMPRESS)*
- ✅ IoRing para leer muchos archivos pequeños a la vez *(nadie)*
- ✅ Autoajuste por disco *(nadie)*
- ✅ Hasta 255 conexiones a la vez con servidores, **ajustadas solas según la velocidad** *(GS RichCopy 360 llega a 255, fijas; rclone)*
- ✅ **Varios orígenes a la vez:** si el mismo contenido está en dos NAS o dos PC, los archivos grandes se leen de todos *(nadie)*
- ✅ ZIP y 7z comprimidos en paralelo en varios núcleos (unas 3 veces más rápido) *(SyncBackPro; 7-Zip lo hace dentro de un archivo)*
- ✅ Exclusión opcional de Windows Defender *(FastCopy)*
- ✅ Límite de velocidad (también en marcha), prioridad baja, pausa con batería o con juegos, límite por horario *(FastCopy, Robocopy, TeraCopy, Ultracopier)*

### Qué y cómo copiar
- ✅ Modos: copiar, mover, espejo, solo actualizar, solo carpetas, organizar fotos por fecha *(FastCopy, FreeFileSync, Robocopy)*
- ✅ Simulación ("qué pasaría") y después "hacerlo de verdad" *(FastCopy, Robocopy /L)*
- ✅ Carpeta de versiones, con **limpieza automática** (días, semanas y meses) *(FreeFileSync, SyncBackPro, GoodSync)*
- ✅ **Copias de seguridad sin duplicados:** instantáneas por trozos de contenido; diez copias ocupan poco más que una, y se puede volver a cualquiera *(restic y Borg, que no son copiadores)*
- ✅ Filtros por nombre, tamaño, fecha, ocultos y del sistema, **en lenguaje normal**, y **fotos y capturas por el texto que contienen** (OCR de Windows, sin internet) *(nadie con lenguaje normal ni OCR)*
- ✅ Omitir la basura habitual (Thumbs.db, desktop.ini, .DS_Store, ~$…)
- ✅ Datos, fechas, atributos, flujos alternativos, permisos, propietario y auditoría, cada uno por separado, también al mover *(TeraCopy, Robocopy /COPY)*
- ✅ Enlaces simbólicos y uniones (como enlace, seguirlos u omitirlos), enlaces duros, archivos dispersos, rutas largas *(Robocopy, FastCopy, GoodSync)*
- ✅ Tolerancia de fechas configurable (2 segundos de FAT, por defecto) *(TeraCopy, Robocopy /FFT)*
- ✅ Conflictos: preguntar con miniaturas, sobrescribir, saltar, si es más reciente, si cambia el tamaño, conservar ambos, **renombrar el que ya estaba**, renombrar con patrón *(TeraCopy, Ultracopier)*
- ✅ Conflictos de carpetas con su propia pregunta: unir, saltar o preguntar *(Ultracopier)*
- ✅ Omitir fotos duplicadas *(nadie entre los copiadores)*
- ✅ Copia cifrada (AES-256), **a ZIP cifrado** (AES de WinZip) y **a 7z cifrado que oculta también los nombres** *(SyncBackPro; el Explorador crea ZIP y 7z, sin contraseña)*
- ✅ Borrado y borrado seguro: ceros, aleatorio, DoD de 3 y de 7 pasadas *(FastCopy, TeraCopy)*
- ✅ Duplicar una carpeta con Ctrl+arrastrar en su misma ubicación *(TeraCopy, el Explorador)*
- ✅ Sincronización en tiempo real *(Robocopy /MON, RealTimeSync, Bvckup 2, GoodSync)*
- ✅ Sincronización en dos sentidos que **detecta renombrados y movidos** (los renombra en el otro lado sin copiarlos otra vez) *(FreeFileSync, GoodSync, Bvckup 2)*
- ✅ Vista de comparación de carpetas *(FreeFileSync, Beyond Compare)*

### Dónde copiar
- ✅ Móviles y cámaras por USB (MTP) *(FreeFileSync)*
- ✅ FTP, FTPS, SFTP, WebDAV (Nextcloud, ownCloud, NAS, Box, pCloud…), S3 y compatibles *(FreeFileSync, SyncBackPro, GS RichCopy, GoodSync, rclone)*
- ✅ **OneDrive, SharePoint, Google Drive, Dropbox, Azure Blob y Google Cloud Storage**, con inicio de sesión en el navegador o permiso SAS/HMAC *(FreeFileSync, SyncBackPro, GoodSync, rclone)*
- ✅ Contraseñas y tokens en el Administrador de credenciales de Windows
- ✅ **LocalSend en los dos sentidos:** recibir del móvil y enviar al móvil *(TeraCopy 4 solo recibe)*
- ✅ Enviar a otro PC con MegaCopier, cifrado y con código *(GS RichCopy, GoodSync Connect)*

### Uso diario
- ✅ Sustituir la copia del Explorador: Ctrl+V, menú contextual (también el corto de Windows 11), arrastre con botón derecho y con botón izquierdo, pegar desde la barra del Explorador *(TeraCopy, Ultracopier, FastCopy)*
- ✅ Preguntar al arrastrar: nunca, solo al mover o siempre *(TeraCopy)*
- ✅ Un doble clic en la bandeja activa o desactiva la intercepción *(Ultracopier)*
- ✅ Varios destinos a la vez, favoritos, recientes y carpetas abiertas del Explorador, Total Commander y Directory Opus *(TeraCopy Pro)*
- ✅ Cola por disco y pregunta de si unir una copia nueva a la que está en marcha o hacerla aparte *(TeraCopy 4, Ultracopier, Directory Opus, Copy Handler)*
- ✅ Cada copia en su propia ventana, si se quiere *(Ultracopier, SuperCopier)*
- ✅ Reordenar archivos y copias arrastrando; añadir o quitar archivos pendientes; guardar y cargar listas de copia *(Ultracopier, TeraCopy)*
- ✅ Tareas programadas que **se ponen al día** si el PC estaba apagado, diarias, semanales o mensuales *(GS RichCopy, SyncBackPro, GoodSync)*
- ✅ Línea de tiempo de la cola y bandeja de arrastre *(nadie)*
- ✅ Historial con repetir, deshacer y "copiar de vuelta" *(TeraCopy guarda historial, sin deshacer)*
- ✅ Aviso SMART de discos que fallan *(SyncBackPro)*
- ✅ Avisos por correo y por webhook; notificación de Windows al terminar *(SyncBackPro, GS RichCopy, FreeFileSync, FastCopy)*
- ✅ **Control desde el móvil** (página web en la red local, con código) *(nadie)*
- ✅ **Control por asistentes de IA** con un servidor MCP (`mcopy mcp`) *(nadie)*
- ✅ Lista de archivos con detalles al pasar el ratón (bytes, fecha, atributos, sumas completas), filtros por estado y quitar los terminados *(TeraCopy 4)*
- ✅ Gráfico de velocidad *(TeraCopy 4)*
- ✅ Pestaña de registro y exportarlo; un registro por copia que se borra solo a los 30 días *(TeraCopy, Ultracopier, FastCopy)*
- ✅ Informes HTML/CSV, en la carpeta de destino o en una fija; informe de errores al terminar *(TeraCopy Pro, Ultracopier)*
- ✅ Al terminar: sonido, apagar, suspender, hibernar, cerrar sesión (con cuenta atrás), expulsar el destino, ejecutar un programa o un script (según haya ido bien o mal) *(FastCopy, TeraCopy)*
- ✅ Opciones de la copia que se cambian en marcha y "Guardar como predeterminado"; modo desatendido *(TeraCopy)*
- ✅ Siempre visible, minimizar a la bandeja, empezar minimizado, empezar con la vista ampliada, recordar posición y tamaño, no cerrar al terminar si hubo errores *(TeraCopy, SuperCopier)*
- ✅ Porcentaje y velocidad en el título de la ventana (también en la barra de tareas y Alt+Tab) y progreso en el botón de la barra de tareas *(TeraCopy, SuperCopier)*
- ✅ Barra de herramientas a tu gusto: qué accesos se ven, en qué orden, y menú «…» con lo que no cabe *(TeraCopy 4, Directory Opus)*
- ✅ Un solo botón para pausar y reanudar todas, que se transforma de uno en otro
- ✅ Archivos solo en la nube: aviso o se omiten *(Robocopy /XA:O)*
- ✅ Escribir o pegar la ruta de destino, con sugerencias *(TeraCopy)*
- ✅ Comparar un archivo con una suma pegada y CRC32 en el nombre *(TeraCopy)*
- ✅ Unidades y número de carpetas en la pestaña Estado *(TeraCopy 4)*
- ✅ LocalSend: repetidos y tipos a recibir *(TeraCopy 4)*
- ✅ Filtros y listas @ en `mcopy` *(Robocopy, FastCopy)*
- ✅ Tareas al conectar un USB y discos reconocidos por número de serie *(Bvckup 2, GoodSync, FreeFileSync)*
- ✅ Expulsar también la unidad de origen *(TeraCopy)*
- ✅ Carpetas abiertas en Multi Commander, xplorer2, Explorer++ y XYplorer como destino *(TeraCopy)*
- ✅ Registro único con formato propio y envío a Syslog *(Ultracopier, SyncBackPro)*
- ✅ CRC32 en el nombre del archivo *(TeraCopy)*
- ✅ Fechas y atributos de las carpetas aparte *(TeraCopy, Robocopy /DCOPY)*
- ✅ Ejemplos de órdenes al terminar (ntfy, IFTTT, abrir el destino, anotar en un archivo) *(TeraCopy)*
- ✅ Copia por el bit «Archivo» y quitarlo después *(Robocopy /A /M)*
- ✅ Filtros por atributo, último uso y niveles de subcarpetas *(Robocopy /IA /XA /MAXLAD /LEV)*
- ✅ Compensar el cambio de horario *(Robocopy /DST, FreeFileSync, GoodSync)*
- ✅ Archivo de exclusiones .mcignore *(Syncthing, rclone)*
- ✅ Varias parejas de carpetas en una tarea *(FreeFileSync, GoodSync)*
- ✅ Orden antes de empezar *(GoodSync, SyncBackPro)*
- ✅ Renombrar al copiar con máscara *(Total Commander, Directory Opus)*
- ✅ Aplanar carpetas *(Directory Opus)*
- ✅ Conservar la ruta común de archivos sueltos *(TeraCopy, XYplorer)*
- ✅ Conservar la compresión NTFS *(Robocopy)*
- ✅ Tareas aunque nadie haya iniciado sesión, con `mcopy run` *(GoodSync, SyncBackPro)*
- ✅ Línea de comandos (`mcopy`) con códigos de salida, y modo portátil *(FastCopy, Robocopy, TeraCopy)*
- ✅ Instalación por usuario sin permisos de administrador; actualizaciones verificadas dentro de Ajustes *(TeraCopy 4)*
- ✅ 73 temas con texturas, 22 estilos de barra de progreso, 29 conjuntos de iconos combinables, editor de colores, 19 sonidos propios, modo claro/oscuro *(SuperCopier y Ultracopier tienen "skins")*
- ✅ 14 idiomas, que se pueden forzar aunque Windows esté en otro: español, inglés, francés, alemán, italiano, portugués de Brasil, ruso, ucraniano, polaco, neerlandés, turco, chino, japonés y coreano *(TeraCopy, Ultracopier)*

---

## 3. Lo que falta

Todo lo de la investigación está hecho (26 de octubre de 2026). Solo queda lo que está «Fuera del plan», con su motivo.

### Prioridad alta

Hecho todo: copiar una unidad entera y arreglar los nombres que el destino no admite (ver la sección 2).

### Prioridad media

Hecho todo (ver la sección 2), salvo MEGA, que pasa a «Fuera del plan».

### Prioridad baja

Hecho todo (ver la sección 2).

### Fuera del plan

| Falta | Quién lo tiene | Estado |
|---|---|---|
| **Plugins de terceros** | Ultracopier | A petición. |
| **Consola central** para varios PC | SyncBackPro, GS RichCopy Enterprise | A petición. |
| **Versiones para Mac y Linux** | Ultracopier, FreeFileSync, TeraCopy (Mac), rclone | MegaCopier es solo para Windows. |
| **Montar un servidor o una nube como unidad** | rclone | Es otro tipo de programa (un sistema de archivos virtual con controlador). |
| **Transferencia acelerada por UDP** para conexiones muy lejanas | Aspera, Signiant, FileCatalyst, Raysync | Necesita el mismo programa en el otro extremo; para eso ya está enviar a otro PC con MegaCopier. |
| **MEGA** | GoodSync, rclone | Usa un protocolo propio con cifrado de extremo a extremo; habría que reimplementarlo entero. Se puede llegar a MEGA con su programa de escritorio (carpeta sincronizada). |
| **Sincronización continua entre equipos sin servidor** (P2P) | Syncthing, Resilio Sync | Se cubre en parte con la sincronización en tiempo real y el envío a otro PC. |
| **Copia de correo** (IMAP, Exchange) y contenedores VHDX | SyncBackPro | No es copiar archivos. |
| **Búsqueda de fotos por lo que se ve** ("fotos de la playa con perros") | Fotos de Windows en PC Copilot+, Google Fotos | Hecha **por el texto que contienen** (OCR de Windows, en cualquier PC). Para "lo que se ve" hace falta un PC Copilot+ (con NPU) o descargar un modelo de IA de unos 350 MB. |
| **Compresión con la tarjeta gráfica** (GDeflate de DirectStorage) | Juegos con DirectStorage | Hecha **en paralelo en la CPU** (unas 3 veces más rápido). Un ZIP o 7z con GDeflate no lo abriría ningún otro programa. |

### Descartado: obsoleto o ya resuelto de otra forma

- **Partir archivos grandes para FAT32** (TeraCopy 2.3): deja trozos que no sirven sin el programa. Es mejor avisar, como ya se hace.
- **Algoritmos Panama, Tiger, RIPEMD, Whirlpool y MD4** (TeraCopy): en desuso.
- **Unlocker** (TeraCopy 1.2): lo cubre VSS.
- **Nombres 8.3 de FAT** (Robocopy /FAT) y **pausa entre paquetes** (Robocopy /IPG): de otra época; el límite de velocidad hace lo segundo.
- **Convertir la letra de unidad en GUID de volumen** (TeraCopy 4): demasiado técnico para el uso normal.
- **"Copia nativa del SO", "Inode threads", búferes por dispositivo, "Cache source stat", algoritmo del tiempo restante y "Registrar para todos los usuarios"** (Ultracopier): ajustes internos de su motor. El de MegaCopier se ajusta solo por disco, y la instalación por usuario es a propósito.
- **"Carpeta cuando no hay destino"** (Ultracopier): lo resuelve el diálogo "¿Adónde?".
- **TeraCopy Manager, carpetas favoritas con estrella, opciones del archivo INI, Windows XP/Vista/7**: cosas de la época, hechas hoy de otra forma.
- **Crear TAR** (el Explorador de Windows 11): ZIP y 7z cubren el uso en Windows.

---

## 4. Lo que solo tiene MegaCopier

Ninguno de los programas estudiados tiene lo siguiente:
- Reanudar **dentro** de un archivo tras un apagón, comprobando lo ya copiado.
- Escudo contra ransomware antes de sobrescribir copias.
- Datos de recuperación que reparan la copia si el disco la estropea.
- Manifiesto forense firmado.
- Varios orígenes a la vez y conexiones con servidores que se ajustan solas.
- Filtros en lenguaje normal y por el texto de las fotos.
- Fotos por fecha de la cámara y sin duplicados, al copiar.
- LocalSend en los dos sentidos.
- Control desde el móvil y desde asistentes de IA (MCP).
- Línea de tiempo de la cola, bandeja de arrastre, autoajuste por disco, IoRing, historial con deshacer.

---

## Notas sobre la investigación

- Varias fuentes son reseñas o páginas de terceros, y algunas tienen años (ExtremeCopy, Copy Handler, Unstoppable Copier: parecen abandonados).
- Las afirmaciones de velocidad de los fabricantes no están comprobadas por terceros.
- Directory Opus no verifica las copias, por decisión de sus autores.
- RichCopy está abandonado desde 2009. "Ultra Fast Copy" no aparece en ninguna fuente fiable.
- Las funciones de TeraCopy salen de su blog oficial, versión por versión; una función antigua puede funcionar distinto en TeraCopy 4.0.5.
- Lo de los programas de transferencia por UDP sale sobre todo de comparativas de los propios fabricantes.
- Los ZIP y 7z de MegaCopier se comprobaron con lectores independientes (el `tar.exe` de Windows y SharpCompress), y el OCR con el reconocimiento de texto real de Windows.

## Fuentes

- TeraCopy: [codesector.com/teracopy](https://www.codesector.com/teracopy), [blog de Code Sector, todas las versiones](https://blog.codesector.com/category/teracopy/), [notas de TeraCopy 4](https://ideas.codesector.com/changelog/teracopy-4), [TeraCopy 4 RC 3](https://ideas.codesector.com/changelog/teracopy-4-rc-3), [Neowin TeraCopy 4.0 Build 37](https://www.neowin.net/amp/teracopy-40-build-37/), [LocalSend en TeraCopy](https://ideas.codesector.com/p/localsend-protocol-support)
- FastCopy: [manual de FastCopy 5.12](https://fastcopy.jp/help/fastcopy_eng.htm), [Neowin FastCopy 5.7.14](https://www.neowin.net/amp/fastcopy-5714/)
- Ultracopier y SuperCopier: [Wikipedia](https://en.wikipedia.org/wiki/Ultracopier), [Softpedia](https://www.softpedia.com/reviews/windows/Ultracopier-Review-294143.shtml), [GitHub](https://github.com/lambdaX/Ultracopier), [ghacks](https://www.ghacks.net/?p=155627)
- Robocopy: [Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/robocopy)
- Explorador de Windows 11: [crear ZIP, 7z y TAR (Pureinfotech)](https://pureinfotech.com/windows-11-create-7-zip-tar-archive-files/)
- ODX: [Microsoft Learn](https://learn.microsoft.com/en-sg/WINDOWS-HARDWARE/DRIVERS/ifs/offloaded-data-transfers), [NetApp](https://docs.netapp.com/us-en/ontap/smb-admin/odx-concept.html)
- FreeFileSync: [manual](https://freefilesync.org/manual.php?topic=freefilesync), [SourceForge](https://sourceforge.net/projects/freefilesync/), [PCWorld](https://www.pcworld.com/article/2144389/freefilesync-review.html)
- GS RichCopy 360: [gurusquad.com](https://gurusquad.com/products/gs-richcopy-360-enterprise), [documentación](https://gurusquad.com/apps/docs/introduction.html)
- SyncBackPro: [funciones de SyncBackPro](https://www.2brightsparks.com/syncback/help/profeatures.htm)
- GoodSync: [funciones](https://www.goodsync.com/features), [copia delta por bloques](https://help.goodsync.com/hc/en-us/articles/360002346092-Block-level-copy)
- Bvckup 2: [Neowin Bvckup 2 Release 82.10](https://www.neowin.net/amp/bvckup-2-release-8210/), [Neowin Release 79.03](https://www.neowin.net/amp/bvckup-2-release-7903/)
- XYplorer: [funciones](https://www.xyplorer.com/features.php)
- rclone: [página del manual (Debian)](https://dyn.manpages.debian.org/testing/rclone/rclone.1)
- Syncthing y Resilio: [FAQ de Syncthing](https://docs.syncthing.net/users/faq), [comparativa en Capterra](https://www.capterra.com/compare/161568-173194/Syncthing-vs-Resilio-Sync)
- Transferencia por UDP: [Raysync, alternativas a Aspera](https://raysync.io/news/aspera-alternative-solutions-2024/), [Fast.io, transferencia por UDP](https://www.fast.io/resources/udp-file-transfer.md)
- Copy Handler: [dev.copyhandler.com](https://dev.copyhandler.com/projects/CH/summary), [Neowin](https://www.neowin.net/amp/copy-handler-140/)
- ExtremeCopy: [Neowin](https://www.neowin.net/amp/extremecopy-pro-222/), [Genbeta](https://www.genbeta.com/windows/extremecopy-copiar-y-mover-archivos-en-windows-hasta-un-120-mas-rapido/amp)
- Unstoppable Copier: [Technibble](https://www.technibble.com/repair-tool-of-the-week-unstoppable-copier/), [MajorGeeks](https://www.majorgeeks.com/files/details/unstoppable_copier.html)
- RichCopy: [labnol](https://labnol.org/software/richcopy-file-copying-tool-for-windows/8035)
- Total Commander: [foro de Ghisler, verificar](https://www.ghisler.ch/board/viewtopic.php?p=470142)
- Directory Opus: [colas de copia](https://gpsoft.com.au/help/opus12/Documents/Copy_Queues.htm), [verificación](https://resource.dopus.com/t/can-opus-verify-file-copies/41032)
- Files: [files.community](https://files.community/docs)
- Resúmenes: [Online Tech Tips 2026](https://www.online-tech-tips.com/speed-up-slow-windows-file-transfers/), [AlternativeTo](https://www.alternativeto.net/software/teracopy/)
- SMB: [compresión SMB (Microsoft)](https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-compression)
- VSS: [Win32_ShadowCopy.Create](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/vsswmi/create-method-in-class-win32-shadowcopy)
