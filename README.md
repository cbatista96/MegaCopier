**English** · [Español](README.es.md) · Changelog: [English](CHANGELOG.en.md) / [Español](CHANGELOG.md)

<div align="center">

<img src="src/MegaCopier.App/Assets/MegaCopier.ico" width="96" alt="MegaCopier icon">

# MegaCopier

**The Windows 11 file copier that resumes after a power cut.**

Replace File Explorer's copy and paste. If a transfer is interrupted by a power cut, a crash or a disconnected drive, resume from the last verified block, even inside a large file. Verify and repair copies, synchronize folders, create backups, and transfer to servers, cloud storage and phones.

[![Build](https://github.com/cbatista96/MegaCopier/actions/workflows/ci.yml/badge.svg)](https://github.com/cbatista96/MegaCopier/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/cbatista96/MegaCopier?label=version&color=6f42c1)](https://github.com/cbatista96/MegaCopier/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/cbatista96/MegaCopier/total?label=downloads&color=success)](https://github.com/cbatista96/MegaCopier/releases)
[![GPL v3](https://img.shields.io/badge/license-GPLv3-blue.svg)](LICENSE)
[![Windows 11](https://img.shields.io/badge/Windows%2011-64%20bit-0078D6)](#requirements)
[![Languages](https://img.shields.io/badge/languages-14-orange)](#all-features)

### [Download MegaCopier](https://github.com/cbatista96/MegaCopier/releases/latest)

### Code signing policy

Updates use the project's own ECDSA key. The application to **SignPath Foundation** was submitted on October 21, 2026; approval and Authenticode certificate access remain pending. Sponsorship is not claimed as granted. See the [code-signing policy](docs/Code-signing-policy.md) and [privacy policy](docs/Privacy.md).

[Features](#all-features) · [Screenshots](#screenshots) · [User guide — Spanish](docs/Guia.md) · [Changelog](CHANGELOG.en.md) · [Español](README.es.md)

<img src="docs/capturas/en/completa-oscuro.png" width="820" alt="Current full transfer view with the dark theme">

</div>

> **Development status:** `main` includes [unreleased changes](CHANGELOG.en.md#unreleased). The download above is still the latest published version, **1.7.1**, and does not contain all development features described below. Updating this repository does not publish a new installer or release.

## Why MegaCopier

| | |
|---|---|
| **Resume after interruption** | Verify recorded blocks and continue within the same file instead of restarting the entire transfer. |
| **Transfer efficiently** | Direct asynchronous disk I/O, per-disk queues, ReFS cloning and supported server-side copying. Speed depends on hardware, settings and workload; screenshots are demonstrations, not benchmark evidence. |
| **Verify and repair** | Read back the destination, compare checksums and repair damaged blocks. |
| **Protect backups** | Version retention, deduplicated snapshots, ransomware checks and optional recovery data. |
| **Reach remote storage** | Phones and cameras, LocalSend, another PC, FTP, SFTP, WebDAV, S3, OneDrive, Google Drive and Dropbox. |
| **Integrate with Windows** | Explorer paste, context menus, drag and drop, optional Windows startup and tray controls. |
| **Control reporting** | Optional filtered crash reports, a visible installer choice and a Settings toggle. Local transfer logs are distinct from reports sent to the private receiver. |
| **Make it yours** | 73 themes, 22 progress bar styles, 29 icon packs, 19 built-in sounds, 14 interface languages and a portable package. |

## Download and install

Everything is on the [latest release page](https://github.com/cbatista96/MegaCopier/releases/latest):

| File | Purpose |
|---|---|
| `MegaCopier-Setup-<version>.exe` | Per-user installer; the usual choice. |
| `MegaCopier-<version>-portable.zip` | Extract anywhere and run `MegaCopier.exe`; settings stay in the adjacent `Data` folder. |
| `mcopy-<version>.exe` | Standalone console tool. The installer already includes it. |
| `SHA256SUMS.txt` | SHA-256 checksums for the downloaded files. |

1. Run the installer on 64-bit Windows 11. It installs for your user in `%LOCALAPPDATA%\Programs\MegaCopier`, without administrator rights.
2. Choose optional shortcuts, Explorer integration and Windows startup. In the development installer, desktop and Start menu shortcuts are off by default; the taskbar choice prepares a shortcut for you to pin through Windows.
3. In the development installer, automatic update checking and error reporting are selected by default and can be deselected. Automatic downloading and installation are off. Silent updates preserve your preferences.

Current public executables and installers are **not Authenticode-signed**. Windows may display SmartScreen warnings. Check the SHA-256 checksum against `SHA256SUMS.txt` and review the [signing status](docs/Code-signing-policy.md). A certificate does not guarantee that every SmartScreen warning will disappear immediately.

```powershell
Get-FileHash .\MegaCopier-Setup-1.7.1.exe -Algorithm SHA256
```

**Portable:** extract the ZIP and run `MegaCopier.exe`. Its `Data` folder holds settings and pending work. Portable mode does not install Explorer integration.

**Uninstall:** Windows Settings > Apps > MegaCopier > Uninstall. You can choose whether to remove settings, history and pending work.

### Requirements

- Windows 11, 64-bit x64.
- The application includes its runtime; no separate .NET installation is needed.
- Local transfers work offline without an account. Remote destinations and update downloads require their respective connections.

## Quick start

1. Copy or cut files in File Explorer and paste into the destination as usual.
2. Follow progress in the compact window. **More** expands it to the file list; **Less** returns to compact mode. The button keeps the same size in both states.
3. After an interruption, reopen MegaCopier and choose Resume for the interrupted transfer.
4. Use the tray icon to open MegaCopier or disable its Explorer interception.

```text
mcopy copy D:\Photos E:\Backup --verify
mcopy move D:\Downloads\Videos E:\Videos
mcopy copy D:\Work F:\Mirror --mirror
mcopy backup D:\Documents F:\Backups
```

## Updates and release notes

In the development version, **Settings > Updates** provides one fixed-size action to check, download and install, with progress and cancellation. Highlights describe the available version, or the installed version when no newer version is known. Complete notes and images open inside the application.

- Automatic checking runs every **48 hours** when enabled and catches up after reconnection if overdue. Connection changes do not cause continuous version checks.
- Automatic downloading is optional and applies to versions discovered by automatic checks. A manual check offers Download.
- Downloads default to Wi-Fi and Ethernet without metering. Mobile and metered networks require permission.
- Optional automatic installation waits until the app is in the tray with no active, paused or pending transfers, dialogs or live synchronization. A completion notification opens Updates.
- Packages are verified using an **ECDSA P-256 signed manifest and SHA-256** before installation. The private signing key is never distributed.
- Known update status and cached notes remain available offline. A manual check or download without Internet displays a connection warning.
- Portable copies download a ZIP instead of using the installed application's replacement process.
- Spanish interface: Spanish release notes. English or any other interface language: English release notes, including previous versions. GitHub release pages show English first and Spanish below.

## Error reports, local logs and security

These are separate features:

- **Local transfer logs and reports** record transfer results and may contain paths. They are not uploaded automatically as crash reports.
- **Optional crash reports** contain versions, language, theme, coarse activity and filtered traces. They exclude files, file names, paths and free-form exception messages. The development installer offers reporting selected by default; portable copies start with it off. Change this in **Settings > Advanced**.
- When automatic reporting is on, authorized reports are sent in the background. When off, an unexpected-exit dialog offers a preview, one-time sending or permission for future reports. Disabling reporting removes queued local reports, not reports already received.
- Cloudflare processes the connection IP for abuse limiting; it is excluded from the report stored in the private GitHub repository. See the [privacy policy](docs/Privacy.md) for providers, retention and deletion limits.
- **Security vulnerabilities are not automatically detected by crash reporting.** Use the private route in [SECURITY.md](SECURITY.md) for a suspected vulnerability; do not post secrets or sensitive exploitation details in public issues.

## Screenshots

Fresh captures of the current application, in English, using local demonstration files. Each example preserves its illustrated theme, icon pack and progress bar style. File names and transfer figures are illustrative; these screenshots are not performance claims. Spanish captures are available in the Spanish overview. Illustrations in historical notes show the current development interface.

<table>
<tr>
<td width="50%"><img src="docs/capturas/en/compacta.png" alt="Compact window"><br><b>Compact window</b>: Only the essentials while copying.</td>
<td width="50%"><img src="docs/capturas/en/interrumpida.png" alt="Interrupted transfer"><br><b>Interrupted transfer</b>: Pending work is recovered after restarting.</td>
</tr>
<tr>
<td width="50%"><img src="docs/capturas/en/reanudada.png" alt="Resumed transfer"><br><b>Resumed transfer</b>: Continue the interrupted transfer rather than starting the whole copy again.</td>
<td width="50%"><img src="docs/capturas/en/conflicto.png" alt="File already exists"><br><b>File already exists</b>: Compare the files and decide what to keep.</td>
</tr>
<tr>
<td width="50%"><img src="docs/capturas/en/completa-claro.png" alt="Full view, light theme"><br><b>Full view, light theme</b>: Transfer queue and current file list.</td>
<td width="50%"><img src="docs/capturas/en/comparar.png" alt="Compare and synchronize"><br><b>Compare and synchronize</b>: Review both folders before applying changes.</td>
</tr>
<tr>
<td width="50%"><img src="docs/capturas/en/ampliada-estado.png" alt="Speed and status"><br><b>Speed and status</b>: Live graph and transfer totals.</td>
<td width="50%"><img src="docs/capturas/en/ampliada-opciones.png" alt="Transfer options"><br><b>Transfer options</b>: Adjust this transfer while it runs.</td>
</tr>
<tr>
<td width="50%"><img src="docs/capturas/en/historial.png" alt="History"><br><b>History</b>: Repeat, undo and copy back.</td>
<td width="50%"><img src="docs/capturas/en/linea-tiempo.png" alt="Queue timeline"><br><b>Queue timeline</b>: Review the queued and scheduled transfers.</td>
</tr>
<tr>
<td width="50%"><img src="docs/capturas/en/terminadas.png" alt="Completed transfers"><br><b>Completed transfers</b>: Summary, skipped files and Open folder.</td>
<td width="50%"><img src="docs/capturas/en/ajustes.png" alt="Settings"><br><b>Settings</b>: Search, sections and contextual explanations.</td>
</tr>
<tr>
<td width="50%"><img src="docs/capturas/en/ajustes-comportamiento.png" alt="Behavior"><br><b>Behavior</b>: Queue choices, incomplete files and destination ejection.</td>
<td width="50%"><img src="docs/capturas/en/ajustes-errores.png" alt="Errors"><br><b>Errors</b>: Retries, local error reports and transfer logs.</td>
</tr>
<tr>
<td colspan="2"><img src="docs/capturas/en/ajustes-madera.png" alt="Wood theme and Mosaic icons"><br><b>Wood theme and Mosaic icons</b>: Combine any theme with any icon pack.</td>
</tr>
</table>

### Themes

73 themes change the application’s colors and supported control styling, with gradients and optional textures. Create custom colors in the theme editor. Here are the same theme examples shown in the previous gallery, captured again in the current English interface.

<table>
<tr>
<td width="33%"><img src="docs/capturas/en/temas/combustion.png" alt="Afterburn"><br>Afterburn</td>
<td width="33%"><img src="docs/capturas/en/temas/codigo-verde.png" alt="Green code"><br>Green code</td>
<td width="33%"><img src="docs/capturas/en/temas/aurora.png" alt="Aurora"><br>Aurora</td>
</tr>
<tr>
<td width="33%"><img src="docs/capturas/en/temas/galaxia.png" alt="Galaxy"><br>Galaxy</td>
<td width="33%"><img src="docs/capturas/en/temas/neon.png" alt="Neon"><br>Neon</td>
<td width="33%"><img src="docs/capturas/en/temas/caja-negra.png" alt="Black box"><br>Black box</td>
</tr>
<tr>
<td width="33%"><img src="docs/capturas/en/temas/madera.png" alt="Wood"><br>Wood</td>
<td width="33%"><img src="docs/capturas/en/temas/cuaderno.png" alt="Notebook"><br>Notebook</td>
<td width="33%"><img src="docs/capturas/en/temas/cristal.png" alt="Glass"><br>Glass</td>
</tr>
<tr>
<td width="33%"><img src="docs/capturas/en/temas/burbuja-azul.png" alt="Blue bubble"><br>Blue bubble</td>
<td width="33%"><img src="docs/capturas/en/temas/berenjena.png" alt="Aubergine"><br>Aubergine</td>
<td width="33%"><img src="docs/capturas/en/temas/humo.png" alt="Smoke"><br>Smoke</td>
</tr>
<tr>
<td width="33%"><img src="docs/capturas/en/temas/cobalto.png" alt="Cobalt"><br>Cobalt</td>
<td width="33%"><img src="docs/capturas/en/temas/metro-oscuro.png" alt="Dark Metro"><br>Dark Metro</td>
<td width="33%"><img src="docs/capturas/en/temas/aero.png" alt="Aero"><br>Aero</td>
</tr>
<tr>
<td width="33%"><img src="docs/capturas/en/temas/luna.png" alt="Luna"><br>Luna</td>
<td width="33%"><img src="docs/capturas/en/temas/aire.png" alt="Air"><br>Air</td>
<td width="33%"><img src="docs/capturas/en/temas/menta.png" alt="Mint"><br>Mint</td>
</tr>
<tr>
<td width="33%"><img src="docs/capturas/en/temas/fluent-dark.png" alt="Fluent dark"><br>Fluent dark</td>
<td width="33%"><img src="docs/capturas/en/temas/fluent-light.png" alt="Fluent light"><br>Fluent light</td>
<td width="33%"><img src="docs/capturas/en/temas/midnight.png" alt="Midnight"><br>Midnight</td>
</tr>
<tr>
<td width="33%"><img src="docs/capturas/en/temas/ocean.png" alt="Ocean"><br>Ocean</td>
<td width="33%"><img src="docs/capturas/en/temas/forest.png" alt="Forest"><br>Forest</td>
<td width="33%"><img src="docs/capturas/en/temas/sunset.png" alt="Sunset"><br>Sunset</td>
</tr>
<tr>
<td width="33%"><img src="docs/capturas/en/temas/azul-clasico.png" alt="Classic blue"><br>Classic blue</td>
<td width="33%"><img src="docs/capturas/en/temas/gris-clasico.png" alt="Classic grey"><br>Classic grey</td>
<td width="33%"><img src="docs/capturas/en/temas/contrast.png" alt="High contrast"><br>High contrast</td>
</tr>
<tr>
<td width="33%"><img src="docs/capturas/en/temas/material-oscuro.png" alt="Material dark"><br>Material dark</td>
<td width="33%"><img src="docs/capturas/en/temas/silicio-claro.png" alt="Light silicon"><br>Light silicon</td>
<td width="33%"><img src="docs/capturas/en/temas/capuchino.png" alt="Cappuccino"><br>Cappuccino</td>
</tr>
<tr>
<td width="33%"><img src="docs/capturas/en/temas/arcade.png" alt="Arcade"><br>Arcade</td>
<td width="33%"><img src="docs/capturas/en/temas/vampiro.png" alt="Vampire"><br>Vampire</td>
<td width="33%"><img src="docs/capturas/en/temas/tiza.png" alt="Chalk"><br>Chalk</td>
</tr>
</table>

### Icon packs

29 icon packs can be combined with any theme. These seven original examples are shown below; other packs draw inspiration from Windows, Android, Material You, macOS and Linux desktops.

<table>
<tr>
<td width="50%"><img src="docs/capturas/en/iconos/fluent.png" alt="Fluent icons"><br><b>Fluent</b></td>
<td width="50%"><img src="docs/capturas/en/iconos/color.png" alt="Color icons"><br><b>Color</b></td>
</tr>
<tr>
<td width="50%"><img src="docs/capturas/en/iconos/mosaico.png" alt="Mosaic icons"><br><b>Mosaic</b></td>
<td width="50%"><img src="docs/capturas/en/iconos/suave.png" alt="Soft icons"><br><b>Soft</b></td>
</tr>
<tr>
<td width="50%"><img src="docs/capturas/en/iconos/degradado.png" alt="Gradient icons"><br><b>Gradient</b></td>
<td width="50%"><img src="docs/capturas/en/iconos/neon.png" alt="Neon icons"><br><b>Neon</b></td>
</tr>
<tr>
<td colspan="2"><img src="docs/capturas/en/iconos/windows10.png" alt="Windows 10 icons"><br><b>Windows 10</b></td>
</tr>
</table>

### Progress bars

22 styles, with independent choices for total, current-file and queue bars: colors, height, rounding, motion, state effects and percentage placement.

<img src="docs/capturas/en/barras.png" alt="Progress bar styles and live preview">

### Recent additions

Integrated updates, optional private reports and reorganized sound controls in the development version.

<table><tr><td><img src="docs/capturas/en/actualizaciones.png" alt="Update center"></td><td><img src="docs/capturas/en/sonidos.png" alt="Sound controls"></td></tr></table>

<img src="docs/capturas/en/informes.png" alt="Optional filtered crash-report controls">


## All features

### Reliability and verification

- **Block-based resume:** confirmed blocks are checked before continuing, including after a power outage.
- **Verification:** BLAKE3, xxHash3, xxHash128, SHA-256, SHA-512 and CRC32C; optional verification of skipped files and the source.
- Compare and save checksums, checksum files and supported file streams; repair blocks that fail verification.
- Recover from missing devices or full destinations by waiting for the device or for free space.
- Optional ransomware checks stop suspicious replacement patterns.
- Optional recovery data can reconstruct damaged blocks; `mcopy repair` provides the console route.
- Forensic manifests record checksums and provenance information and can be verified with `mcopy verify-manifest`.

### Performance controls

- Direct asynchronous disk I/O, parallel small-file transfers, IoRing support and per-disk queues.
- Per-disk tuning of block sizes and queue depths; ReFS block cloning where supported.
- Supported server-side network copying and SMB compression.
- Delta copying for changed blocks, optional multiple sources, and configurable concurrent remote connections.
- Parallel ZIP and 7z compression.
- Live per-transfer speed limits, low-priority mode, battery or full-screen pauses and schedules.
- Optional Windows Defender exclusion requires administrative permission; it is not enabled silently.

### What and how to copy

- Copy, move, mirror, update-only, folders-only and organize photos by camera date.
- Simulate a transfer before running it for real.
- Filter names, sizes, dates and attributes. Natural-language parsing accepts Spanish or English; extension and size expressions are usable independently of the interface language.
- Filter photos by contained text with Windows OCR, without a remote AI service.
- Conflicts: ask with thumbnails, replace, skip, newer file, keep both or rename with a pattern. Existing folders offer their own choices.
- Optional duplicate-photo skipping, independent of file names.
- Preserve supported timestamps, attributes, permissions, owners, alternate streams, links, sparse files and long paths.
- Version folders with daily, weekly and monthly retention.
- Deduplicated backup snapshots and restore.
- AES-256 encrypted copies, encrypted ZIP or encrypted 7z archives.
- Secure deletion options: zeroes, random data and supported DoD multi-pass patterns. Actual recoverability depends on the storage medium.

### Behavior and recovery

- On persistent errors: retry, skip, skip following errors or stop; configurable unattended policies.
- New transfers can join existing work or run separately according to disk and queue preferences.
- Optional one-window-per-transfer mode, using the same theme and tray progress indicators.
- Optional local error reports at completion, destination ejection and cleanup of incomplete files.
- Configurable Close and Minimize behavior, window size and position, and small-screen adaptation.
- Native Windows completion notifications, email or webhook notifications when configured, and optional completion actions.

### Synchronization and backups

- One-way mirror, two-way synchronization and live folder synchronization.
- Two-way tracking of deletions and supported renames or moves; replaced items go to the Recycle Bin where supported.
- Side-by-side comparison with per-file action selection before synchronization.
- Deduplicated backups, version retention and restore tools.

### Destinations and connections

- Local disks, USB storage and network shares.
- Phones and cameras over USB/MTP in both directions.
- LocalSend: send to or receive from compatible mobile devices.
- Another PC running MegaCopier on the local network, using its connection code.
- FTP, FTPS, SFTP, WebDAV and S3-compatible services.
- OneDrive, Google Drive and Dropbox through their browser authorization flows; [connection guide — Spanish](docs/Nube.md).
- Server and cloud credentials in Windows Credential Manager.

### Everyday use and customization

- Explorer paste, context menus and left- or right-button drag and drop.
- Multiple destinations, favorites, recent locations and supported file-manager folders.
- Reorder disk queues and file lists, add or remove pending files, and save or load pending transfer lists.
- Queue timeline, drag shelf, scheduling and transfer history with repeat, undo and copy-back actions.
- SMART warnings for supported disks; local-network mobile control protected by a code.
- MCP server (`mcopy mcp`) for compatible assistants, plus HTML/CSV reports and the `mcopy` console.
- **14 languages:** English, Spanish, French, German, Italian, Brazilian Portuguese, Russian, Ukrainian, Polish, Dutch, Turkish, Simplified Chinese, Japanese and Korean.
- **73 themes**, including light and dark variants, gradients and textures; **29 icon packs** that can be combined with any theme; **22 progress bar styles** with independent total, file and queue settings.
- **19 built-in sounds**, Windows or custom sound files, inline volume controls, and optional quiet mode for full-screen games, presentations or Do Not Disturb.
- Windows Settings-style sections, search, contextual explanations, smooth back/forward navigation and mouse side-button support.

## Documentation

| Document | Contents |
|---|---|
| [English changelog](CHANGELOG.en.md) / [Español](CHANGELOG.md) | Complete version history, including unreleased work. |
| [Spanish project page](README.es.md) | The project overview in Spanish. |
| [Privacy](docs/Privacy.md) / [Privacidad](docs/Privacidad.md) | Data, providers and retention limits. |
| [Code-signing policy](docs/Code-signing-policy.md) | Signing status, responsibilities and release approvals. |
| [Security](SECURITY.md) | Private vulnerability reporting and crash-reporting limits. |
| [User guide](docs/Guia.md), [features](docs/Caracteristicas.md), [FAQ](docs/Preguntas-frecuentes.md) | Detailed documentation, currently in Spanish. |
| [Build guide](docs/Compilar.md), [release guide](docs/Versiones.md) | Build and publication instructions, currently in Spanish. |

## Versions and building

Each published version has its own [release page](https://github.com/cbatista96/MegaCopier/releases), packages and notes. The complete history is available in [English](CHANGELOG.en.md) and [Spanish](CHANGELOG.md). To follow new releases, use **Watch > Custom > Releases** on GitHub.

```powershell
git clone https://github.com/cbatista96/MegaCopier.git
cd MegaCopier
.\build.ps1
```

Building requires .NET 10 SDK, Microsoft C++ build tools and Inno Setup for the installer. The script validates translations, runs tests and builds locally. It does **not** publish a GitHub release. See the [build guide — Spanish](docs/Compilar.md).

## Contribute

Bug reports, ideas, translations and code contributions are welcome. See [contribution guide — Spanish](CONTRIBUTING.md). Optional crash reports help investigate failures; suspected security vulnerabilities still need the private reporting route in [SECURITY.md](SECURITY.md).

## License

MegaCopier is free software under **[GNU GPL v3](LICENSE)**. You may use, study, modify and redistribute it under the license terms.
