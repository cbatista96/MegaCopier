# Compilar MegaCopier

Todo se compila desde la línea de comandos, sin abrir Visual Studio. Lo mismo hace la integración continua de GitHub en
cada cambio (`.github/workflows/ci.yml`).

## Requisitos

Windows 11 de 64 bits y, todo gratis:

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Microsoft.VisualStudio.2022.BuildTools --override "--quiet --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
winget install JRSoftware.InnoSetup
```

- **.NET 10 SDK**: la app, el motor y la consola (la versión exacta está en `global.json`).
- **Herramientas de compilación de C++** (con el SDK de Windows): la extensión del Explorador, en C++, y la compilación
  nativa (Native AOT) de la app y de la consola.
- **Inno Setup 6**: el instalador. Sin él se compila todo menos el instalador.

## Compilar

```powershell
.\build.ps1             # textos + pruebas + app + extensión del Explorador + consola + instalador
.\build.ps1 -SkipTests  # lo mismo sin las pruebas
```

Resultado en `artifacts\`:

| Archivo | Qué es |
|---|---|
| `app\MegaCopier.exe` | La app, lista para ejecutarse sin instalar. |
| `app\MegaCopierShell-<versión>.dll` y `MegaCopierShell.msix` | Extensión del Explorador y paquete del menú corto de Windows 11. |
| `cli\mcopy.exe` | La consola. |
| `installer\MegaCopier-Setup-<versión>.exe` | El instalador. |

Para la versión portátil basta con copiar `artifacts\app` y `mcopy.exe` a una carpeta y crear en ella un archivo vacío
`portable.txt`: los ajustes se guardan entonces en `Data\`, al lado.

## Pruebas

```powershell
dotnet test tests/MegaCopier.Engine.Tests
```

Más de 300 pruebas del motor: cortes simulados en cada punto crítico, journal dañado, conflictos, sincronización,
servidores FTP/WebDAV/S3, nubes y móviles de prueba (todo en `127.0.0.1`, sin internet), formatos ZIP y 7z (comprobados
con lectores independientes), datos de recuperación, copias de seguridad y más.

La app se prueba sin tocar el equipo con `MEGACOPIER_TEST=1`: no instala el atajo de Ctrl+V, no escribe en el registro, no
usa el Administrador de credenciales ni la Papelera, y solo escucha en `127.0.0.1`. Se maneja por IPC (`mcopy send`) y
UI Automation.

## Textos e idiomas

Todos los textos están en `tools/Strings`: `strings.tsv` (la app), `engine.tsv` (el motor y `mcopy`) y `shell.tsv` (el
menú del Explorador), con el español y el inglés, y en `i18n/<idioma>[.engine|.shell].tsv` los demás idiomas.
`build.ps1` genera con ellos los `.resw` de la app, `Texts.g.cs` y `Texts.g.h`; no se editan a mano. Para añadir un
idioma: una línea en `languages.tsv` y sus tres archivos en `i18n/`. El generador comprueba que no falte ninguna clave y
que los huecos `{0}` coincidan.

## Versiones

La versión está en `Directory.Build.props` (sigue [SemVer](https://semver.org/lang/es/)). Para publicar una:

1. Sube la versión en `Directory.Build.props` y anota los cambios en `CHANGELOG.md`.
2. Crea la etiqueta y súbela:

   ```powershell
   git tag v1.2.0
   git push origin v1.2.0
   ```

3. GitHub compila, prueba y publica la versión con el instalador, el ZIP portátil, `mcopy.exe` y sus sumas SHA-256
   (`.github/workflows/release.yml`).

## Firmar (opcional)

Sin firma, Windows muestra el aviso de SmartScreen. Con un certificado de firma de código:

- Instalador y ejecutables: `signtool sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /f cert.pfx ...`
  (o Azure Trusted Signing).
- Paquete del menú corto de Windows 11: `build.ps1` lo firma si existen `MEGACOPIER_SIGN_PFX`, `MEGACOPIER_SIGN_PASSWORD`
  y `MEGACOPIER_SIGN_PUBLISHER` (el sujeto del certificado). Sin ellas se publica sin firmar, que Windows 11 admite.

## Arquitectura

| Proyecto | Qué contiene |
|---|---|
| `src/MegaCopier.Engine` | El motor: copia con diario, E/S, conflictos, cola, sincronización, servidores y nubes, formatos, IPC. |
| `src/MegaCopier.App` | La app WinUI 3 (Windows App SDK, sin MSIX, Native AOT): proceso residente, bandeja, ventanas y ajustes. |
| `src/MegaCopier.Shell` | Integración con el Explorador desde la app: Ctrl+V, portapapeles, carpetas abiertas, registro de la extensión. |
| `src/MegaCopier.ShellExt` | Extensión del Explorador en C++: menús contextuales y arrastre con el botón derecho. |
| `src/MegaCopier.Cli` | La consola `mcopy` (y el servidor MCP). |
| `tests/MegaCopier.Engine.Tests` | Las pruebas del motor. |
| `installer/MegaCopier.iss` | El instalador Inno Setup, por usuario. |
| `tools/` | Generadores del icono y de los textos. |

La app es de instancia única por usuario: el primer proceso queda residente y los demás (la extensión, `mcopy`, otro
`MegaCopier.exe`) le entregan la petición por una tubería con nombre propia del usuario.
