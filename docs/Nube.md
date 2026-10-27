# Nubes en MegaCopier: OneDrive, SharePoint, Google Drive, Dropbox, Azure y Google Cloud Storage

MegaCopier copia a y desde OneDrive, Google Drive y Dropbox con su propio inicio de sesión: se abre el navegador, entras
con tu cuenta y aceptas. MegaCopier nunca ve tu contraseña; solo guarda un "token de renovación" en el Administrador de
credenciales de Windows, igual que las contraseñas de los servidores FTP o WebDAV.

Cada servicio exige que el programa esté **registrado** una vez. Es gratis y lleva unos 10 minutos. La versión
publicada de MegaCopier **no trae identificadores incluidos**, así que cada persona registra el suyo siguiendo estos pasos
(así además nadie depende de una cuenta ajena). En todos los casos la dirección de vuelta es `http://127.0.0.1`
(MegaCopier escucha solo en este equipo, en un puerto libre).

## OneDrive (Microsoft)

1. Entra en <https://entra.microsoft.com> > **Aplicaciones** > **Registros de aplicaciones** > **Nuevo registro**.
2. Nombre: `MegaCopier`. Tipos de cuenta: **Cuentas de cualquier directorio organizativo y cuentas personales de Microsoft**.
3. URI de redirección: plataforma **Cliente público/nativo (móvil y escritorio)**, valor `http://localhost`.
4. En **Permisos de API** añade **Microsoft Graph > Permisos delegados > Files.ReadWrite** y **offline_access**.
5. Copia el **Id. de aplicación (cliente)**: es el *Identificador de la app* que pide MegaCopier.

## SharePoint (Microsoft 365)

La biblioteca de documentos de un sitio de tu empresa. Sirve el mismo registro que OneDrive, con dos permisos más:

1. En el registro de OneDrive, **Permisos de API** > **Microsoft Graph > Permisos delegados**: añade **Files.ReadWrite.All**
   y **Sites.ReadWrite.All**. Si tu empresa lo exige, un administrador tiene que **conceder el consentimiento**.
2. En MegaCopier, tipo **SharePoint**: pega el mismo identificador y escribe la dirección del sitio, como
   `https://empresa.sharepoint.com/sites/Equipo`. «Carpeta» es una carpeta dentro de la biblioteca (vacío = la raíz).

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

## Azure Blob Storage

No hace falta registrar nada: se usa un **token SAS**, un permiso firmado para un contenedor, sin dar la clave de la
cuenta.

1. En <https://portal.azure.com>, abre la cuenta de almacenamiento > **Contenedores** > el contenedor > **Tokens de acceso
   compartido**.
2. Permisos: **Leer, Agregar, Crear, Escribir, Eliminar y Enumerar**. Elige hasta cuándo vale y pulsa **Generar**.
3. En MegaCopier, tipo **Azure Blob**: «Cuenta» es `https://tucuenta.blob.core.windows.net` (o solo `tucuenta`), el token
   SAS va en su campo (con o sin el «?» del principio) y «Contenedor» es `contenedor` o `contenedor/carpeta`.

Los archivos grandes se suben por bloques de 16 MB y solo aparecen al final, completos. Cuando el token caduca, MegaCopier
lo dice («permiso SAS no válido o caducado»): genera otro y pégalo.

## Google Cloud Storage

Google Cloud Storage entiende el mismo idioma que S3. Necesita **claves HMAC**:

1. En <https://console.cloud.google.com> > **Cloud Storage** > **Configuración** > **Interoperabilidad**, crea una clave
   HMAC para tu cuenta (o una cuenta de servicio).
2. En MegaCopier, tipo **Google Cloud Storage**: «Clave de acceso» es el *ID de acceso* (`GOOG1…`), «Clave secreta» el
   *secreto* y «Bucket» es `bucket` o `bucket/carpeta`. El servidor (`storage.googleapis.com`) y la región (`auto`) ya
   vienen puestos.

## Usarlo

Ajustes > Red y dispositivos > Servidores y nubes > **Añadir…** > tipo OneDrive, SharePoint, Google Drive o Dropbox > pega el identificador > **Iniciar sesión en el
navegador** > acepta en la página del servicio > **Guardar**. Después la nube aparece en "Nueva > Copiar desde un móvil,
cámara o servidor…" y como destino, como cualquier servidor.

Detalles:

- Los archivos grandes se suben por partes (OneDrive en trozos de 10 MB, Dropbox de 8 MB, Google en una subida
  reanudable), y MegaCopier ajusta solo cuántos sube a la vez.
- Los documentos de Google (Docs, Hojas, Presentaciones) no son archivos descargables tal cual y no se muestran.
- Lo que MegaCopier borra en la nube va a la papelera del servicio, no se elimina para siempre.
