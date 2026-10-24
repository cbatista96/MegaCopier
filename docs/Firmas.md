# Firmas del actualizador y de Windows

Estado revisado el 21 de octubre de 2026. Son dos verificaciones diferentes; Cloudflare se utiliza para los informes de errores y no proporciona ninguna de estas firmas.

## Firma del manifiesto de actualización

MegaCopier comprueba `update.json` y `update.sig` con ECDSA P-256 y verifica el SHA-256 del paquete antes de ejecutarlo. La pareja de claves la genera y custodia el propietario; GitHub Actions guarda la privada como secreto y firma cada publicación. GitHub no emite aquí un certificado para Windows.

La clave de producción está configurada desde el 21 de octubre de 2026:

- Pública: `tools/Updates/public-key.pem`, incorporada a las nuevas compilaciones de la app.
- Privada: secreto `MEGACOPIER_UPDATE_PRIVATE_KEY` de Actions en `cbatista96/MegaCopier`; nunca se incluye en la app ni en Git.
- Huella SHA-256 de la pública DER: `3330c979bc72779d35cdbbe0fd05c2d91a8700f62175ec47b430b28be1f2d771`.
- Custodia local y recuperación: [tools/Updates/README.md](../tools/Updates/README.md).

La carga del secreto se realizó correctamente y su nombre quedó confirmado por GitHub. No se ha publicado una versión nueva ni ejecutado el flujo de release remoto. La pública y los cambios del actualizador deben estar incluidos en el código de la próxima publicación. Las pruebas locales firman y verifican paquetes sin subirlos; las claves de prueba anteriores no se utilizan para publicar. No se sustituye una clave distribuida sin diseñar una transición de confianza.

## Firma Authenticode: SignPath Foundation

El EXE y el instalador actuales devuelven `NotSigned` al comprobar su firma Authenticode. La firma del manifiesto no cambia este resultado.

[SignPath Foundation](https://signpath.org/apply) ofrece una solicitud de firma gratuita para proyectos abiertos, sujeta a aprobación. Sus [condiciones](https://signpath.org/terms) piden licencia OSI, mantenimiento, publicaciones y documentación; también verifican reputación y control del proyecto. Exigen MFA, funciones de revisión/aprobación, política de firma y privacidad, builds verificables y aprobación manual de cada release. No se debe anunciar el patrocinio como concedido antes de recibirlo. La recogida de informes debe describirse en la política de privacidad, mostrarse durante la instalación y poder desactivarse.

MegaCopier dispone de repositorio público, licencia GPL v3 y publicaciones; esto permite preparar la solicitud, pero no garantiza su aceptación. El propietario confirmó sus datos y autorizó publicar únicamente la clave pública y las políticas de firma y privacidad, disponibles en el commit `704cdc1086b28c1c711aceae7650d73e79a68f5a`. Solicitud enviada el 21 de octubre de 2026 con los consentimientos autorizados. Tras resolver el propietario el CAPTCHA, SignPath confirmó «Form submitted» y «Thank you, we'll be in touch soon». El 22 de octubre de 2026 SignPath comunicó que no aprobaba la solicitud por falta de señales de adopción y visibilidad pública; permite volver a solicitarla cuando el proyecto tenga mayor reconocimiento. Esta recepción no equivale a aprobación ni a un certificado activo, y no se han concedido accesos a SignPath. La revisión de componentes distribuidos y los requisitos de cuenta deben completarse antes de activar la firma.

### Preparación concreta de la integración

1. Presentar el repositorio `cbatista96/MegaCopier`, la documentación y sus releases; completar los datos reales del responsable y esperar la aprobación.
2. Configurar proyecto, política y permisos de SignPath con los identificadores reales que facilite el servicio. No inventar IDs ni guardar tokens en el código.
3. Ajustar el flujo según la [integración oficial con GitHub](https://docs.signpath.io/trusted-build-systems/github): los artefactos se generan y cargan desde runners alojados en GitHub; el servicio comprueba su origen y devuelve los artefactos firmados tras aprobarlos.
4. Firmar únicamente los binarios propios: app, consola y extensión del Explorador. Las DLL de Microsoft u otros terceros conservan sus firmas originales.
5. Construir el instalador con esos binarios y firmar también el instalador. Revisar por separado la configuración de firma del desinstalador de Inno Setup y, si se firma el paquete de menú MSIX, su identidad `Publisher`.
6. Preparar el ZIP portátil con los archivos firmados. **Después** calcular las sumas y firmar el manifiesto ECDSA sobre los paquetes definitivos. Cualquier firma posterior cambia los bytes y obliga a recalcular sumas y manifiesto.
7. Comprobar firmas y sellos de tiempo, instalación limpia, actualización silenciosa, reapertura e integración del Explorador en un equipo de prueba antes de publicar.

### SmartScreen

Firmar identifica al editor y permite acumular reputación, pero no garantiza que desaparezcan todos los avisos. Microsoft explica que incluso un binario firmado puede aparecer como desconocido y que los certificados EV ya no conceden reputación inmediata por defecto. Consulta [la documentación actual de SmartScreen](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation). No prometer «sin SmartScreen» como resultado automático de la aprobación de SignPath.
