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
           "/link /DEF:`"$def`" /OUT:`"$dll`" /IMPLIB:`"$obj\MegaCopierShell.lib`" ole32.lib shell32.lib shlwapi.lib advapi32.lib user32.lib gdi32.lib uuid.lib"
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

# Paquete disperso para el menú contextual corto de Windows 11 (MegaCopierShell.msix, junto a la DLL).
# Sin firma por defecto: Windows 11 admite paquetes dispersos sin firmar si el editor lleva el OID de "sin firmar".
# Con MEGACOPIER_SIGN_PFX (y MEGACOPIER_SIGN_PASSWORD, MEGACOPIER_SIGN_PUBLISHER = sujeto del certificado) se firma.
$kit = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Directory -Filter '10.*' -ErrorAction SilentlyContinue |
    Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'x64' } |
    Where-Object { Test-Path (Join-Path $_ 'makeappx.exe') } | Select-Object -First 1
if (-not $kit) {
    Write-Warning 'No se encontró makeappx.exe (Windows SDK): se omite el menú corto de Windows 11.'
    return
}

$parts = @($Version.Split('.') + @('0', '0', '0', '0'))[0..3]
$publisher = if ($env:MEGACOPIER_SIGN_PFX) { $env:MEGACOPIER_SIGN_PUBLISHER } else { 'CN=MegaCopier, OID.2.25.311729368913984317654407730594956997722=1' }
$layout = Join-Path $obj 'package'
Remove-Item $layout -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $layout | Out-Null
Copy-Item (Join-Path $here 'package\*.png') $layout
$manifest = (Get-Content (Join-Path $here 'package\AppxManifest.xml') -Raw -Encoding utf8).
    Replace('$VERSION$', ($parts -join '.')).Replace('$DLL$', "MegaCopierShell-$Version.dll").Replace('$PUBLISHER$', [Security.SecurityElement]::Escape($publisher))
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))

$msix = Join-Path $Output 'MegaCopierShell.msix'
& (Join-Path $kit 'makeappx.exe') pack /o /nv /d $layout /p $msix | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'La creación del paquete del menú de Windows 11 falló.' }
if ($env:MEGACOPIER_SIGN_PFX) {
    & (Join-Path $kit 'signtool.exe') sign /fd SHA256 /f $env:MEGACOPIER_SIGN_PFX /p $env:MEGACOPIER_SIGN_PASSWORD $msix | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'La firma del paquete del menú de Windows 11 falló.' }
}
Write-Host "Menú de Windows 11: $msix"
