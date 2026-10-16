# MegaCopier frente a los demás copiadores

Investigación hecha el 16 de octubre de 2026 en las webs de los fabricantes, sus notas de versión y reseñas (fuentes al final).
Lo que tiene MegaCopier está comprobado en su código, no de memoria.

Programas estudiados: **TeraCopy 4**, **FastCopy 5.12**, **Ultracopier 3**, **Robocopy** (viene con Windows),
**FreeFileSync**, **GS RichCopy 360**, **SyncBackPro**, **GoodSync**, **Bvckup 2**, **Copy Handler**, **ExtremeCopy**,
**Unstoppable Copier**, **RichCopy** (de Microsoft, abandonado en 2009), **Total Commander**, **Directory Opus** y **Files**.

---

## 1. Las tres funciones "sin probar en real", explicadas fácil

Las tres están programadas y revisadas contra la documentación de Microsoft. Si algo falla, MegaCopier copia de la forma
normal: nunca se pierde la copia por culpa de ellas. Aun así, solo se pueden probar del todo con el equipo adecuado.

| Función | Qué hace, en fácil | Qué hace falta para probarla |
|---|---|---|
| **Clonado en ReFS** | En discos con formato ReFS (las "unidades de desarrollo" de Windows 11), copiar dentro del mismo disco es instantáneo aunque el archivo pese 50 GB: no se duplican los datos, la copia apunta a los mismos. | Crear una unidad de desarrollo (Configuración › Sistema › Almacenamiento › Discos y volúmenes). Pide permiso de administrador. |
| **VSS** (instantáneas) | Copia archivos que otro programa tiene abiertos y bloqueados (el correo de Outlook, una base de datos). Windows hace una "foto" del disco y se copia desde la foto. | Que tú aceptes el aviso de Windows "¿Quieres permitir…?" (2 minutos). |
| **SMB** (red) | Al copiar entre dos carpetas del mismo servidor o NAS, el servidor copia solo, sin pasar los datos por tu PC. Con la compresión activada, los datos viajan comprimidos (más rápido en redes lentas). | Una carpeta compartida en red (otro PC o un NAS). |

**Arreglos de la revisión (commit `8f6b285`):**
- **ReFS:** el tamaño de clúster se toma igual que en el ejemplo oficial de Microsoft, y cualquier rechazo vuelve a la copia normal.
- **VSS:** la "foto" se lee a través de un acceso directo temporal (enlace simbólico), la técnica de los programas de copia de seguridad. Así el motor ve rutas normales.
- **Red:** ahora reconoce también las unidades de red con letra (por ejemplo `Z:` conectada a un NAS). Antes solo reconocía `\\servidor\carpeta`.
- **Clonado y copia en el servidor:** ya no se relee el archivo entero para calcular su hash, salvo si pides verificar o guardar sumas. Antes, un clonado "instantáneo" de 50 GB tardaba lo que se tarda en leer 50 GB.

---

## 2. Lo que ya tiene MegaCopier

✅ = lo tiene. Entre paréntesis, quién más lo tiene.

### Copia y fiabilidad
- ✅ **Reanudar tras un apagón** desde el último bloque verificado, también dentro de un mismo archivo enorme. *(Nadie más igual; Robocopy /Z reanuda, pero sin verificar)*
- ✅ Pausa, continuar, saltar el archivo actual, parar y reanudar más tarde *(TeraCopy, Ultracopier, Copy Handler, ExtremeCopy)*
- ✅ Reintentos automáticos y lista de fallidos para reintentar solo esos *(TeraCopy)*
- ✅ Esperar a que vuelva el disco o la red, o a que haya espacio libre *(TeraCopy; Robocopy /LFSM para el espacio)*
- ✅ Retomar solas las copias interrumpidas al arrancar *(Copy Handler)*
- ✅ Carpetas "todo o nada" (o se copia entera o no queda nada a medias)
- ✅ Modo rescate para discos dañados: sigue leyendo lo que se pueda *(Unstoppable Copier)*
- ✅ Copiar archivos en uso con instantáneas VSS *(TeraCopy 4, GS RichCopy, SyncBackPro, Bvckup 2)*
- ✅ Copia delta (solo lo que cambió) con posibilidad de deshacer *(Bvckup 2, GoodSync)*

### Verificación
- ✅ Verificar releyendo el destino sin caché, y reparar los bloques dañados *(TeraCopy, FastCopy, Total Commander)*
- ✅ 11 algoritmos: CRC32, xxHash3/64/128, MD5, SHA-1, SHA-256/512, SHA3-256/512, BLAKE3 *(TeraCopy tiene 17)*
- ✅ Crear y comprobar archivos de sumas (.sfv, .md5, .sha256, .b3…) *(TeraCopy, FastCopy)*
- ✅ Guardar el hash dentro del propio archivo y vigilar carpetas contra la corrupción silenciosa *(FastCopy guarda su información de verificación de forma parecida)*

### Velocidad
- ✅ E/S sin caché, en paralelo y con colas por disco *(FastCopy, TeraCopy)*
- ✅ Archivos pequeños en paralelo *(Robocopy /MT, TeraCopy, GS RichCopy)*
- ✅ Clonado instantáneo en ReFS/Dev Drive *(solo el Explorador de Windows 11)*
- ✅ Copia en el servidor y compresión SMB *(solo el Explorador y Robocopy /COMPRESS)*
- ✅ IoRing para leer muchos archivos pequeños a la vez *(nadie)*
- ✅ Autoajuste por disco (mide cada disco y elige bloque y colas) *(nadie)*
- ✅ Límite de velocidad, prioridad baja, pausa con batería o con juegos a pantalla completa, horario *(FastCopy, Robocopy)*
- ✅ Reservar el espacio del archivo de una vez *(FastCopy)* y evitar la suspensión del PC mientras copia *(FastCopy)*

### Qué y cómo copiar
- ✅ Modos: copiar, espejo (sincronizar), solo actualizar, solo carpetas, organizar fotos por fecha *(FastCopy, FreeFileSync, Robocopy)*
- ✅ Simulación ("qué pasaría") y después "hacerlo de verdad" *(FastCopy "Listing", Robocopy /L)*
- ✅ Carpeta de versiones para lo que se sobrescribe *(FreeFileSync, SyncBackPro)*
- ✅ Filtros por nombre, tamaño y fecha, y **en lenguaje normal** ("fotos de más de 2 MB del último mes") *(los filtros: todos; en lenguaje normal: nadie)*
- ✅ Permisos, flujos alternativos, atributos, fechas, enlaces, uniones, enlaces duros, archivos dispersos, rutas largas *(Robocopy, FastCopy)*
- ✅ Conflictos: preguntar con miniaturas, sobrescribir, saltar, más reciente, renombrar con patrón *(TeraCopy, Ultracopier)*
- ✅ Omitir fotos duplicadas *(nadie entre los copiadores)*
- ✅ Copia cifrada para USB (AES-256) y descifrar *(SyncBackPro, dentro de ZIP/7z; ningún copiador)*
- ✅ Borrado y borrado seguro *(TeraCopy, FastCopy)*

### Uso diario
- ✅ Sustituir la copia del Explorador: Ctrl+V, menú contextual, arrastrar con botón derecho *(TeraCopy, Ultracopier, FastCopy)*
- ✅ Varios destinos a la vez, favoritos, recientes y carpetas abiertas del Explorador como destino *(TeraCopy Pro)*
- ✅ Cola por disco: en el mismo disco, de una en una *(TeraCopy 4, Directory Opus, Copy Handler)*
- ✅ Reordenar archivos y copias arrastrando *(Ultracopier)*
- ✅ Programar copias y tareas de Windows *(GS RichCopy, FastCopy, SyncBackPro)*
- ✅ Línea de tiempo de la cola *(nadie)*
- ✅ Bandeja de arrastre *(nadie entre los copiadores; existe en Mac: Dropover, Yoink)*
- ✅ Historial con deshacer y "copiar de vuelta" *(TeraCopy guarda historial, sin deshacer)*
- ✅ Enviar a otro PC de la red local, cifrado *(TeraCopy 4 recibe por LocalSend; GS RichCopy)*
- ✅ Aviso SMART de discos que fallan *(nadie)*
- ✅ Informes HTML/CSV, guardar y cargar listas de copia, registros *(TeraCopy Pro, Ultracopier)*
- ✅ Al terminar: sonido, apagar, suspender, hibernar, cerrar sesión, ejecutar un programa, aviso web (webhook) *(FastCopy)*
- ✅ Línea de comandos (`mcopy`), modo portátil *(FastCopy, Robocopy)*
- ✅ 11 temas, editor de colores, sonidos propios, modo claro/oscuro *(Ultracopier tiene "skins")*
- ✅ Español e inglés

---

## 3. Lo que todavía falta

Ordenado por importancia para un usuario normal.

| # | Falta | Quién lo tiene | Importancia | Dificultad |
|---|---|---|---|---|
| 1 | **Más idiomas.** Ahora solo hay español e inglés; TeraCopy y Ultracopier tienen decenas. | TeraCopy, Ultracopier, ExtremeCopy | Alta | Baja |
| 2 | **Sustituir el arrastre con botón izquierdo** entre ventanas del Explorador, con aviso opcional antes de mover. | TeraCopy, FastCopy, Ultracopier | Alta | Alta |
| 3 | **Copiar a o desde móviles y cámaras** conectados por USB (MTP). | FreeFileSync | Alta | Media-alta |
| 4 | **Recibir desde el móvil con LocalSend** (la app gratuita para iPhone/Android). | TeraCopy 4 (novedad de junio de 2026) | Media-alta | Media |
| 5 | **Sincronización en tiempo real:** vigilar una carpeta y copiar cada cambio al momento. | Robocopy /MON, RealTimeSync (FreeFileSync), Bvckup 2, GS RichCopy | Media-alta | Media |
| 6 | **Sincronización en las dos direcciones**, detectando renombrados y borrados en ambos lados. | FreeFileSync, GoodSync, SyncBackPro | Media-alta | Alta |
| 7 | **Vista de comparación de carpetas** lado a lado antes de sincronizar. | FreeFileSync, Beyond Compare, Total Commander | Media | Media |
| 8 | **Nube y servidores:** FTP, SFTP, WebDAV, OneDrive, Google Drive, Dropbox, S3. | FreeFileSync, SyncBackPro, GS RichCopy | Media | Alta |
| 9 | **Avisos por correo electrónico** (ahora solo webhook). | SyncBackPro, GS RichCopy, FreeFileSync | Media-baja | Baja |
| 10 | **Modo copia de seguridad:** copiar carpetas protegidas saltándose permisos, con permiso de administrador. | Robocopy /B, TeraCopy (servicio con permisos) | Media | Media |
| 11 | **Carpetas abiertas de Total Commander y Directory Opus** como destinos, e integración con ellos. | TeraCopy | Media-baja | Media |
| 12 | **Entrada en el menú corto de Windows 11** (sin "Mostrar más opciones"). | TeraCopy, FastCopy | Media | Necesita firma digital de pago |
| 13 | **Borrado seguro de varias pasadas** (ahora una pasada de ceros). | FastCopy (3 pasadas, estilo NSA) | Baja | Baja |
| 14 | **Archivos cifrados con EFS** copiados tal cual, sin descifrarlos. | Robocopy /EFSRAW | Baja | Baja |
| 15 | **Copiar dentro de un ZIP o 7z**, comprimido y cifrado. | SyncBackPro | Baja | Media |
| 16 | **Más hilos para servidores** (hasta 255; ahora hasta 16 para archivos pequeños). | GS RichCopy 360 | Baja (solo empresas) | Baja |
| 17 | **Añadirse a las exclusiones de Windows Defender** para copiar más rápido (con permiso). | FastCopy | Baja | Baja |
| 18 | **Plugins de terceros.** | Ultracopier | Baja | Alta |
| 19 | **Consola central** para controlar MegaCopier en varios PC. | SyncBackPro, GS RichCopy Enterprise | Baja (solo empresas) | Alta |
| 20 | **Versiones para Mac y Linux.** | Ultracopier, FreeFileSync, TeraCopy (Mac) | Baja: por diseño, MegaCopier es solo para Windows | Muy alta |

**Orden recomendado:** 1 → 2 → 3 → 4 → 5 → 6 y 7 juntos → 8 → 9 y 10 → el resto.

---

## 4. Lo que solo tiene MegaCopier

Ninguno de los programas estudiados tiene lo siguiente:
- Reanudar **dentro** de un archivo tras un apagón, comprobando lo ya copiado.
- Filtros en lenguaje normal.
- Fotos por fecha de la cámara y sin duplicados, al copiar.
- Copia cifrada para USB integrada en el copiador.
- Aviso SMART de discos que fallan.
- Línea de tiempo de la cola.
- Bandeja de arrastre.
- Autoajuste por disco.
- IoRing.
- Historial con deshacer.

---

## Notas sobre la investigación

- Varias fuentes son reseñas o páginas de terceros, y algunas tienen años (ExtremeCopy, Copy Handler, Unstoppable Copier: parecen abandonados).
- Las afirmaciones de velocidad de los fabricantes no están comprobadas por terceros.
- Directory Opus no verifica las copias, por decisión de sus autores.
- RichCopy está abandonado desde 2009. "Ultra Fast Copy" no aparece en ninguna fuente fiable.

## Fuentes

- TeraCopy: [codesector.com/teracopy](https://www.codesector.com/teracopy), [notas de TeraCopy 4](https://ideas.codesector.com/changelog/teracopy-4), [Neowin TeraCopy 4.0 Build 37](https://www.neowin.net/amp/teracopy-40-build-37/), [LocalSend en TeraCopy](https://ideas.codesector.com/p/localsend-protocol-support)
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
- VSS: [Win32_ShadowCopy.Create](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/vsswmi/create-method-in-class-win32-shadowcopy)
