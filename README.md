<div align="center">

<img src="src/MegaCopier.App/Assets/MegaCopier.ico" width="96" alt="Icono de MegaCopier">

# MegaCopier

**El copiador de archivos para Windows 11 que sobrevive a los apagones.**

Sustituye al copiar y pegar del Explorador. Si la copia se corta (apagón, cuelgue, USB desconectado), sigue desde el
último bloque comprobado, también dentro de un archivo enorme, sin corromperlo. Y además verifica, sincroniza, hace
copias de seguridad y copia a servidores, nubes y móviles.

[![Compilación](https://github.com/cbatista96/MegaCopier/actions/workflows/ci.yml/badge.svg)](https://github.com/cbatista96/MegaCopier/actions/workflows/ci.yml)
[![Última versión](https://img.shields.io/github/v/release/cbatista96/MegaCopier?label=versi%C3%B3n&color=6f42c1)](https://github.com/cbatista96/MegaCopier/releases/latest)
[![Descargas](https://img.shields.io/github/downloads/cbatista96/MegaCopier/total?label=descargas&color=success)](https://github.com/cbatista96/MegaCopier/releases)
[![Licencia: GPL v3](https://img.shields.io/badge/licencia-GPLv3-blue.svg)](LICENSE)
[![Windows 11](https://img.shields.io/badge/Windows%2011-64%20bits-0078D6)](#requisitos)
[![Idiomas](https://img.shields.io/badge/idiomas-14-orange)](#todas-las-características)

### [⬇️ Descargar MegaCopier](https://github.com/cbatista96/MegaCopier/releases/latest)

[Características](#todas-las-características) ·
[Capturas](#capturas) ·
[Guía de uso](docs/Guia.md) ·
[Novedades](CHANGELOG.md) ·
[Comparativa](docs/Comparativa-copiadores.md) ·
[Preguntas frecuentes](docs/Preguntas-frecuentes.md) ·
[English](#english)

<img src="docs/capturas/completa-oscuro.png" width="820" alt="MegaCopier copiando dos carpetas: vista completa con la lista de archivos">

</div>

---

## Por qué MegaCopier

| | |
|---|---|
| ⚡ **Reanuda tras un apagón** | Copia por bloques y anota cada uno solo cuando está de verdad en el disco. Al volver, comprueba lo ya copiado y sigue **dentro del mismo archivo**. Como mucho se repite un bloque. |
| 🚀 **Rápido** | E/S directa al disco en paralelo, colas por disco, clonado instantáneo en ReFS, copia en el propio servidor por red. Entre dos NVMe: **3.050–3.900 MB/s**, frente a 1.800–2.290 MB/s de robocopy. |
| ✅ **Comprueba lo que copia** | Relee el destino sin caché, compara sumas (BLAKE3, SHA-256, xxHash…) y **repara** los bloques dañados. |
| 🛡️ **Protege tus copias** | Escudo contra ransomware, datos de recuperación que reparan la copia si el disco la estropea, versiones con limpieza automática y copias de seguridad sin duplicados. |
| 🌍 **Llega a todas partes** | Móviles y cámaras por USB, LocalSend, otro PC, FTP, SFTP, WebDAV, S3, OneDrive, Google Drive y Dropbox. |
| 🪟 **Integrado en Windows** | Ctrl+V, menú contextual (también el corto de Windows 11) y arrastrar con cualquier botón. Un doble clic en la bandeja lo activa o lo desactiva. |
| 🔒 **Sin telemetría** | No envía nada a ningún sitio salvo lo que tú configures (avisos por correo o webhook). |
| 🎨 **A tu gusto** | 52 temas (con texturas y degradados), 7 conjuntos de iconos combinables, editor de colores, 14 idiomas, consola `mcopy` y versión portátil. |

---

## Descargar e instalar

Todo está en la página de la **[última versión](https://github.com/cbatista96/MegaCopier/releases/latest)**:

| Archivo | Para qué |
|---|---|
| `MegaCopier-Setup-<versión>.exe` | **El instalador. Es el que necesita casi todo el mundo.** |
| `MegaCopier-<versión>-portable.zip` | Versión portátil: se descomprime donde quieras (también en un USB) y no se instala. |
| `mcopy-<versión>.exe` | Solo la consola, para scripts y servidores (no hace falta si instalas MegaCopier: ya la incluye). |
| `SHA256SUMS.txt` | Las sumas SHA-256 de los archivos, para comprobar que son los originales. |

1. Descarga y ejecuta `MegaCopier-Setup-<versión>.exe`.
2. Se instala **solo para tu usuario** en `%LOCALAPPDATA%\Programs\MegaCopier`: no pide permisos de administrador y lleva
   todo lo que necesita (no instala .NET ni nada más).
3. Durante la instalación eliges si añadir las opciones al menú del Explorador y si arrancar con Windows (recomendado).

> [!NOTE]
> El instalador todavía no está firmado digitalmente, así que Windows mostrará el aviso de **SmartScreen**. Pulsa
> **Más información** y luego **Ejecutar de todas formas**. Si quieres comprobar que el archivo es el original, compara su
> suma con la de `SHA256SUMS.txt`:
>
> ```powershell
> Get-FileHash .\MegaCopier-Setup-1.0.0.exe -Algorithm SHA256
> ```

**Versión portátil:** descomprime el ZIP y ejecuta `MegaCopier.exe`. Guarda los ajustes en la carpeta `Data` de al lado y
no toca el registro ni el Explorador (para eso, usa el instalador).

**Desinstalar:** *Configuración > Aplicaciones > MegaCopier > Desinstalar*. Al final pregunta si quieres borrar también tus
ajustes e historial.

### Requisitos

- Windows 11 de 64 bits (x64).
- Unos 90 MB de disco. No necesita internet ni cuentas.

---

## Uso rápido

1. **Copia y pega como siempre** (Ctrl+C / Ctrl+V, o Ctrl+X para mover) en el Explorador. La copia la hace MegaCopier.
2. Se abre una **ventana compacta** con el progreso. **«Más»** la amplía a la vista completa, con la lista de archivos.
3. Si se corta la luz, al volver a encender aparece la copia como **interrumpida**: pulsa **Continuar**.
4. **Doble clic en el icono de la bandeja** para activar o desactivar MegaCopier (desactivado, copia el Explorador).

Desde la consola:

```text
mcopy copy D:\Fotos E:\Copias --verify          copia y comprueba
mcopy move D:\Descargas\Vídeos E:\Vídeos       mueve (instantáneo en el mismo disco)
mcopy copy D:\Trabajo F:\Espejo --mirror        deja F:\Espejo igual que D:\Trabajo
mcopy backup D:\Documentos F:\CopiasSeguridad   copia de seguridad sin duplicados
mcopy restore F:\CopiasSeguridad latest D:\Recuperado
mcopy                                           muestra todas las opciones
```

Todo explicado paso a paso en la **[guía de uso](docs/Guia.md)**.

---

## Capturas

<table>
<tr>
<td width="50%"><img src="docs/capturas/compacta.png" alt="Ventana compacta"><br><b>Ventana compacta</b>: lo justo mientras copia.</td>
<td width="50%"><img src="docs/capturas/interrumpida.png" alt="Copia interrumpida"><br><b>Tras un apagón</b>: la copia espera a que pulses <i>Continuar</i>.</td>
</tr>
<tr>
<td><img src="docs/capturas/reanudada.png" alt="Copia reanudada"><br><b>Reanudada</b>: sigue al 30 % del mismo archivo, no desde cero.</td>
<td><img src="docs/capturas/conflicto.png" alt="El archivo ya existe"><br><b>Si el archivo ya existe</b>: compara los dos y eliges.</td>
</tr>
<tr>
<td><img src="docs/capturas/completa-claro.png" alt="Vista completa en modo claro"><br><b>Vista completa</b> en modo claro: copias a la izquierda, archivos a la derecha.</td>
<td><img src="docs/capturas/comparar.png" alt="Comparar y sincronizar"><br><b>Comparar y sincronizar</b> dos carpetas antes de tocar nada.</td>
</tr>
<tr>
<td><img src="docs/capturas/ampliada-estado.png" alt="Gráfica de velocidad"><br><b>Ventana ampliada</b>: gráfica de velocidad y resumen.</td>
<td><img src="docs/capturas/ampliada-opciones.png" alt="Opciones de la copia"><br><b>Opciones de cada copia</b>, también con la copia en marcha.</td>
</tr>
<tr>
<td><img src="docs/capturas/historial.png" alt="Historial"><br><b>Historial</b> con repetir, deshacer y «copiar de vuelta».</td>
<td><img src="docs/capturas/linea-tiempo.png" alt="Línea de tiempo"><br><b>Línea de tiempo</b>: cuándo terminará cada copia de la cola.</td>
</tr>
<tr>
<td><img src="docs/capturas/terminadas.png" alt="Copias terminadas"><br><b>Al terminar</b>: resumen, «Abrir carpeta» y lo omitido.</td>
<td><img src="docs/capturas/ajustes.png" alt="Ajustes"><br><b>Ajustes</b> como la Configuración de Windows 11: secciones, buscador e iconos.</td>
</tr>
<tr>
<td><img src="docs/capturas/ajustes-comportamiento.png" alt="Ajustes de comportamiento"><br><b>Comportamiento</b>: sumar copias, borrar lo que quedó a medias, expulsar el USB.</td>
<td><img src="docs/capturas/ajustes-errores.png" alt="Ajustes de errores"><br><b>Errores</b>: preguntar qué hacer, reintentos e informe de errores.</td>
</tr>
<tr>
<td colspan="2"><img src="docs/capturas/ajustes-madera.png" alt="Ajustes con el tema Madera y los iconos Mosaico"><br>Tema <b>Madera</b> con los iconos <b>Mosaico</b>: cualquier tema con cualquier conjunto de iconos.</td>

</tr>
</table>

### Temas

**52 temas** que cambian toda la interfaz al momento, hasta los botones, los menús y las barras de desplazamiento. Todos
están diseñados para Windows 11, con degradados, y algunos tienen textura de fondo.
Además, un editor para crear los tuyos.

<table>
<tr>
<td width="33%"><img src="docs/capturas/temas/combustion.png" alt="Combustión"><br>Combustión</td>
<td width="33%"><img src="docs/capturas/temas/codigo-verde.png" alt="Código verde"><br>Código verde</td>
<td width="33%"><img src="docs/capturas/temas/aurora.png" alt="Aurora"><br>Aurora</td>
</tr>
<tr>
<td><img src="docs/capturas/temas/galaxia.png" alt="Galaxia"><br>Galaxia</td>
<td><img src="docs/capturas/temas/neon.png" alt="Neón"><br>Neón</td>
<td><img src="docs/capturas/temas/caja-negra.png" alt="Caja negra"><br>Caja negra</td>
</tr>
<tr>
<td><img src="docs/capturas/temas/madera.png" alt="Madera"><br>Madera</td>
<td><img src="docs/capturas/temas/cuaderno.png" alt="Cuaderno"><br>Cuaderno</td>
<td><img src="docs/capturas/temas/cristal.png" alt="Cristal"><br>Cristal</td>
</tr>
<tr>
<td><img src="docs/capturas/temas/burbuja-azul.png" alt="Burbuja azul"><br>Burbuja azul</td>
<td><img src="docs/capturas/temas/berenjena.png" alt="Berenjena"><br>Berenjena</td>
<td><img src="docs/capturas/temas/humo.png" alt="Humo"><br>Humo</td>
</tr>
<tr>
<td><img src="docs/capturas/temas/cobalto.png" alt="Cobalto"><br>Cobalto</td>
<td><img src="docs/capturas/temas/metro-oscuro.png" alt="Metro oscuro"><br>Metro oscuro</td>
<td><img src="docs/capturas/temas/aero.png" alt="Aero"><br>Aero</td>
</tr>
<tr>
<td><img src="docs/capturas/temas/luna.png" alt="Luna"><br>Luna</td>
<td><img src="docs/capturas/temas/aire.png" alt="Aire"><br>Aire</td>
<td><img src="docs/capturas/temas/menta.png" alt="Menta"><br>Menta</td>
</tr>
<tr>
<td><img src="docs/capturas/temas/fluent-dark.png" alt="Fluent oscuro"><br>Fluent oscuro</td>
<td><img src="docs/capturas/temas/fluent-light.png" alt="Fluent claro"><br>Fluent claro</td>
<td><img src="docs/capturas/temas/midnight.png" alt="Medianoche"><br>Medianoche</td>
</tr>
<tr>
<td><img src="docs/capturas/temas/ocean.png" alt="Océano"><br>Océano</td>
<td><img src="docs/capturas/temas/forest.png" alt="Bosque"><br>Bosque</td>
<td><img src="docs/capturas/temas/sunset.png" alt="Atardecer"><br>Atardecer</td>
</tr>
<tr>
<td><img src="docs/capturas/temas/azul-clasico.png" alt="Azul clásico"><br>Azul clásico</td>
<td><img src="docs/capturas/temas/gris-clasico.png" alt="Gris clásico"><br>Gris clásico</td>
<td><img src="docs/capturas/temas/contrast.png" alt="Alto contraste"><br>Alto contraste</td>
</tr>
</table>

Y otros 24: Grafito, Robot verde, Cielo de otoño, Ámbar, Granate, Vapor, Metro gris, Televisión, Reproductor,
Esmeralda, Mandarina, Metro claro, Pez azul, Línea plana, Plus, Cielo, Brillo, Nieve, Burbuja lima, Tóxico,
Naranja tierra, Moderno 8, Mensajero y Plata. **Fluent (como Windows)** sigue el modo claro u oscuro y el color de acento
de Windows.

### Conjuntos de iconos

Siete estilos de iconos, todos con los iconos modernos de Windows 11, que se combinan con cualquier tema:

<table>
<tr>
<td width="50%"><img src="docs/capturas/iconos/fluent.png" alt="Fluent"><br><b>Fluent</b>: como Windows.</td>
<td width="50%"><img src="docs/capturas/iconos/color.png" alt="Color"><br><b>Color</b>: cada icono con su color.</td>
</tr>
<tr>
<td><img src="docs/capturas/iconos/mosaico.png" alt="Mosaico"><br><b>Mosaico</b>: sobre un cuadrado de color, como la Configuración de Windows 11.</td>
<td><img src="docs/capturas/iconos/suave.png" alt="Suave"><br><b>Suave</b>: con un fondo suave de su color.</td>
</tr>
<tr>
<td><img src="docs/capturas/iconos/degradado.png" alt="Degradado"><br><b>Degradado</b>: con los colores de las barras del tema.</td>
<td><img src="docs/capturas/iconos/neon.png" alt="Neón"><br><b>Neón</b>: con un halo de luz.</td>
</tr>
<tr>
<td colspan="2"><img src="docs/capturas/iconos/windows10.png" alt="Windows 10"><br><b>Windows 10</b>: los iconos clásicos.</td>
</tr>
</table>

---

## Todas las características

### Copia que no se pierde
- **Reanudación tras un apagón** dentro del propio archivo: cada bloque se escribe en el disco y solo después se anota con
  su suma en un diario. Al volver se comprueba lo ya copiado y se sigue; como mucho se pierde un bloque.
- Las copias interrumpidas aparecen al arrancar y se continúan con un clic (o solas, si lo activas).
- **Pausar, saltar el archivo actual, detener y seguir más tarde**; reintentos automáticos y lista de fallidos.
- **Espera** a que vuelva el disco, la red o el espacio libre, en vez de fallar.
- Carpetas **«todo o nada»**: o se copian enteras o no queda nada a medias.
- **Modo rescate** para discos dañados: salva lo legible y deja en ceros lo ilegible.
- **Archivos en uso** (Outlook, bases de datos) copiados desde una instantánea de Windows (VSS).
- **Modo copia de seguridad**: salta los permisos (con permiso de administrador).
- **Archivos cifrados con EFS** copiados tal cual, sin descifrarlos.

### Comprobación y protección
- **Verificación** releyendo el destino sin caché, con **reparación** de los bloques dañados.
- 11 algoritmos de suma: CRC32, xxHash3/64/128, MD5, SHA-1, SHA-256/512, SHA3-256/512 y BLAKE3.
- Crear y comprobar archivos de sumas (`.sha256`, `.md5`, `.sfv`, `.b3`, `.xxh3`…).
- **Guardián de integridad**: guarda la suma de cada archivo y revisa tus carpetas cada cierto tiempo contra la corrupción
  silenciosa del disco.
- **Escudo contra ransomware**: si muchos archivos que van a sobrescribir una copia buena parecen cifrados de repente, la
  copia se para antes de estropear también la copia buena.
- **Datos de recuperación** (paridad Reed-Solomon, como PAR2): si el disco estropea bloques de la copia, se reconstruyen sin
  necesitar el original (`mcopy repair`).
- **Modo forense**: manifiesto firmado (ECDSA) con la SHA-256 de cada archivo, quién y cuándo, comprobable con
  `mcopy verify-manifest`.

### Velocidad
- E/S directa al disco, asíncrona y en paralelo, con colas por disco físico.
- Archivos pequeños en paralelo e IoRing para leer muchos a la vez.
- **Autoajuste por disco**: mide cada disco y elige el tamaño de bloque y las colas.
- **Clonado instantáneo** en ReFS y en las unidades de desarrollo de Windows 11.
- **Copia en el propio servidor** y compresión SMB en red.
- **Copia delta**: al reemplazar archivos grandes solo escribe los bloques que cambiaron (y se puede deshacer).
- **Varios orígenes a la vez**: si lo mismo está en dos NAS o dos PC, los archivos grandes se leen de todos.
- Hasta 255 conexiones con servidores y nubes, **ajustadas solas** según la velocidad.
- ZIP y 7z **comprimidos en paralelo** en varios núcleos.
- Exclusión opcional de Windows Defender, límite de velocidad, prioridad baja, pausa con batería o con juegos y horario.

### Qué y cómo copiar
- Modos: copiar, mover, **espejo**, solo actualizar, solo carpetas y **organizar fotos por fecha** de la cámara.
- **Simulación** («qué pasaría») y después «hacerlo de verdad».
- **Filtros** por nombre, tamaño, fecha y atributos, también **en lenguaje normal** («fotos de más de 2 MB del último mes
  sin los .tmp») y **por el texto que contienen las fotos** (OCR de Windows, sin internet).
- **Conflictos**: preguntar (con miniaturas), reemplazar, omitir, el más reciente, conservar ambos o renombrar con un patrón.
- Omitir **fotos duplicadas** (mismo contenido, cualquier nombre).
- Permisos, propietario, flujos alternativos, atributos, fechas, enlaces simbólicos, uniones, enlaces duros, archivos
  dispersos y rutas de más de 260 caracteres.
- **Carpeta de versiones** para lo que se sobrescribe o se quita, con **limpieza automática** (días, semanas y meses).
- **Copias de seguridad sin duplicados**: diez copias de algo que apenas cambia ocupan poco más que una, y se puede
  restaurar cualquiera.
- **Cifrado**: copia cifrada AES-256 (`.mcenc`), a **ZIP cifrado** (lo abren 7-Zip y WinRAR) o a **7z cifrado**, que oculta
  también los nombres.
- **Borrado seguro**: ceros, aleatorio, DoD de 3 pasadas o DoD de 7 pasadas.

### Comportamiento y errores
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

### Sincronizar
- **Espejo** de una carpeta en otra.
- **Sincronización en los dos sentidos** que recuerda el estado anterior, detecta borrados y **renombrados o movidos** (los
  renombra en el otro lado sin volver a copiarlos); lo sustituido va a la Papelera.
- **Vista de comparación** lado a lado antes de sincronizar, eligiendo qué se hace con cada archivo.
- **Sincronización en tiempo real**: vigila una carpeta y copia cada cambio al momento.

### Dónde copiar
- Discos, USB y carpetas de red.
- **Móviles y cámaras** por USB (MTP), en los dos sentidos.
- **LocalSend** (app gratuita para iPhone y Android): recibir del móvil y **enviar al móvil**.
- **Otro PC con MegaCopier** por la red local, cifrado y con código.
- **Servidores**: FTP, FTPS, SFTP, WebDAV (Nextcloud, ownCloud, NAS…) y S3 y compatibles (AWS, Backblaze B2, Cloudflare R2,
  Wasabi, MinIO).
- **Nubes**: OneDrive, Google Drive y Dropbox, con inicio de sesión en el navegador ([cómo conectarlas](docs/Nube.md)).
- Contraseñas y tokens guardados en el Administrador de credenciales de Windows, nunca en archivos.

### Uso diario
- Sustituye **Ctrl+V**, el **menú contextual** (también el corto de Windows 11) y el **arrastre** del Explorador.
- **Varios destinos a la vez**, favoritos, recientes y las carpetas abiertas en el Explorador, Total Commander y Directory Opus.
- **Cola por disco**: en el mismo disco, de una en una; en discos distintos, a la vez. Se reordena arrastrando.
- **Línea de tiempo** de la cola y **bandeja de arrastre** para ir juntando archivos.
- **Tareas programadas** (una vez, diarias, semanales o mensuales) que se hacen al encender si el PC estaba apagado.
- **Historial** con repetir, deshacer y «copiar de vuelta».
- **Aviso SMART** de discos que empiezan a fallar.
- Avisos al terminar por **correo** y **webhook**; al terminar, apagar, suspender, hibernar o ejecutar un programa.
- **Control desde el móvil**: página web en la red local, con código, para ver, pausar, seguir o detener copias.
- **Asistentes de IA**: servidor MCP (`mcopy mcp`) para que Claude, Copilot y otros pidan copias y copias de seguridad.
- Informes HTML y CSV, listas de copia guardadas y registros.
- **Consola `mcopy`** con todas las funciones y **versión portátil**.
- **14 idiomas**: español, inglés, francés, alemán, italiano, portugués de Brasil, ruso, ucraniano, polaco, neerlandés,
  turco, chino, japonés y coreano.
- **52 temas** que colorean toda la interfaz (hasta las barras de desplazamiento), con degradados y texturas, **7 conjuntos de iconos** que se combinan con cualquier tema, editor de colores, sonidos propios y modo claro u oscuro; accesible con lectores de pantalla.
- **Ajustes como la Configuración de Windows 11**: secciones a la izquierda, buscador, iconos y animaciones.

Cada característica explicada en detalle: **[Características](docs/Caracteristicas.md)**. Cómo se compara con TeraCopy,
FastCopy, Ultracopier, robocopy y otros: **[Comparativa](docs/Comparativa-copiadores.md)**.

---

## Documentación

| Documento | Qué contiene |
|---|---|
| [Guía de uso](docs/Guia.md) | Todo MegaCopier paso a paso: copiar, la ventana, sincronizar, copias de seguridad, servidores, móviles, ajustes y consola. |
| [Características](docs/Caracteristicas.md) | La lista completa, explicada. |
| [Preguntas frecuentes](docs/Preguntas-frecuentes.md) | SmartScreen, apagones, velocidad, desinstalar, problemas habituales. |
| [Nubes](docs/Nube.md) | Cómo conectar OneDrive, Google Drive y Dropbox. |
| [Integraciones](docs/Integraciones.md) | Total Commander, Directory Opus y asistentes de IA (MCP). |
| [Comparativa](docs/Comparativa-copiadores.md) | MegaCopier frente a otros copiadores. |
| [Novedades](CHANGELOG.md) | Qué cambia en cada versión. |
| [Versiones](docs/Versiones.md) | Cómo se numeran y publican las versiones. |
| [Compilar](docs/Compilar.md) | Compilar desde el código, pruebas y arquitectura. |

---

## Versiones y novedades

MegaCopier sigue el [versionado semántico](https://semver.org/lang/es/): **1.2.3** = versión principal . nueva función .
arreglo. Cada versión publicada tiene su página en
**[Releases](https://github.com/cbatista96/MegaCopier/releases)** con sus novedades y sus archivos, y todos los cambios
están en **[CHANGELOG.md](CHANGELOG.md)**. Cómo se publica cada versión: [docs/Versiones.md](docs/Versiones.md).

Para enterarte de las versiones nuevas: botón **Watch > Custom > Releases** arriba en esta página.

---

## Compilar desde el código

```powershell
git clone https://github.com/cbatista96/MegaCopier.git
cd MegaCopier
.\build.ps1             # textos, pruebas, app, extensión del Explorador, consola e instalador
```

Requisitos (todo gratis: .NET 10 SDK, herramientas de C++ e Inno Setup) e instrucciones en **[Compilar.md](docs/Compilar.md)**.

## Contribuir

Las contribuciones son bienvenidas: avisos de fallos, ideas, traducciones y código. Lee **[CONTRIBUTING.md](CONTRIBUTING.md)**
antes de enviar cambios. Los fallos de seguridad se avisan en privado: **[SECURITY.md](SECURITY.md)**.

## Licencia

MegaCopier es software libre con licencia **[GNU GPL v3](LICENSE)**: puedes usarlo, estudiarlo, modificarlo y
compartirlo; si distribuyes una versión modificada, tiene que seguir siendo libre y con la misma licencia.

---

## English

**MegaCopier** is a file copier for Windows 11 that survives power cuts: if a copy is interrupted, it resumes from the
last verified block, even inside a huge file, without corrupting it. It replaces Explorer's copy and paste and adds
verification with block repair, two-way and real-time sync, deduplicated backups, ransomware protection, self-healing
recovery data, phone and cloud transfers (MTP, LocalSend, FTP, SFTP, WebDAV, S3, OneDrive, Google Drive, Dropbox),
52 themes, 7 icon sets, 14 languages and a `mcopy` command line. No telemetry.

- **Download:** [latest release](https://github.com/cbatista96/MegaCopier/releases/latest) — the `Setup` installer
  (per-user, no admin rights) or the portable ZIP. Windows 11, 64-bit.
- **The app is fully translated into English**; the documentation is in Spanish: [features](docs/Caracteristicas.md),
  [guide](docs/Guia.md), [building](docs/Compilar.md).
- **License:** [GPL v3](LICENSE). Contributions welcome, see [CONTRIBUTING.md](CONTRIBUTING.md).
