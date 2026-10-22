# Imagen del instalador

`Assets/Wizard.bmp` y `Assets/WizardSmall.bmp` usan el icono original de MegaCopier. La composición se genera localmente con `tools/Installer/artwork.py` (Python y Pillow); no descarga recursos ni modifica el icono. La imagen lateral está preparada a triple resolución para pantallas de alta densidad. Inno Setup la presenta en Bienvenida y Finalización, y el icono pequeño en las demás páginas.

Los accesos al escritorio y al menú Inicio son opcionales y vienen desmarcados. La opción de barra de tareas prepara un `.lnk` dentro de la carpeta de instalación y lo muestra en el Explorador: el usuario debe anclarlo mediante el menú de Windows. No se emplean verbos ocultos ni APIs no documentadas para forzar el anclaje. Microsoft indica que la API de anclaje no debe invocarse desde instaladores: https://learn.microsoft.com/windows/apps/develop/windows-integration/pin-to-taskbar

Las actualizaciones silenciosas reciben las tareas de acceso existentes y no sobrescriben los permisos de búsqueda ni de informes. Una instalación nueva aplica las tareas elegidas; solo la búsqueda automática está activada entre las tres opciones del actualizador.
