# Compila, prueba y empaqueta MegaCopier sin necesidad de abrir Visual Studio.
#   .\build.ps1             -> textos + pruebas + app + extensión del Explorador + consola + instalador
#   .\build.ps1 -SkipTests  -> igual, sin pruebas
#
# Resultado:
#   artifacts\app\                          la app (MegaCopier.exe, ya ejecutable)
#   artifacts\cli\mcopy.exe                 herramienta de consola
#   artifacts\installer\MegaCopier-Setup-<versión>.exe   instalador (si está Inno Setup)
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = ([xml](Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
Write-Host "MegaCopier $version"

# El compilador AOT localiza el enlazador de C++ con vswhere.exe y espera encontrarlo en el PATH.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path $installer) { $env:PATH = "$installer;$env:PATH" }

# Textos de la interfaz (español / inglés) a partir de tools\Strings\strings.tsv.
dotnet run tools/Strings/gen.cs -- tools/Strings/strings.tsv src/MegaCopier.App/Strings
if ($LASTEXITCODE -ne 0) { throw 'La generación de textos falló.' }

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

& (Join-Path $PSScriptRoot 'src\MegaCopier.ShellExt\build.ps1') -Output (Join-Path $PSScriptRoot 'artifacts\app') -Version $version

dotnet publish src/MegaCopier.Cli -c Release -o artifacts/cli -nologo
if ($LASTEXITCODE -ne 0) { throw 'La publicación de la consola falló.' }

# Instalador (Inno Setup 6, gratuito: winget install JRSoftware.InnoSetup).
$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($iscc) {
    & $iscc /Q "/DAppVersion=$version" installer\MegaCopier.iss
    if ($LASTEXITCODE -ne 0) { throw 'La creación del instalador falló.' }
    Write-Host "Instalador: artifacts\installer\MegaCopier-Setup-$version.exe"
}
else {
    Write-Warning 'No se encontró Inno Setup: se omite el instalador (winget install JRSoftware.InnoSetup).'
}

Write-Host "Listo."
