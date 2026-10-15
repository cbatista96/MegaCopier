# Compila la extensión del Explorador con las Build Tools de C++ (sin abrir Visual Studio).
#   .\build.ps1 -Output <carpeta> [-Version 1.0.0]
# La DLL lleva la versión en el nombre (MegaCopierShell-1.0.0.dll): el Explorador mantiene cargada la anterior,
# así que una versión nueva se instala al lado sin tener que reiniciar el Explorador.
param([Parameter(Mandatory)][string]$Output, [string]$Version = '0.0.0')

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$obj = Join-Path $here 'obj'
New-Item -ItemType Directory -Force $obj, $Output | Out-Null

$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
$env:PATH = "$installer;$env:PATH"  # vcvars64.bat también usa vswhere
$vswhere = Join-Path $installer 'vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'No se encontraron las Build Tools de C++ (componente VC.Tools.x86.x64).' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'

$source = Join-Path $here 'MegaCopierShell.cpp'
$def = Join-Path $here 'MegaCopierShell.def'
$dll = Join-Path $obj 'MegaCopierShell.dll'
$compile = "cl /nologo /utf-8 /LD /EHsc /O2 /W4 /permissive- /std:c++20 /DUNICODE /D_UNICODE /MT /Fo`"$obj\\`" `"$source`" " +
           "/link /DEF:`"$def`" /OUT:`"$dll`" /IMPLIB:`"$obj\MegaCopierShell.lib`" ole32.lib shell32.lib advapi32.lib user32.lib gdi32.lib uuid.lib"
cmd /c "`"$vcvars`" >nul && $compile"
if ($LASTEXITCODE -ne 0) { throw 'La compilación de la extensión del shell falló.' }

# Versiones anteriores: se borran si se puede; si el Explorador las tiene cargadas, se quedan hasta el próximo inicio.
Get-ChildItem $Output -Filter 'MegaCopierShell*.dll*' | ForEach-Object {
    try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch { }
}

$target = Join-Path $Output "MegaCopierShell-$Version.dll"
if (Test-Path $target) { Rename-Item $target ("MegaCopierShell-$Version.dll.old-" + [DateTime]::Now.Ticks) }
Copy-Item $dll $target
Write-Host "Extensión del shell: $target"
