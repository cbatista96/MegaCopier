# Seguridad

[English](SECURITY.md) · **Español**

## Informes de errores y vulnerabilidades

Los informes opcionales envían cierres inesperados y datos técnicos filtrados mediante Cloudflare a un repositorio privado de GitHub. Puedes elegir durante la instalación o en Ajustes > Avanzado. Si el envío automático está desactivado, el diálogo ofrece vista previa y envío de una sola vez.

**Un informe de fallo no es una auditoría de seguridad.** No detecta automáticamente todas las vulnerabilidades ni sustituye los pasos para reproducir un problema de seguridad.

## Comunicar una vulnerabilidad

No publiques credenciales, archivos privados ni detalles de explotación en incidencias públicas. Usa [Report a vulnerability](https://github.com/cbatista96/MegaCopier/security/advisories/new), el formulario privado de GitHub. Si no está disponible, escribe a **contact@worldmarketinglabs.com**. Incluye versión, pasos para reproducir y posible impacto, con los datos personales eliminados. Los arreglos se aplican a la última versión mantenida; no se promete un plazo de respuesta concreto.

## Protecciones y límites

- Credenciales de servidores y nubes en el Administrador de credenciales de Windows.
- Las transferencias entre equipos usan la protección de su protocolo; LocalSend conserva su propio protocolo.
- Control móvil limitado a la red local y protegido por código.
- Clave forense protegida mediante DPAPI para el usuario de Windows.
- La versión en desarrollo verifica las actualizaciones con manifiesto ECDSA y SHA-256. La firma Authenticode sigue pendiente de aprobación de SignPath Foundation.
- La búsqueda automática consulta GitHub cada 48 horas cuando está habilitada. Los informes automáticos opcionales excluyen archivos, nombres, rutas y mensajes libres. No se afirma que la aplicación carezca de toda conexión automática.

Consulta la [privacidad](docs/Privacidad.md), la [política de firma](docs/Code-signing-policy.md) y la [documentación del receptor](tools/informes-worker/README.md) para conocer proveedores y límites.
