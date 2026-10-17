# Seguridad

## Versiones con soporte

Se corrigen los fallos de seguridad en la última versión publicada.

## Avisar de un fallo de seguridad

**No abras una incidencia pública.** Usa el aviso privado de GitHub:
[Report a vulnerability](https://github.com/cbatista96/MegaCopier/security/advisories/new) (pestaña *Security* del
repositorio). Incluye la versión, los pasos para reproducirlo y su posible efecto.

Se responderá lo antes posible; una vez corregido, se publicará una versión nueva y el aviso, reconociendo tu ayuda si lo deseas.

## Qué protege MegaCopier

- Contraseñas y tokens de servidores y nubes: en el Administrador de credenciales de Windows, nunca en archivos.
- Envío entre equipos: cifrado con TLS y con un código de un solo uso; LocalSend sigue su propio protocolo.
- Control desde el móvil: solo en la red local, con código, y bloqueo tras 10 códigos erróneos.
- Clave del modo forense: protegida con DPAPI (solo tu usuario de Windows puede usarla).
- Sin telemetría: MegaCopier no se conecta a internet salvo a lo que configures tú.
