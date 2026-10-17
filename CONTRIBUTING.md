# Cómo contribuir a MegaCopier

¡Gracias por querer ayudar! Toda ayuda cuenta: avisar de un fallo, proponer una idea, mejorar una traducción o enviar código.

## Avisar de un fallo o proponer una idea

Abre una [incidencia (issue)](https://github.com/cbatista96/MegaCopier/issues/new/choose) con la plantilla que corresponda.
En un fallo, indica la versión de MegaCopier y de Windows, los pasos para reproducirlo y, si lo hay, el contenido de
`%LOCALAPPDATA%\MegaCopier\crash.log`. Revisa antes que no exista ya una incidencia igual.

Los fallos de seguridad **no** se cuentan en una incidencia pública: sigue [SECURITY.md](SECURITY.md).

## Traducciones

Los textos están en `tools/Strings` (ver [Compilar.md](docs/Compilar.md#textos-e-idiomas)). Para corregir un idioma, edita
su archivo en `tools/Strings/i18n/` y envía los cambios. Para uno nuevo, abre antes una incidencia.

## Enviar código

1. Haz un *fork* y crea una rama desde `main` con un nombre claro (`arreglo-conflictos-ftp`, `idioma-catalan`...).
2. Compila y pasa las pruebas: `.\build.ps1` (ver [Compilar.md](docs/Compilar.md)).
3. Añade pruebas para lo que cambies en el motor. Las de red usan solo `127.0.0.1` y servidores de prueba, nunca internet.
4. Si el cambio se nota al usar MegaCopier, añade una línea en `## [Sin publicar]` de [CHANGELOG.md](CHANGELOG.md), en su
   apartado (*Añadido*, *Cambiado*, *Arreglado*…), escrita para quien usa el programa. Ver [Versiones.md](docs/Versiones.md).
5. Abre un *pull request* explicando qué cambia y por qué. La integración continua tiene que pasar.

Las versiones las publica quien mantiene el proyecto con `tools\publicar-version.ps1` ([cómo](docs/Versiones.md)).

### Estilo

- Sigue el estilo del código que tocas; el idioma del código, los comentarios y los mensajes de commit es el español.
- Ningún texto visible se escribe en el código: va a `tools/Strings`, con su traducción al menos al inglés.
- La app tiene que seguir compilándose con Native AOT sin avisos (`IL2026`, `IL3050`...): nada de reflexión ni de
  serialización JSON sin generador de código.
- MegaCopier no envía nada a internet por su cuenta: nada de telemetría ni conexiones que el usuario no haya pedido.

## Licencia

Al contribuir aceptas que tu aportación se publique con la licencia del proyecto, la [GNU GPL v3](LICENSE).
