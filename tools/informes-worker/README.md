# Receptor privado de informes de MegaCopier

Este servicio es opcional. Receptor desplegado el 21 de octubre de 2026:
`https://megacopier-informes.informes-worker.workers.dev/report`, con destino exclusivo al repositorio privado
`cbatista96/MegaCopier-informes`. `Endpoint` está configurado en `src/MegaCopier.App/Services/CrashReporter.cs`.
Los secretos se guardan cifrados únicamente en Cloudflare. El token granular de Issues caduca el **19 de enero de 2027**:
debe renovarse antes de esa fecha y sustituirse en `GITHUB_TOKEN`. No se incluyó ningún secreto en este proyecto.

## Qué recibe y dónde lo guarda

`POST /report` recibe exclusivamente el contrato de `CrashReport`, con un cuerpo inferior a 64.000 bytes.
Valida campos, tipos, trazas filtradas y huella SHA-256 antes de aceptar. La app no adjunta el registro sin filtrar,
archivos, nombres, rutas ni un identificador permanente del equipo. Los mensajes libres de las excepciones se descartan.
La vista previa de la app muestra el mismo JSON que se transmite.

Cloudflare recibe necesariamente la IP de la conexión. El Worker la transforma mediante HMAC con un secreto para
limitar peticiones; no la incluye en el informe, en el objeto duradero ni en GitHub. La observabilidad del Worker está
desactivada y el código no imprime cuerpos ni secretos. Esto no equivale a que los proveedores no traten datos de red.

El objeto duradero SQLite serializa entregas, cuotas y deduplicación. Mantiene recibos durante 14 días, cuotas durante
30 días y el índice de grupos hasta su eliminación por el propietario. Los informes se guardan como incidencias en un
repositorio **privado**: no se borran automáticamente de GitHub. El propietario debe definir y comunicar su plazo de
conservación antes de activar el servicio. Solo él y las personas a las que dé acceso podrán leer esas incidencias.

## Límites y fallos

- App: cinco intentos de envío al día, incluyendo errores; cola máxima de 20 informes autorizados, caducidad de 14 días
  al procesarla y huellas de entregas conservadas 90 días. El mismo fallo y versión se deduplican.
- Worker: ocho peticiones por minuto por clave derivada de la IP. Es el límite aproximado por ubicación de Cloudflare,
  no una garantía global. Además, el objeto duradero impone 2.000 intentos diarios y 50 incidencias nuevas diarias.
- Fallos repetidos: una incidencia por huella, como máximo un comentario agrupado al día. Cada ID de envío tiene recibo
  persistente para que un reintento no duplique una entrega ya confirmada.
- Comprueba que el repositorio sea privado antes de escribir. Si es público o no se puede verificar, responde 503.
- Sin token, secreto, permisos, cuota o conexión, no confirma la entrega y la app conserva el informe autorizado.
- No hay un secreto de autenticación dentro de la app. Un cliente modificado podría enviar informes ficticios que
  respeten el contrato; las cuotas contienen el abuso, pero no certifican que el fallo sea auténtico.

## Configuración y renovación por el propietario

La primera configuración ya se completó con autorización del propietario. Los siguientes pasos documentan cómo
reproducirla o renovarla. La incidencia privada #1 contiene un informe totalmente ficticio de comprobación y está cerrada.

1. Crear una cuenta de Cloudflare y un repositorio **privado**, por ejemplo `cbatista96/MegaCopier-informes`.
   Activar Issues. No utilizar el repositorio público del programa como receptor.
2. Crear un token de GitHub de acceso granular limitado a ese único repositorio, con **Issues: lectura y escritura**
   y **Metadata: lectura**. Elegir caducidad y custodiarlo fuera del código. No conceder permisos de código o de cuenta.
3. Instalar Node.js LTS. Usar Wrangler 4.36.0 o posterior (necesario para este binding de límite por IP).
   Desde esta carpeta ejecutar `npx wrangler login` para iniciar sesión personalmente.
4. Revisar `wrangler.toml`: nombre del Worker, propietario y repositorio. El binding `INBOX` y su migración SQLite son
   necesarios para la deduplicación. `REPORT_RATE_LIMITER` debe usar un `namespace_id` que no se comparta con otro
   limitador del mismo proyecto. No activar observabilidad con cuerpos de informes.
5. Guardar el token **solo** con `npx wrangler secret put GITHUB_TOKEN`. No escribirlo en el TOML, la app o GitHub.
6. Generar localmente un secreto aleatorio largo (al menos 32 bytes) y guardarlo con
   `npx wrangler secret put RATE_LIMIT_SALT`. No reutilizar el token de GitHub para este secreto.
7. Revisar los límites gratuitos de la cuenta y ejecutar `npx wrangler deploy`. SQLite Durable Objects está disponible
   en el plan gratuito; las cuotas son finitas y pueden cambiar. Este proyecto no activa un plan de pago.
8. Comprobar con un informe ficticio que la incidencia aparece en el repositorio privado; probar también token inválido,
   repositorio público, duplicado, límite y desconexión. Las pruebas locales no sustituyen esta comprobación del despliegue.
9. Aprobar el texto de privacidad y el plazo de conservación. Solo entonces poner
   `https://<nombre>.<cuenta>.workers.dev/report` en la constante `Endpoint` de `CrashReporter.cs`, compilar y probar
   el consentimiento en la app. El instalador ofrece el envío automático marcado en instalaciones nuevas, con su
   explicación y la posibilidad de desmarcarlo; las actualizaciones conservan la elección. El modo portátil comienza
   con el envío desactivado.
10. Para retirar el servicio, vaciar `Endpoint` en la siguiente compilación y revocar el token del Worker. Decidir qué
    hacer con las incidencias ya recibidas y la base del objeto duradero. Desactivar la opción en la app borra sus
    pendientes, pero no puede retirar informes que el servidor ya recibió.

No publicar claves, volcar variables de entorno ni enviar informes reales durante estas comprobaciones.
Una respuesta `{"accepted":true}` solo se devuelve tras confirmar el destino privado y completar la entrega.
Las consultas a GitHub usan `redirect: 'manual'`: cualquier respuesta 3xx se rechaza sin reenviar el token.
Cloudflare Workers no admite el modo `redirect: 'error'`, aunque Node.js sí lo admita.

## Pruebas locales

Desde la raíz, con Node.js 24 o posterior:

```powershell
node --test tools/informes-worker/worker.test.mjs
dotnet test tests/MegaCopier.Engine.Tests --filter FullyQualifiedName~CrashReportTests
```

Las pruebas del Worker usan SQLite real en memoria y un GitHub simulado, sin peticiones a Internet. Las del cliente
usan únicamente `127.0.0.1`. `MEGACOPIER_TEST=1` permite `MEGACOPIER_TEST_REPORT_URL=http://127.0.0.1:<puerto>/report`
y `MEGACOPIER_TEST_DATA_DIRECTORY=<carpeta absoluta de prueba>`. Fuera de ese modo se ignoran estas opciones.
El comando IPC `debug-report-crash` solo existe en modo de prueba y sin trabajos o diálogos pendientes: provoca una
excepción real controlada para verificar el siguiente arranque. Nunca se debe usar en una sesión con datos reales.
`debug-report-open` abre la sección y `debug-report-status` consulta su estado.

Referencias oficiales: [límites por IP](https://developers.cloudflare.com/workers/runtime-apis/bindings/rate-limit/),
[plan y cuotas de Durable Objects](https://developers.cloudflare.com/durable-objects/platform/pricing/),
[versiones de la API de GitHub](https://docs.github.com/en/rest/about-the-rest-api/api-versions),
[consulta de eventos nativos de Windows](https://learn.microsoft.com/en-us/windows/win32/api/winevt/nf-winevt-evtquery).
