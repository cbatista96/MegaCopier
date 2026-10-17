# MegaCopier: normas del proyecto

Copiador de archivos para Windows 11 (WinUI 3 sin MSIX, .NET 10, Native AOT). Código abierto con licencia GPL v3 en
https://github.com/cbatista96/MegaCopier.

## Al hacer cambios

- **Cada cambio que se note al usar MegaCopier se anota en `## [Sin publicar]` de `CHANGELOG.md`**, en el mismo commit,
  en su apartado (`### Añadido`, `### Cambiado`, `### Arreglado`, `### Quitado`, `### Seguridad`), escrito para quien
  usa el programa. Si cambia algo que describe la documentación (`README.md`, `docs/`), se actualiza también.
- Código, comentarios, mensajes de commit y documentación en español. Ningún texto visible en el código: va a
  `tools/Strings` (TSV) con su traducción, y `build.ps1` genera los recursos.
- La app y `mcopy` deben compilar con Native AOT sin avisos (`IL2026`, `IL3050`…).
- Pruebas: `dotnet test tests/MegaCopier.Engine.Tests`; todo: `.\build.ps1`. Las pruebas de red usan solo `127.0.0.1`.
- Las capturas de `docs/capturas` van sin bordes negros (recortadas al marco visible) y sin rutas personales.
- Antes de subir a `main`, la compilación y las pruebas tienen que pasar. GitHub las repite en cada subida (`ci.yml`).

## Publicar una versión

`.\tools\publicar-version.ps1 x.y.z` (versionado semántico). Detalles en `docs/Versiones.md`. La versión está solo en
`Directory.Build.props`. Una etiqueta ya subida no se mueve ni se reutiliza: si algo falla, se publica la siguiente.
