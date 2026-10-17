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

## Asistentes de IA (Claude, Copilot y otros): servidor MCP

`mcopy mcp` es un servidor MCP (Model Context Protocol): un asistente de IA puede pedirle a MegaCopier copias,
comprobaciones y copias de seguridad hablando normal ("copia Documentos al NAS esta noche y verifícala"). Las copias se
entregan a la app: se ven en su ventana, se pueden pausar y siguen tras un corte de luz. No hay herramientas de borrado.

Herramientas: `copy` (copiar, mover, verificar, espejo y hora de inicio), `verify_checksums`, `create_checksums`,
`backup`, `list_backups`, `restore` y `status`.

En Claude Desktop (Configuración > Desarrollador > Editar configuración, archivo `claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "megacopier": {
      "command": "C:\\Users\\<tu usuario>\\AppData\\Local\\Programs\\MegaCopier\\mcopy.exe",
      "args": ["mcp"]
    }
  }
}
```

Cualquier otro programa compatible con MCP se configura igual: orden `mcopy.exe` con el argumento `mcp`.
