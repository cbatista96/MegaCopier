# Versiones y control de cambios

Cómo se registran los cambios de MegaCopier y cómo se publica cada versión para que la gente la descargue.

## Resumen

```text
 cambio en el código  ──►  línea en CHANGELOG.md  ──►  commit en main  ──►  GitHub compila y prueba (CI)
 (arreglo, función…)       bajo [Sin publicar]
                                                    cuando quieras publicar:
                           .\tools\publicar-version.ps1 1.1.0  ──►  GitHub publica la versión 1.1.0
                                                                    con instalador, ZIP, mcopy y sumas
```

## 1. Los números de versión

MegaCopier usa el [versionado semántico](https://semver.org/lang/es/): **principal.menor.arreglo**, por ejemplo `1.4.2`.

| Sube… | Cuándo | Ejemplo |
|---|---|---|
| **arreglo** (`1.0.0` → `1.0.1`) | Solo se arreglan fallos; nada nuevo. | Arreglada la copia a FTP con nombres con acentos. |
| **menor** (`1.0.1` → `1.1.0`) | Hay funciones nuevas y todo lo anterior sigue igual. | Nueva copia a Amazon Drive. |
| **principal** (`1.1.0` → `2.0.0`) | Algo deja de funcionar como antes (cambia el formato de los ajustes, se quita una opción…). | Las copias de seguridad usan un formato nuevo. |

La versión está escrita en **un solo sitio**, `Directory.Build.props` (`<Version>1.0.0</Version>`). De ahí la toman la app
(*Acerca de*), la consola `mcopy`, la extensión del Explorador y el instalador. No hay que cambiarla a mano: lo hace el
script de publicar.

## 2. Cada cambio queda anotado

[`CHANGELOG.md`](../CHANGELOG.md) es la lista de novedades de cada versión, en el formato
[Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Arriba del todo hay siempre una sección
**`## [Sin publicar]`** con lo que ya está hecho pero todavía no se ha publicado.

`CHANGELOG.en.md` contiene el mismo historial completo en inglés, con `[Unreleased]`. Cada cambio debe traducirse también allí. La app elige español solo para la interfaz española e inglés para los demás idiomas; ambos archivos se incrustan para consulta sin conexión. Las capturas deben conservar las mismas direcciones en ambas versiones.

**Regla: cada cambio que note quien usa MegaCopier añade una línea en `[Sin publicar]`**, en el mismo commit que el cambio,
en su apartado:

| Apartado | Para |
|---|---|
| `### Añadido` | Funciones nuevas. |
| `### Cambiado` | Cambios en algo que ya existía. |
| `### Arreglado` | Fallos corregidos. |
| `### Quitado` | Lo que se elimina. |
| `### Seguridad` | Arreglos de seguridad. |

Ejemplo:

```markdown
## [Sin publicar]

### Añadido
- Copia a Amazon S3 Glacier.

### Arreglado
- La ventana compacta ya no parpadea al pasar de una copia a otra.
```

Se escribe para quien usa el programa, no para programadores: «Arreglado: …» y qué notará. Los cambios internos (pruebas,
limpieza de código) no hace falta anotarlos.

Además de `CHANGELOG.md`, el **historial de git** guarda cada cambio con su explicación: se ve en
[Commits](https://github.com/cbatista96/MegaCopier/commits/main) en GitHub, o con `git log` en local.

## 3. Cada cambio se comprueba solo

Cada vez que se sube algo a `main` (o alguien propone un cambio con un *pull request*), GitHub ejecuta
[`ci.yml`](../.github/workflows/ci.yml): compila todo en un Windows limpio y pasa las más de 300 pruebas. El resultado se ve
en la pestaña [Actions](https://github.com/cbatista96/MegaCopier/actions) y en la insignia **Compilación** del README
(verde = bien, roja = algo falla).

## 4. Publicar una versión

Cuando haya suficientes cambios en `[Sin publicar]` y la compilación de GitHub esté en verde:

```powershell
.\tools\publicar-version.ps1 1.1.0
```

El script:

1. Comprueba que estás en `main`, sin cambios a medio guardar y al día con GitHub, que la versión es mayor que la actual
   y que hay novedades anotadas.
2. Cambia la versión en `Directory.Build.props`.
3. Convierte `## [Sin publicar]` y su equivalente `[Unreleased]` en `## [1.1.0] - <fecha de hoy>` en ambos registros (y deja una sección vacía encima).
4. Guarda el cambio como «Versión 1.1.0», crea la etiqueta `v1.1.0` y lo sube todo a GitHub.

Al llegar la etiqueta, GitHub ejecuta [`release.yml`](../.github/workflows/release.yml), que en unos 10 minutos:

- compila y pasa todas las pruebas (si alguna falla, no se publica nada);
- crea la página de la versión en [Releases](https://github.com/cbatista96/MegaCopier/releases) con las novedades
  completas en inglés y español, extraídas de `CHANGELOG.en.md` y `CHANGELOG.md`, con el resumen al principio de cada idioma;
- adjunta `MegaCopier-Setup-1.1.0.exe`, `MegaCopier-1.1.0-portable.zip`, `mcopy-1.1.0.exe`, `SHA256SUMS.txt`, `release-notes.es.md`, `release-notes.en.md`, `update.json` y `update.sig`.

**Antes de publicar la primera versión con actualizador**, configura la pública del repositorio y la privada como secreto de Actions siguiendo [tools/Updates/README.md](../tools/Updates/README.md). El flujo se detiene si falta una clave o si la privada no corresponde a la pública de la app. No se generan ni publican claves automáticamente. `tools/Updates/notes.cs` extrae las notas completas; `sign.cs` firma el nombre, tamaño, versión, plataforma y SHA-256 del instalador y ZIP. La firma y el manifiesto no se modifican después.

El README enlaza siempre a la **última versión** (`releases/latest`), así que no hay que tocarlo.

Para prepararlo sin subir nada (y revisarlo antes): `.\tools\publicar-version.ps1 1.1.0 -SinSubir`, y después
`git push origin main v1.1.0`.

## 5. Si algo sale mal

- **Falla la compilación al publicar**: no se publica nada. Arregla el fallo, súbelo a `main` y publica la versión
  siguiente (`1.1.1`). No reutilices un número de versión que ya se ha subido.
- **Se publicó con un fallo grave**: publica cuanto antes un arreglo (`1.1.1`). Si hace falta, en la página de la versión
  se puede marcar como *pre-release* o borrarla, pero la etiqueta y el número no se reutilizan.

## 6. Ramas

- **`main`** es lo que se publica. Siempre debe compilar y pasar las pruebas.
- Para cambios grandes o de otras personas: una rama (`arreglo-ftp`, `idioma-catalan`…) y un *pull request* hacia `main`.
  La integración continua lo comprueba antes de juntarlo. Ver [CONTRIBUTING.md](../CONTRIBUTING.md).

## 7. Firmar el instalador (opcional)

La firma Authenticode identifica al editor y ayuda a acumular reputación, pero no garantiza que desaparezcan todos los avisos de SmartScreen. SignPath Foundation es una vía gratuita para proyectos abiertos, previa solicitud y aprobación. Estado real, requisitos e integración: [Firmas.md](Firmas.md). La firma ECDSA del manifiesto del actualizador es distinta y también debe configurarse antes de publicar.

### Resumen editorial y registro completo: instrucciones para la IA que publica

Antes de publicar, prepara `### Resumen` al principio de «Sin publicar» en `CHANGELOG.md` y `### Highlights` al principio de «Unreleased» en `CHANGELOG.en.md`. Ambos deben expresar las mismas novedades y mantener todos los detalles, enlaces e imágenes debajo, en sus apartados habituales. El cuerpo público de GitHub comienza con el resumen; los assets `release-notes.es.md` y `release-notes.en.md` conservan el documento completo.

**Presentación de idiomas en GitHub:** `README.md` enlaza al inicio a `README.en.md` y ambos historiales enlazan a su traducción. GitHub presenta el contenido del README y un único cuerpo de notas por release; no selecciona estos archivos por el idioma del visitante. `tools/Updates/notes.cs` genera además `release-body.md` con enlaces English / Español y las notas completas de ambos idiomas, primero inglés. El workflow lo mueve fuera de los assets y lo usa como cuerpo de la publicación. No se deben mezclar ambos idiomas dentro de cada asset de notas: MegaCopier elige `release-notes.es.md` para la interfaz española y `release-notes.en.md` para todas las demás. Las publicaciones históricas conservan su cuerpo original; las traducciones completas están en `CHANGELOG.en.md` y embebidas en la app.

**Subir código para revisión no es publicar una versión:** se puede guardar y subir a `main` con `[Sin publicar]` / `[Unreleased]`, capturas y traducciones, sin cambiar `Directory.Build.props`, crear etiquetas ni ejecutar `publicar-version.ps1`. CI compila y prueba también el instalador, pero no adjunta paquetes descargables de desarrollo. El workflow de release solo se activa con una etiqueta de versión autorizada.

**Selección editorial obligatoria:**

- Lee todos los cambios de la versión y elige las mejoras con mayor utilidad o impacto para quien usa MegaCopier. No copies mecánicamente los primeros elementos del registro ni detalles internos de implementación.
- Para una versión pequeña, normalmente bastan cuatro o cinco puntos. Si solo hay dos novedades reales, usa dos; no rellenes con cambios menores.
- Una actualización grande puede necesitar diez puntos o más. No hay un límite artificial de cinco o diez en la publicación ni en la app; añade puntos solo cuando representen novedades importantes y distintas.
- Si hay muchas correcciones menores, agrúpalas en un punto: por ejemplo, «Se corrigieron 10 errores de estabilidad y compatibilidad», **solo si puedes verificar esa cifra** en el registro. Cuenta problemas distintos, no líneas, commits ni traducciones. Si no conoces la cifra, no la inventes: describe el grupo sin número.
- Si una corrección tiene un impacto importante (pérdida de datos, copias interrumpidas, compatibilidad relevante), puede merecer un punto propio. No la ocultes en un grupo genérico.
- Si la actualización contiene únicamente correcciones, selecciona las cuatro o cinco más importantes, o menos si hay pocas. Agrupa el resto en un punto breve cuando aporte información útil.
- Desarrolla cada punto con una o dos frases naturales: explica qué ocurría o qué se añade, cómo cambia su uso y qué ventaja aporta. La referencia es un párrafo breve y claro, no un título de tres o cuatro palabras como «Recuperación de transferencias». Usa puntos cortos solo cuando la novedad realmente necesite poca explicación; no alargues con relleno ni traslades todo el detalle técnico del registro completo al resumen. Evita duplicar características. La versión inglesa debe conservar el significado, las cifras verificadas y el alcance.

Ejemplo para una versión con funciones nuevas y diez arreglos menores:

```markdown
### Resumen

- Si una transferencia se interrumpe, MegaCopier conserva lo que queda pendiente para que puedas continuar al volver a abrirlo, sin empezar de nuevo toda la copia.
- Puedes decidir si las actualizaciones se descargan con Wi-Fi y cable o también en redes móviles y de uso medido. La descarga respeta tu elección y queda lista para instalar.
- El historial de versiones se consulta dentro de Ajustes, con las novedades completas, enlaces e imágenes de cada actualización, sin tener que abrir el navegador.
- Se corrigieron 10 errores de estabilidad y compatibilidad que afectaban a las transferencias y a la navegación. Los arreglos se detallan individualmente en el registro completo.

### Añadido

[Cada cambio completo, con enlaces y capturas cuando corresponda.]

### Arreglado

[Las diez correcciones descritas individualmente; el resumen no sustituye esta lista.]
```

El ejemplo es una pauta editorial, no contenido para copiar sin contrastarlo con la versión real. No publiques los textos entre corchetes.

**Validación antes de publicar:** el script local y el flujo de GitHub exigen un resumen no vacío en ambos idiomas. Verifica además que no haya novedades importantes omitidas, cifras inventadas, puntos redundantes ni un listado largo de arreglos menores. La app muestra todos los puntos del resumen editorial, sin truncarlos a cinco; «Ver registro de cambios completo» abre el documento íntegro, con imágenes. Las versiones antiguas sin resumen mantienen una vista previa de hasta cinco puntos abreviados y su historial original. El registro de la versión instalada se muestra si no hay una nueva; al encontrar otra versión, se muestran su resumen y sus notas completas.

No publiques ni modifiques una release real para hacer una prueba. Usa el servidor local y claves efímeras descritos en `tools/Updates/README.md`.
