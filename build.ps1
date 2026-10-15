# Compila, prueba y publica MegaCopier sin necesidad de abrir Visual Studio.
#   .\build.ps1            -> pruebas + app (artifacts\app) + herramienta de consola (artifacts\cli)
#   .\build.ps1 -SkipTests -> solo publica
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# El compilador AOT localiza el enlazador de C++ con vswhere.exe y espera encontrarlo en el PATH.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path $installer) { $env:PATH = "$installer;$env:PATH" }

if (-not $SkipTests) {
    dotnet test tests/MegaCopier.Engine.Tests -nologo
    if ($LASTEXITCODE -ne 0) { throw 'Las pruebas fallaron.' }
}

# La app no puede publicarse encima de sí misma si está abierta en la bandeja.
if (Get-Process -Name MegaCopier -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$PSScriptRoot\artifacts\app\*" }) {
    throw 'Cierra MegaCopier (bandeja > Salir) antes de publicar.'
}

dotnet publish src/MegaCopier.App -c Release -o artifacts/app -nologo
if ($LASTEXITCODE -ne 0) { throw 'La publicación de la app falló.' }

& (Join-Path $PSScriptRoot 'src\MegaCopier.ShellExt\build.ps1') -Output (Join-Path $PSScriptRoot 'artifacts\app')

dotnet publish src/MegaCopier.Cli -c Release -o artifacts/cli -nologo
if ($LASTEXITCODE -ne 0) { throw 'La publicación de la consola falló.' }

Write-Host "Listo: artifacts\app\MegaCopier.exe (+ MegaCopierShell.dll) y artifacts\cli\mcopy.exe"
