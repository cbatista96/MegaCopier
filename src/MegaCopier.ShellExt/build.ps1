# Compila MegaCopierShell.dll con las Build Tools de C++ (sin abrir Visual Studio).
#   .\build.ps1 -Output <carpeta>
param([Parameter(Mandatory)][string]$Output)

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

# El Explorador mantiene cargada la DLL si ya se usó: no se puede sobrescribir, pero sí renombrar.
$target = Join-Path $Output 'MegaCopierShell.dll'
if (Test-Path $target) {
    try { Remove-Item $target -Force -ErrorAction Stop }
    catch { Rename-Item $target ("MegaCopierShell.dll.old-" + [DateTime]::Now.Ticks) }
}
Copy-Item $dll $target
Write-Host "Extensión del shell: $target"
