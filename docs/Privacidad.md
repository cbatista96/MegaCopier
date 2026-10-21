# Política de privacidad

Actualizada el 21 de octubre de 2026. Proyecto: [MegaCopier](https://github.com/cbatista96/MegaCopier), mantenido por [cbatista96](https://github.com/cbatista96). Contacto: contact@worldmarketinglabs.com.

## Archivos y datos locales

Las copias se realizan entre los destinos y servicios elegidos por el usuario. MegaCopier no envía los archivos copiados al responsable del proyecto. Los ajustes, historial, trabajos pendientes y registros se guardan en `%LOCALAPPDATA%\MegaCopier`, o en `Data` junto a la aplicación portátil. Los registros locales pueden contener nombres y rutas; no se adjuntan automáticamente a los informes de errores.

## Actualizaciones y novedades

La búsqueda automática viene marcada en el instalador y puede desactivarse allí o en Ajustes. Consulta GitHub cuando han transcurrido 48 horas desde la última comprobación correcta; una comprobación vencida puede realizarse al recuperar Internet. También se puede buscar manualmente. GitHub recibe la IP de conexión y un User-Agent con la versión de la aplicación. La consulta no envía nombres, historial de copias ni contenido de archivos.

Las descargas y las imágenes remotas de las novedades utilizan servicios de GitHub. El último resultado y las novedades se guardan localmente. Descargar e instalar automáticamente vienen desactivados y tienen controles separados. Las descargas respetan los permisos de red elegidos.

## Informes de errores opcionales

El instalador interactivo ofrece **Enviar informes de errores**, marcado por defecto; puede desmarcarse antes de instalar. También puede cambiarse en Ajustes. Si está activado, los informes autorizados se envían en segundo plano. Si está desactivado, tras un cierre inesperado se puede revisar un informe y decidir si enviarlo; elegir no enviarlo no transmite ese informe. Marcar **Enviar siempre sin preguntar** activa el envío automático. Desactivar el envío borra su cola pendiente.

El informe contiene un identificador aleatorio de ese informe, fecha, versiones de la aplicación y Windows, idioma, modo, tema, categoría general de actividad, tipos de errores y nombres de funciones filtrados y, si están disponibles, módulo nativo que falló, código y desplazamiento. Una huella agrupa fallos repetidos. Excluye contenido de archivos, nombres y rutas de archivos del usuario, mensajes libres de excepciones e identificadores permanentes del equipo. Un apagón también puede generar una marca de cierre inesperado. La vista previa de la aplicación muestra el JSON que se enviará.

Se envía por HTTPS a través de un Worker de Cloudflare al repositorio **privado** de informes del responsable. Cloudflare recibe necesariamente la IP; el Worker deriva un valor HMAC para limitar abusos y no incorpora la IP al informe ni a la incidencia de GitHub. Solo el responsable y las personas que autorice expresamente para ese repositorio privado pueden leer las incidencias. La aplicación no contiene el token del repositorio.

La aplicación conserva como máximo 20 informes pendientes autorizados, descarta los de más de 14 días al procesar la cola y guarda huellas de entregas durante 90 días. El receptor conserva recibos de entrega 14 días, cuotas 30 días e índices de agrupación hasta que el responsable los retire. Las incidencias privadas de GitHub **no tienen actualmente un plazo de borrado automático**: se mantienen hasta que el responsable las elimine. Para solicitar un borrado o consultar un informe, escribe a contact@worldmarketinglabs.com indicando, si lo tienes, su identificador. No envíes archivos privados ni registros sin filtrar por correo salvo acuerdo separado.

## Otras funciones de red

Las copias de red, nubes, control remoto y descubrimiento de dispositivos dependen de las funciones y destinos que el usuario active o seleccione. Los datos necesarios se envían a esos destinos. Abrir enlaces o crear manualmente una incidencia pública en GitHub utiliza el navegador y los servicios elegidos; la información de una incidencia pública es pública.

## Proveedores y cambios

GitHub y Cloudflare tratan datos de conexión y servicio según sus políticas: [privacidad de GitHub](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement) y [privacidad de Cloudflare](https://www.cloudflare.com/privacypolicy/). Ese tratamiento es distinto del contenido filtrado de un informe de MegaCopier.

Los cambios futuros en recogida o transmisión de datos deben reflejarse aquí y en las explicaciones del instalador y Ajustes. [English version](Privacy.md).
