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
3. Convierte `## [Sin publicar]` en `## [1.1.0] - <fecha de hoy>` (y deja una `[Sin publicar]` nueva y vacía encima).
4. Guarda el cambio como «Versión 1.1.0», crea la etiqueta `v1.1.0` y lo sube todo a GitHub.

Al llegar la etiqueta, GitHub ejecuta [`release.yml`](../.github/workflows/release.yml), que en unos 10 minutos:

- compila y pasa todas las pruebas (si alguna falla, no se publica nada);
- crea la página de la versión en [Releases](https://github.com/cbatista96/MegaCopier/releases) con las novedades
  copiadas de `CHANGELOG.md`;
- adjunta `MegaCopier-Setup-1.1.0.exe`, `MegaCopier-1.1.0-portable.zip`, `mcopy-1.1.0.exe` y `SHA256SUMS.txt`.

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

Sin firma, Windows muestra el aviso de SmartScreen al instalar. Para quitarlo hace falta un certificado de firma de código
(por ejemplo, Azure Trusted Signing, de pago mensual). Cómo firmar: [Compilar.md](Compilar.md#firmar-opcional).
