# MegaCopier frente a los demás copiadores

Investigación hecha el 16 de octubre de 2026 y actualizada el 17 de octubre de 2026, con las webs de los fabricantes, sus
notas de versión y reseñas (fuentes al final). Lo que tiene MegaCopier está comprobado en su código y en sus pruebas
automáticas, no de memoria.

Programas estudiados: **TeraCopy 4**, **FastCopy 5.12**, **Ultracopier 3**, **Robocopy** (viene con Windows),
**FreeFileSync**, **GS RichCopy 360**, **SyncBackPro**, **GoodSync**, **Bvckup 2**, **Copy Handler**, **ExtremeCopy**,
**Unstoppable Copier**, **RichCopy** (de Microsoft, abandonado en 2009), **Total Commander**, **Directory Opus** y **Files**.

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
| **SMB** (red) | Copia en el propio servidor y compresión en redes lentas. | Una carpeta compartida en otro PC o un NAS. |
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
- ✅ Reintentos automáticos y lista de fallidos para reintentar solo esos *(TeraCopy)*
- ✅ Esperar a que vuelva el disco o la red, o a que haya espacio libre *(TeraCopy; Robocopy /LFSM)*
- ✅ Retomar solas las copias interrumpidas al arrancar *(Copy Handler)*
- ✅ Carpetas "todo o nada" (o se copia entera o no queda nada a medias)
- ✅ Modo rescate para discos dañados *(Unstoppable Copier)*
- ✅ Copiar archivos en uso con instantáneas VSS *(TeraCopy 4, GS RichCopy, SyncBackPro, Bvckup 2)*
- ✅ Copia delta (solo lo que cambió) con posibilidad de deshacer *(Bvckup 2, GoodSync)*
- ✅ Modo copia de seguridad, saltándose permisos, con permiso de administrador *(Robocopy /B)*
- ✅ Archivos cifrados con EFS copiados tal cual *(Robocopy /EFSRAW)*
- ✅ **Escudo contra ransomware:** si lo que va a sobrescribir una copia buena parece cifrado de repente, se para *(nadie entre los copiadores)*
- ✅ **Datos de recuperación** (paridad Reed-Solomon, como PAR2): si el disco estropea la copia, se repara sin el original *(nadie entre los copiadores)*

### Verificación y prueba
- ✅ Verificar releyendo el destino sin caché, y reparar los bloques dañados *(TeraCopy, FastCopy, Total Commander)*
- ✅ 11 algoritmos: CRC32, xxHash3/64/128, MD5, SHA-1, SHA-256/512, SHA3-256/512, BLAKE3 *(TeraCopy tiene 17)*
- ✅ Crear y comprobar archivos de sumas (.sfv, .md5, .sha256, .b3…) *(TeraCopy, FastCopy)*
- ✅ Guardar el hash dentro del propio archivo y revisar las carpetas cada cierto tiempo contra la corrupción silenciosa *(FastCopy, parecido)*
- ✅ **Modo forense:** manifiesto SHA-256 firmado (ECDSA) de lo copiado, que cualquiera puede comprobar con `mcopy verify-manifest` *(nadie entre los copiadores)*

### Velocidad
- ✅ E/S sin caché, en paralelo y con colas por disco *(FastCopy, TeraCopy)*
- ✅ Archivos pequeños en paralelo *(Robocopy /MT, TeraCopy, GS RichCopy)*
- ✅ Clonado instantáneo en ReFS/Dev Drive *(solo el Explorador de Windows 11)*
- ✅ Copia en el servidor y compresión SMB *(solo el Explorador y Robocopy /COMPRESS)*
- ✅ IoRing para leer muchos archivos pequeños a la vez *(nadie)*
- ✅ Autoajuste por disco *(nadie)*
- ✅ Hasta 255 conexiones a la vez con servidores, **ajustadas solas según la velocidad** *(GS RichCopy 360 llega a 255, fijas)*
- ✅ **Varios orígenes a la vez:** si el mismo contenido está en dos NAS o dos PC, los archivos grandes se leen de todos *(nadie)*
- ✅ ZIP y 7z comprimidos en paralelo en varios núcleos (unas 3 veces más rápido) *(7-Zip lo hace dentro de un archivo; los copiadores no)*
- ✅ Exclusión opcional de Windows Defender *(FastCopy)*
- ✅ Límite de velocidad, prioridad baja, pausa con batería o con juegos, horario *(FastCopy, Robocopy)*

### Qué y cómo copiar
- ✅ Modos: copiar, espejo, solo actualizar, solo carpetas, organizar fotos por fecha *(FastCopy, FreeFileSync, Robocopy)*
- ✅ Simulación ("qué pasaría") y después "hacerlo de verdad" *(FastCopy, Robocopy /L)*
- ✅ Carpeta de versiones, con **limpieza automática** (días, semanas y meses) *(FreeFileSync, SyncBackPro)*
- ✅ **Copias de seguridad sin duplicados:** instantáneas por trozos de contenido; diez copias ocupan poco más que una, y se puede volver a cualquiera *(restic y Borg, que no son copiadores)*
- ✅ Filtros por nombre, tamaño y fecha, **en lenguaje normal**, y **fotos y capturas por el texto que contienen** (OCR de Windows, sin internet) *(nadie)*
- ✅ Permisos, flujos alternativos, atributos, fechas, enlaces, uniones, enlaces duros, archivos dispersos, rutas largas *(Robocopy, FastCopy)*
- ✅ Conflictos: preguntar con miniaturas, sobrescribir, saltar, más reciente, renombrar con patrón *(TeraCopy, Ultracopier)*
- ✅ Omitir fotos duplicadas *(nadie entre los copiadores)*
- ✅ Copia cifrada (AES-256), **a ZIP cifrado** (AES de WinZip) y **a 7z cifrado que oculta también los nombres** *(SyncBackPro)*
- ✅ Borrado y borrado seguro: ceros, aleatorio, DoD de 3 y de 7 pasadas *(FastCopy, TeraCopy)*
- ✅ Sincronización en tiempo real *(Robocopy /MON, RealTimeSync, Bvckup 2)*
- ✅ Sincronización en dos sentidos que **detecta renombrados y movidos** (los renombra en el otro lado sin copiarlos otra vez) *(FreeFileSync, GoodSync)*
- ✅ Vista de comparación de carpetas *(FreeFileSync, Beyond Compare)*

### Dónde copiar
- ✅ Móviles y cámaras por USB (MTP) *(FreeFileSync)*
- ✅ FTP, FTPS, SFTP, WebDAV, S3 y compatibles *(FreeFileSync, SyncBackPro, GS RichCopy)*
- ✅ **OneDrive, Google Drive y Dropbox**, con inicio de sesión en el navegador *(FreeFileSync, SyncBackPro)*
- ✅ **LocalSend en los dos sentidos:** recibir del móvil y enviar al móvil *(TeraCopy 4 solo recibe)*
- ✅ Enviar a otro PC con MegaCopier, cifrado *(GS RichCopy)*

### Uso diario
- ✅ Sustituir la copia del Explorador: Ctrl+V, menú contextual (también el corto de Windows 11), arrastre con botón derecho y con botón izquierdo *(TeraCopy, Ultracopier, FastCopy)*
- ✅ Varios destinos a la vez, favoritos, recientes y carpetas abiertas del Explorador, Total Commander y Directory Opus *(TeraCopy Pro)*
- ✅ Cola por disco *(TeraCopy 4, Directory Opus, Copy Handler)*
- ✅ Reordenar archivos y copias arrastrando *(Ultracopier)*
- ✅ Tareas programadas que **se ponen al día** si el PC estaba apagado, diarias, semanales o mensuales *(GS RichCopy, FastCopy, SyncBackPro)*
- ✅ Línea de tiempo de la cola y bandeja de arrastre *(nadie)*
- ✅ Historial con deshacer y "copiar de vuelta" *(TeraCopy guarda historial, sin deshacer)*
- ✅ Aviso SMART de discos que fallan *(nadie)*
- ✅ Avisos por correo y por webhook *(SyncBackPro, GS RichCopy, FreeFileSync)*
- ✅ **Control desde el móvil** (página web en la red local, con código) *(nadie)*
- ✅ **Control por asistentes de IA** con un servidor MCP (`mcopy mcp`) *(nadie)*
- ✅ Lista de archivos con detalles al pasar el ratón (bytes, fecha, atributos, sumas completas) *(TeraCopy 4)*
- ✅ Informes HTML/CSV, listas de copia, registros *(TeraCopy Pro, Ultracopier)*
- ✅ Al terminar: sonido, apagar, suspender, hibernar, cerrar sesión, ejecutar un programa *(FastCopy)*
- ✅ Línea de comandos (`mcopy`) y modo portátil *(FastCopy, Robocopy)*
- ✅ 73 temas con texturas, 22 estilos de barra de progreso, 23 conjuntos de iconos combinables, editor de colores, 19 sonidos propios, modo claro/oscuro *(SuperCopier y Ultracopier tienen "skins")*
- ✅ 14 idiomas: español, inglés, francés, alemán, italiano, portugués de Brasil, ruso, ucraniano, polaco, neerlandés, turco, chino, japonés y coreano *(TeraCopy, Ultracopier)*

---

## 3. Lo que falta

Todo lo de la lista anterior de "lo que falta" está hecho, salvo lo que se dejó fuera a propósito y dos ideas que se han
hecho de otra forma:

| Falta | Quién lo tiene | Estado |
|---|---|---|
| **Plugins de terceros** | Ultracopier | Fuera del plan, a petición. |
| **Consola central** para varios PC | SyncBackPro, GS RichCopy Enterprise | Fuera del plan, a petición. |
| **Versiones para Mac y Linux** | Ultracopier, FreeFileSync, TeraCopy (Mac) | Fuera del plan: MegaCopier es solo para Windows. |
| **Búsqueda de fotos por lo que se ve** ("fotos de la playa con perros") | Fotos de Windows en PC Copilot+, Google Fotos | Hecha **por el texto que contienen** (OCR de Windows, en cualquier PC). Para "lo que se ve" hace falta un PC Copilot+ (con NPU) o descargar un modelo de IA de unos 350 MB. |
| **Compresión con la tarjeta gráfica** (GDeflate de DirectStorage) | Juegos con DirectStorage | Hecha **en paralelo en la CPU** (unas 3 veces más rápido). GDeflate no es un formato que abra ningún otro programa: un ZIP o 7z así no se podría abrir con 7-Zip ni WinRAR. |

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
- Control desde el móvil y desde asistentes de IA (MCP).
- Aviso SMART, línea de tiempo de la cola, bandeja de arrastre, autoajuste por disco, IoRing, historial con deshacer.

---

## Notas sobre la investigación

- Varias fuentes son reseñas o páginas de terceros, y algunas tienen años (ExtremeCopy, Copy Handler, Unstoppable Copier: parecen abandonados).
- Las afirmaciones de velocidad de los fabricantes no están comprobadas por terceros.
- Directory Opus no verifica las copias, por decisión de sus autores.
- RichCopy está abandonado desde 2009. "Ultra Fast Copy" no aparece en ninguna fuente fiable.
- Los ZIP y 7z de MegaCopier se comprobaron con lectores independientes (el `tar.exe` de Windows y SharpCompress), y el OCR con el reconocimiento de texto real de Windows.

## Fuentes

- TeraCopy: [codesector.com/teracopy](https://www.codesector.com/teracopy), [notas de TeraCopy 4](https://ideas.codesector.com/changelog/teracopy-4), [TeraCopy 4 RC 3](https://ideas.codesector.com/changelog/teracopy-4-rc-3), [Neowin TeraCopy 4.0 Build 37](https://www.neowin.net/amp/teracopy-40-build-37/), [LocalSend en TeraCopy](https://ideas.codesector.com/p/localsend-protocol-support)
- FastCopy: [manual de FastCopy 5.12](https://fastcopy.jp/help/fastcopy_eng.htm), [Neowin FastCopy 5.7.14](https://www.neowin.net/amp/fastcopy-5714/)
- Ultracopier: [Wikipedia](https://en.wikipedia.org/wiki/Ultracopier), [Softpedia](https://www.softpedia.com/reviews/windows/Ultracopier-Review-294143.shtml), [GitHub](https://github.com/lambdaX/Ultracopier)
- Robocopy: [Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/robocopy)
- FreeFileSync: [SourceForge](https://sourceforge.net/projects/freefilesync/), [PCWorld](https://www.pcworld.com/article/2144389/freefilesync-review.html)
- GS RichCopy 360: [gurusquad.com](https://gurusquad.com/products/gs-richcopy-360-enterprise), [documentación](https://gurusquad.com/apps/docs/introduction.html)
- SyncBackPro: [funciones de SyncBackPro](https://support.2brightsparks.com/syncback/help/profeatures.htm)
- GoodSync: [copia delta por bloques](https://help.goodsync.com/hc/en-us/articles/360002346092-Block-level-copy), [funciones](https://www.goodsync.com/features)
- Bvckup 2: [Neowin Bvckup 2 Release 82.10](https://www.neowin.net/amp/bvckup-2-release-8210/)
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
