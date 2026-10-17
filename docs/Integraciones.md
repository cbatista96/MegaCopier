# Usar MegaCopier desde Total Commander y Directory Opus

MegaCopier ya ofrece como destino ("Nueva > Copiar a" y la ventana de destinos) las carpetas que tengas abiertas en el
Explorador, en **Total Commander** y en **Directory Opus**. Para que estos dos programas copien *con* MegaCopier, basta
un botón que llame a `mcopy send`: la copia aparece en la ventana de MegaCopier, con su progreso, su verificación y la
posibilidad de seguir tras un corte de luz.

`mcopy.exe` está en la carpeta de MegaCopier:

```
%LOCALAPPDATA%\Programs\MegaCopier\mcopy.exe
```

## Total Commander

Botón nuevo en la barra de botones (clic derecho en la barra > Cambiar…):

| Campo | Valor |
| --- | --- |
| Orden | `%LOCALAPPDATA%\Programs\MegaCopier\mcopy.exe` |
| Parámetros | `send %S %T` (copiar) o `send %S %T --move` (mover) |
| Ruta de inicio | `%P` |
| Ejecutar minimizado | sí |

`%S` son los archivos marcados (relativos a la ruta de inicio `%P`) y `%T` la carpeta del otro panel. Puedes asignarle
un atajo de teclado (por ejemplo, Mayús+F5) en Configuración > Varios > Redefinir teclas.

## Directory Opus

Botón nuevo (Configuración > Personalizar barras > Nuevo botón), tipo **MS-DOS Batch Function** o **Standard Function**:

```
"%LOCALAPPDATA%\Programs\MegaCopier\mcopy.exe" send {allfilepath} {destpath}
```

Para mover, añade `--move` al final. `{allfilepath}` son las rutas completas de lo seleccionado y `{destpath}` la carpeta
de destino (el otro panel o la otra ventana).

## Más opciones de `mcopy send`

- `--verify` comprueba la copia al terminar.
- `--also <otra carpeta>` copia a la vez a otra carpeta más.
- `--at 23:30` la deja programada.
- `mcopy` sin nada muestra la ayuda completa.
