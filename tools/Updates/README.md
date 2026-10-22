# Firma de actualizaciones

El actualizador solo instala paquetes autorizados por una clave ECDSA P-256. SHA256SUMS por sí solo no sustituye esa firma.

## Estado de producción (21 de octubre de 2026)

La clave pública ya está en `tools/Updates/public-key.pem` y la privada se guardó como secreto de Actions `MEGACOPIER_UPDATE_PRIVATE_KEY` en `cbatista96/MegaCopier`. Huella SHA-256 de la clave pública DER:
`3330c979bc72779d35cdbbe0fd05c2d91a8700f62175ec47b430b28be1f2d771`.

La privada local está en `%USERPROFILE%\.megacopier\signing\update-private.pem`, fuera del repositorio, con acceso limitado al usuario y SYSTEM. `update-private.dpapi` es una copia cifrada con DPAPI para ese usuario de Windows; no sustituye una copia de recuperación independiente. Conserva la PEM en un almacén cifrado de recuperación y no la adjuntes a incidencias, mensajes ni publicaciones. No vuelvas a generar una clave para cada versión.

El secreto se guardó con éxito y se verificó su existencia mediante la API de GitHub; GitHub no permite leer el valor de un secreto ya guardado. La ejecución remota completa del flujo se comprobará en una publicación autorizada. La clave pública ya está publicada en el código; la próxima versión descargable debe incorporar también el actualizador y sus recursos.

## Procedimiento de configuración y recuperación

1. Genera las claves con `.NET 10`: `dotnet run tools/Updates/sign.cs -- create-key RUTA_FUERA_DEL_REPO/privada.pem tools/Updates/public-key.pem`.
2. Conserva la privada fuera del repositorio y una copia de recuperación. **No publiques ni adjuntes la privada.** El generador no sobreescribe archivos existentes.
3. Incluye solamente `tools/Updates/public-key.pem` en el código que vaya a publicarse. La app la incrusta al compilar. No sirve cambiar la pública después de distribuir una versión sin un procedimiento de transición.
4. En GitHub configura, cuando apruebes hacerlo, el secreto de Actions `MEGACOPIER_UPDATE_PRIVATE_KEY` con la privada PEM. Restringe el acceso a publicaciones. No hay un token del propietario dentro de MegaCopier.
5. Antes de publicar confirma que la privada del secreto corresponde a la pública incrustada. Firma un paquete de prueba y verifica su actualización en un entorno aislado.

El flujo de publicación comprueba automáticamente esa correspondencia y se detiene si falta una clave. Para firmar manualmente: `dotnet run tools/Updates/sign.cs -- sign X.Y.Z carpeta-paquetes ruta-privada.pem tools/Updates/public-key.pem`. El comando `dotnet run tools/Updates/notes.cs -- X.Y.Z carpeta-paquetes` extrae las dos traducciones; falla si falta alguna. El script habitual de publicar prepara ambos registros de cambios. No ejecutes dicho script para una simple prueba: crea commits y etiquetas.

La release debe contener `update.json`, `update.sig`, el instalador, el ZIP y `SHA256SUMS.txt`. El manifiesto se firma sobre sus bytes UTF-8 exactos, con SHA-256 y firma DER (Rfc3279DerSequence); no se reformatea después. Incluye esquema, versión, plataforma y nombre/tamaño/SHA-256 de cada paquete. Las notas se publican por separado en `release-notes.es.md` y `release-notes.en.md`.

Si falta la pública o la firma, la aplicación puede consultar versiones y mostrar novedades, pero **no ejecuta el paquete**. Esto es intencionado. La clave de producción ya está configurada; los paquetes deben incluir el manifiesto y su firma válida. No se ha publicado una versión nueva durante esta configuración.

La firma del manifiesto no es una firma Authenticode del ejecutable y no evita los controles de Windows. Si se pierde o compromete la clave, no se debe sustituirla sin diseñar y probar la transición de confianza.

## Pruebas sin publicación

Las pruebas unitarias usan claves efímeras y un servidor en `127.0.0.1`. Para la aplicación se admite `MEGACOPIER_TEST_UPDATE_URL=http://127.0.0.1:PUERTO/latest` y `MEGACOPIER_TEST_UPDATE_KEY=ruta-publica-prueba.pem` únicamente junto a `MEGACOPIER_TEST=1`. No se admiten otros hosts ni redirecciones fuera del puerto de prueba. `MEGACOPIER_TEST_UPDATE_INSTALLED=1` simula el modo instalado, pero el botón de instalar **solo registra la petición** y nunca ejecuta el archivo de prueba.

No pruebes el instalador real sobre una instalación de trabajo sin autorización específica. Conserva los datos, tareas e integraciones y verifica la reapertura de la app.

`MEGACOPIER_TEST_DATA_DIRECTORY` permite una carpeta absoluta de datos aislados solo con `MEGACOPIER_TEST=1`. `MEGACOPIER_TEST_UPDATE_NETWORK=wifi|mobile|offline` simula las decisiones de red sin tocar la configuración de Windows. `MEGACOPIER_TEST_UPDATE_NOTIFICATIONS=1` habilita la notificación nativa en ese proceso de prueba; por defecto se registra solo su simulación local. Ninguna de estas variables altera la versión publicada.

## Notificaciones y distribución autónoma

Foundation 2.3.12 presenta un fallo conocido al registrar avisos sin el recurso de versión del runtime ([Microsoft #6774](https://github.com/microsoft/WindowsAppSDK/issues/6774)). El proyecto obtiene **solo** `Microsoft.WindowsAppRuntime.Insights.Resource.dll` (34 KB) del paquete oficial Runtime 2.5.1 y lo incluye junto al exe. No cambia las versiones de WinUI, no instala paquetes MSIX ni añade los componentes de IA. La restauración de NuGet obtiene el paquete firmado original; la extracción usa la tarea Unzip de MSBuild. Conserva esta dependencia hasta comprobar una corrección oficial de Foundation con pruebas de registro y activación en una máquina limpia.

## Pruebas de conexión y resumen editorial

Solo con `MEGACOPIER_TEST=1`, el archivo `test-update-network.txt` dentro de la carpeta de datos aislados puede contener `offline`, `wifi`, `ethernet`, `wifi-metered`, `ethernet-metered` o `mobile`. La orden interna `debug-update-tick` vuelve a evaluar esa política y adelanta únicamente el intervalo entre intentos de prueba; no el periodo de inactividad de instalación. No modifica la red de Windows.

El resumen editorial no tiene un límite de cinco ni diez puntos. Sigue la selección y agrupación de correcciones de `docs/Versiones.md`. Las notas completas nunca se sustituyen por ese resumen. La publicación rechaza resúmenes vacíos en cualquiera de los dos idiomas.
