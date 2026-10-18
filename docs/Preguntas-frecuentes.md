# Preguntas frecuentes

## Instalar

### Windows dice «Windows protegió su PC» al abrir el instalador
Es el aviso de **SmartScreen**, que sale con los programas que todavía no están firmados digitalmente. Pulsa
**Más información** y luego **Ejecutar de todas formas**. Si quieres asegurarte de que el archivo es el original, calcula
su suma y compárala con la de `SHA256SUMS.txt` de la misma versión:

```powershell
Get-FileHash .\MegaCopier-Setup-1.0.0.exe -Algorithm SHA256
```

### ¿Necesito permisos de administrador?
No. Se instala solo para tu usuario. Solo algunas funciones concretas los piden en el momento (modo copia de seguridad,
copiar archivos en uso con VSS, excluir MegaCopier de Windows Defender), y Windows te pregunta.

### ¿Funciona en Windows 10?
No. Necesita **Windows 11 de 64 bits**.

### ¿Cómo lo actualizo?
Descarga el instalador de la [última versión](https://github.com/cbatista96/MegaCopier/releases/latest) y ejecútalo
encima. Se conservan tus ajustes e historial. Para enterarte de las versiones nuevas: en GitHub, **Watch > Custom >
Releases**.

### ¿Cómo lo quito del todo?
*Configuración > Aplicaciones > MegaCopier > Desinstalar*. Al final pregunta si borrar también tus ajustes e historial.
Windows vuelve a copiar como siempre.

## Usarlo

### He pegado con Ctrl+V y ha copiado el Explorador, no MegaCopier
Comprueba que:
- MegaCopier está **activado**: su icono de la bandeja tiene color (gris con una X roja = desactivado; doble clic para
  activarlo).
- *Ajustes > General > Copiar y mover con MegaCopier al pegar (Ctrl+V)* está activado.
- No estás pegando en la misma carpeta ni en una carpeta especial (*Este equipo*, dentro de un ZIP, una biblioteca…):
  esas las sigue haciendo el Explorador a propósito.
- MegaCopier está abierto (en la bandeja). Si no, actívalo en *Ajustes > General > Iniciar con Windows*.

### ¿Cómo vuelvo a copiar con el Explorador un momento?
Doble clic en el icono de la bandeja: se desactiva hasta que vuelvas a hacer doble clic.

### No veo «Pegar con MegaCopier» en el menú contextual
*Ajustes > General > Opciones en el menú contextual*. En Windows 11 aparece en el menú corto; si no, en *Mostrar más
opciones*. La versión portátil no añade opciones al menú (hace falta el instalador).

### ¿Qué pasa si se va la luz en mitad de una copia?
Al volver a abrir MegaCopier, la copia aparece como **interrumpida**. Pulsa **Continuar**: comprueba lo ya copiado y sigue
desde el último bloque guardado, también dentro de un archivo enorme. Como mucho se repite un bloque (64 MB). Los archivos
a medias nunca tienen el nombre final: se llaman `.mcpart` hasta que están completos.

### Veo archivos `.mcpart` y `.mcjournal` en el destino
Son la copia a medias de un archivo y su diario. Desaparecen al terminar. Si abandonas la copia con **Descartar**, se
borran. No los borres a mano mientras la copia está pendiente o tendrá que empezar ese archivo de cero.

### La copia va más lenta de lo que esperaba
- Mira si hay un **límite de velocidad** (*Ajustes > Copia > Rendimiento* o *Ajustes > Cola y rendimiento > Límite de velocidad por horario*), o si
  están activados *Ir más despacio mientras usas el equipo*, *Pausar con batería* o *Pausar en juegos*.
- Dos copias en el **mismo disco** van de una en una a propósito (a la vez serían más lentas).
- Muchos archivos pequeños: activa *Ajustes > Cola y rendimiento > Excluir MegaCopier del análisis de Windows Defender*.
- *Ajustes > Cola y rendimiento > Ajuste automático para cada disco > Medir los discos* elige el mejor tamaño de bloque para cada uno.
- En un SSD, tras escribir unos GB seguidos la velocidad baja (se llena su caché interna): pasa con cualquier programa.

### ¿Verifica siempre lo que copia?
Cada bloque se comprueba al reanudar. La **verificación completa** (releer todo el destino del disco y compararlo) se
activa en *Ajustes > Copia > Verificar después de copiar* o en cada copia; tarda un poco más, pero repara lo que no
coincida.

### ¿Puedo copiar al móvil?
Sí: por cable USB (*Nueva > Copiar archivos a un móvil, cámara o servidor…*) o sin cables con la app gratuita
[LocalSend](https://localsend.org) (*Nueva > Enviar archivos a otro equipo…*). Ver la [guía](Guia.md#13-móviles-otros-equipos-servidores-y-nubes).

### ¿Por qué tengo que registrar una app para usar OneDrive, Google Drive o Dropbox?
Esos servicios solo dejan entrar a programas registrados. Es gratis y se hace una vez:
[cómo conectar las nubes](Nube.md).

### ¿MegaCopier envía algo a internet?
No. No tiene telemetría ni comprueba actualizaciones por su cuenta. Solo se conecta a lo que tú configures: servidores,
nubes, avisos por correo o webhook.

### ¿Dónde guarda mis ajustes?
En `%LOCALAPPDATA%\MegaCopier` (en la versión portátil, en la carpeta `Data` de al lado). Ahí están `settings.json`, el
historial, los temas y los registros. Las contraseñas no: van al Administrador de credenciales de Windows.

## Problemas

### MegaCopier se ha cerrado de golpe
Se guarda un informe en `%LOCALAPPDATA%\MegaCopier\crash.log`. Las copias en marcha se pueden continuar al volver a
abrirlo. Por favor, [abre una incidencia](https://github.com/cbatista96/MegaCopier/issues/new/choose) con ese archivo y
qué estabas haciendo.

### Un archivo da error «otro programa tiene el archivo abierto»
Cierra el programa que lo usa y pulsa **Reintentar**. Para copiar archivos que siempre están abiertos (Outlook, bases de
datos), MegaCopier puede copiarlos desde una instantánea de Windows (VSS); pide permiso de administrador.

### He encontrado un fallo o tengo una idea
[Abre una incidencia](https://github.com/cbatista96/MegaCopier/issues/new/choose) con la plantilla que corresponda. Si es
un fallo de seguridad, no lo publiques: sigue [SECURITY.md](../SECURITY.md).
