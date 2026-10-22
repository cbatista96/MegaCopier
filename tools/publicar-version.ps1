<#
.SYNOPSIS
    Publica una versión nueva de MegaCopier.

.DESCRIPTION
    1. Comprueba que estás en la rama main, sin cambios a medio guardar y al día con GitHub.
    2. Comprueba que hay algo anotado en «## [Sin publicar]» de CHANGELOG.md.
    3. Cambia la versión en Directory.Build.props (de ahí la toman la app, la consola y el instalador).
    4. Convierte «## [Sin publicar]» en «## [x.y.z] - fecha» y actualiza los enlaces del final de CHANGELOG.md.
    5. Guarda el cambio («Versión x.y.z»), crea la etiqueta vx.y.z y lo sube todo a GitHub.
    Al recibir la etiqueta, GitHub compila, pasa las pruebas y publica la versión con el instalador, el ZIP portátil,
    mcopy y las sumas SHA-256 (.github/workflows/release.yml).

.EXAMPLE
    .\tools\publicar-version.ps1 1.1.0
    .\tools\publicar-version.ps1 1.1.0 -SinSubir     # lo prepara todo en local, sin subir nada
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$SinSubir
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Fallar([string]$message) { Write-Host "ERROR: $message" -ForegroundColor Red; exit 1 }
function Ejecutar { & git @args; if ($LASTEXITCODE -ne 0) { Fallar "git $args" } }

if ($Version -notmatch '^\d+\.\d+\.\d+$') { Fallar "La versión tiene que ser x.y.z (por ejemplo 1.1.0), no '$Version'." }
$tag = "v$Version"

# 1. Rama y estado
$branch = (& git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') { Fallar "Estás en la rama '$branch'. Las versiones se publican desde main." }
if (& git status --porcelain) { Fallar "Hay cambios sin guardar (git status). Guárdalos (commit) o descártalos antes." }
if (& git tag --list $tag) { Fallar "La etiqueta $tag ya existe." }
if (-not $SinSubir) {
    Ejecutar fetch --quiet origin main
    $behind = (& git rev-list --count HEAD..origin/main).Trim()
    if ($behind -ne '0') { Fallar "Tu main va $behind cambios por detrás de GitHub. Haz git pull antes." }
}

# 2. Versión anterior y novedades
$propsPath = Join-Path $root 'Directory.Build.props'
$props = [IO.File]::ReadAllText($propsPath)
$previous = [regex]::Match($props, '<Version>([^<]+)</Version>').Groups[1].Value
if ([version]$Version -le [version]$previous) { Fallar "La versión nueva ($Version) tiene que ser mayor que la actual ($previous)." }

$changelogPath = Join-Path $root 'CHANGELOG.md'
$changelog = [IO.File]::ReadAllText($changelogPath).Replace("`r`n", "`n")
$section = [regex]::Match($changelog, '(?ms)^## \[Sin publicar\][^\n]*\n(.*?)(?=^## \[)')
if (-not $section.Success) { Fallar "No encuentro la sección '## [Sin publicar]' en CHANGELOG.md." }
if (-not $section.Groups[1].Value.Trim()) { Fallar "No hay nada en '## [Sin publicar]' de CHANGELOG.md: anota antes las novedades de esta versión." }
$englishPath = Join-Path $root 'CHANGELOG.en.md'
$english = [IO.File]::ReadAllText($englishPath).Replace("`r`n", "`n")
$englishSection = [regex]::Match($english, '(?ms)^## \[Unreleased\][^\n]*\n(.*?)(?=^## \[)')
if (-not $englishSection.Success -or -not $englishSection.Groups[1].Value.Trim()) { Fallar 'Faltan las novedades de esta versión en inglés (CHANGELOG.en.md, Unreleased).' }
foreach ($releaseSection in @($section.Groups[1].Value, $englishSection.Groups[1].Value)) {
    $summary = [regex]::Match($releaseSection, '(?ims)^### (?:Resumen|Highlights)\s*\r?\n(.*?)(?=^### |\z)')
    $points = [regex]::Matches($summary.Groups[1].Value, '(?m)^- ').Count
    if (-not $summary.Success -or $points -lt 1) { Fallar 'Añade un resumen editorial en ambos registros (### Resumen / ### Highlights). Sigue los criterios de selección y agrupación de docs/Versiones.md.' }
}
if (-not (Test-Path (Join-Path $root 'tools/Updates/public-key.pem'))) { Fallar 'Configura antes la firma de actualizaciones: tools/Updates/README.md.' }

# 3. Directory.Build.props
$props = $props.Replace("<Version>$previous</Version>", "<Version>$Version</Version>")
[IO.File]::WriteAllText($propsPath, $props, $utf8)

# 4. CHANGELOG.md
$date = Get-Date -Format 'yyyy-MM-dd'
$changelog = $changelog.Replace('## [Sin publicar]', "## [Sin publicar]`n`n## [$Version] - $date")
$repo = 'https://github.com/cbatista96/MegaCopier'
$changelog = [regex]::Replace($changelog, '(?m)^\[Sin publicar\]: .*$', "[Sin publicar]: $repo/compare/$tag...HEAD`n[$Version]: $repo/compare/v$previous...$tag")
[IO.File]::WriteAllText($changelogPath, $changelog, $utf8)
$english = $english.Replace('## [Unreleased]', "## [Unreleased]`n`n## [$Version] - $date")
$english = [regex]::Replace($english, '(?m)^\[Unreleased\]: .*$', "[Unreleased]: $repo/compare/$tag...HEAD`n[$Version]: $repo/compare/v$previous...$tag")
[IO.File]::WriteAllText($englishPath, $english, $utf8)

Write-Host ""
Write-Host "Novedades de la versión $Version" -ForegroundColor Cyan
Write-Host $section.Groups[1].Value.Trim()
Write-Host ""

# 5. Guardar, etiquetar y subir
Ejecutar add Directory.Build.props CHANGELOG.md CHANGELOG.en.md
Ejecutar commit --quiet -m "Versión $Version"
Ejecutar tag -a $tag -m "MegaCopier $Version"
if ($SinSubir) {
    Write-Host "Listo en local: versión $Version guardada y etiqueta $tag creada." -ForegroundColor Green
    Write-Host "Para publicarla: git push origin main $tag"
    exit 0
}
Ejecutar push --quiet origin main $tag
Write-Host "Versión $Version subida. GitHub la está compilando y publicando:" -ForegroundColor Green
Write-Host "  $repo/actions"
Write-Host "  $repo/releases/tag/$tag  (en unos 10 minutos)"
