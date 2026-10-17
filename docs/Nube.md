# OneDrive, Google Drive y Dropbox en MegaCopier

MegaCopier copia a y desde OneDrive, Google Drive y Dropbox con su propio inicio de sesión: se abre el navegador, entras
con tu cuenta y aceptas. MegaCopier nunca ve tu contraseña; solo guarda un "token de renovación" en el Administrador de
credenciales de Windows, igual que las contraseñas de los servidores FTP o WebDAV.

Cada servicio exige que el programa esté **registrado** una vez (es gratis). Quien distribuye MegaCopier lo registra y
pone el identificador en la versión publicada; si usas una versión propia, regístralo tú siguiendo estos pasos. En todos
los casos la dirección de vuelta es `http://127.0.0.1` (MegaCopier escucha solo en este equipo, en un puerto libre).

## OneDrive (Microsoft)

1. Entra en <https://entra.microsoft.com> > **Aplicaciones** > **Registros de aplicaciones** > **Nuevo registro**.
2. Nombre: `MegaCopier`. Tipos de cuenta: **Cuentas de cualquier directorio organizativo y cuentas personales de Microsoft**.
3. URI de redirección: plataforma **Cliente público/nativo (móvil y escritorio)**, valor `http://localhost`.
4. En **Permisos de API** añade **Microsoft Graph > Permisos delegados > Files.ReadWrite** y **offline_access**.
5. Copia el **Id. de aplicación (cliente)**: es el *Identificador de la app* que pide MegaCopier.

## Google Drive

1. Entra en <https://console.cloud.google.com>, crea un proyecto y activa **Google Drive API**.
2. **Pantalla de consentimiento de OAuth**: tipo *Externo*, añade el permiso `.../auth/drive` y, mientras esté en pruebas,
   tu cuenta como usuario de prueba.
3. **Credenciales** > **Crear credenciales** > **ID de cliente de OAuth** > tipo **App de escritorio**.
4. Copia el **ID de cliente** y el **Secreto del cliente** (Google lo pide aunque en una app instalada no es secreto).

## Dropbox

1. Entra en <https://www.dropbox.com/developers/apps> > **Create app** > **Scoped access** > **Full Dropbox**.
2. En **Permissions** marca `files.metadata.read`, `files.content.read` y `files.content.write`, y pulsa **Submit**.
3. En **Settings** añade la URI de redirección `http://127.0.0.1` (Dropbox acepta cualquier puerto en 127.0.0.1).
4. Copia la **App key**: es el *Identificador de la app*.

## Usarlo

Ajustes > Conexiones > **Añadir** > tipo OneDrive, Google Drive o Dropbox > pega el identificador > **Iniciar sesión en el
navegador** > acepta en la página del servicio > **Guardar**. Después la nube aparece en "Nueva > Copiar desde un móvil,
cámara o servidor…" y como destino, como cualquier servidor.

Detalles:

- Los archivos grandes se suben por partes (OneDrive en trozos de 10 MB, Dropbox de 8 MB, Google en una subida
  reanudable), y MegaCopier ajusta solo cuántos sube a la vez.
- Los documentos de Google (Docs, Hojas, Presentaciones) no son archivos descargables tal cual y no se muestran.
- Lo que MegaCopier borra en la nube va a la papelera del servicio, no se elimina para siempre.
