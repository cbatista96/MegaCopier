[Español](README.md) · **English** · Changelog: [English](CHANGELOG.en.md) / [Español](CHANGELOG.md)

<div align="center">

<img src="src/MegaCopier.App/Assets/MegaCopier.ico" width="96" alt="MegaCopier icon">

# MegaCopier

**The Windows 11 file copier that resumes after a power cut.**

Replace File Explorer's copy and paste. If a transfer is interrupted by a power cut, crash or disconnected drive, resume from the last verified block, even inside a large file. Verify and repair copies, synchronize folders, create backups, and transfer to servers, cloud storage and phones.

[![Build](https://github.com/cbatista96/MegaCopier/actions/workflows/ci.yml/badge.svg)](https://github.com/cbatista96/MegaCopier/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/cbatista96/MegaCopier)](https://github.com/cbatista96/MegaCopier/releases/latest)
[![GPL v3](https://img.shields.io/badge/license-GPLv3-blue.svg)](LICENSE)

### [Download MegaCopier](https://github.com/cbatista96/MegaCopier/releases/latest)

<img src="docs/capturas/completa-oscuro.png" width="820" alt="MegaCopier's full transfer view; screenshot in Spanish">

</div>

## Current development status

The default branch includes development changes that have **not been released** as a new downloadable version. Start with the [Unreleased highlights and complete notes](CHANGELOG.en.md#unreleased), including screenshots. The latest download remains the last published release; it does not necessarily contain everything described in the development notes below.

## Main features

| Feature | What it does |
|---|---|
| Resume interrupted transfers | Record verified blocks and resume within a file instead of restarting the whole transfer. |
| Verification and repair | Read back the destination, compare checksums such as BLAKE3, SHA-256 and xxHash, and repair damaged blocks. |
| Transfer controls | Pause and resume transfers, manage queues and speed limits, and review file conflicts before replacing files. |
| Synchronization | One-way, two-way and real-time folder synchronization. |
| Backups and recovery | Deduplicated backups, version retention, ransomware protection and recovery data. |
| Remote destinations | FTP, SFTP, WebDAV, S3, OneDrive, Google Drive, Dropbox, LocalSend, and phones or cameras over USB/MTP. |
| Windows integration | File Explorer copy/paste, context menus, drag and drop, optional startup with Windows and tray controls. |
| Appearance and language | 73 themes, 22 progress bar styles, 29 icon packs, 19 built-in sounds and 14 interface languages. |
| Command line | The included `mcopy` console supports scripts and automation. |

## Download and install

Choose a package on the [latest release page](https://github.com/cbatista96/MegaCopier/releases/latest):

| File | Purpose |
|---|---|
| `MegaCopier-Setup-<version>.exe` | Per-user installer; the usual choice. No administrator rights are required. |
| `MegaCopier-<version>-portable.zip` | Extract anywhere and run `MegaCopier.exe`. Settings stay in the adjacent `Data` folder. |
| `mcopy-<version>.exe` | Standalone command-line tool. The installer already includes it. |
| `SHA256SUMS.txt` | SHA-256 checksums for the downloaded files. |

Requires **64-bit Windows 11**. The application includes its runtime; installing .NET separately is unnecessary. Local transfers do not require an Internet connection or an account.

The installer offers optional shortcuts and Explorer integration. In the current development version, automatic update checking and error reporting are selected by default; automatic download and installation are off. You can change those choices. Silent updates preserve your reporting preference.

Current public executables and installers are **not Authenticode-signed**. Windows may display a SmartScreen warning. Compare the downloaded file's SHA-256 checksum with `SHA256SUMS.txt`; a Windows code-signing certificate is still pending review.

```powershell
Get-FileHash .\MegaCopier-Setup-1.7.1.exe -Algorithm SHA256
```

## Updates in the development version

Open **Settings > Updates** to check manually, download an available version, then install it. The main button changes with the update's state and keeps its size. Highlights describe the available version, or the installed version when no newer release is known. Open the complete notes or update history to see details and images.

- Automatic checking runs every **48 hours** when enabled, catching up after Internet access returns if overdue.
- Automatic download is a separate, optional setting and applies to versions found by an automatic check. A manual check offers the Download action.
- Downloads default to Wi-Fi and Ethernet connections without metering. Mobile and metered connections require permission.
- Optional automatic installation waits until MegaCopier is in the tray without active or pending work or dialogs. A completion notification leads back to Updates.
- Update packages are verified against an **ECDSA P-256 signed manifest and SHA-256** before installation. The public verification key is included in the application; the private signing key is never distributed.
- Cached results and release notes remain available offline. A network failure does not discard a known available version.
- Portable copies download a ZIP instead of replacing themselves through the installed application's update process.

The app displays release notes in Spanish for the Spanish interface and in English for all other interface languages. Both histories include previous versions. GitHub documents have explicit language links; selecting the English interface on GitHub does not translate the project's written content.

## Optional error reports and privacy

The current development installer offers **Send error reports**, selected by default with an explanation and an option to deselect it. Portable copies start with reporting disabled. Change the setting in **Settings > Advanced**.

With automatic reporting enabled, filtered reports are sent in the background. Otherwise, an unexpected-exit prompt lets you review the exact report, send it once, or authorize future reports. A power outage can also leave an unexpected-exit marker.

Reports include application and Windows versions, language, theme, coarse activity and filtered diagnostic traces. They exclude file contents, file names, paths and free-form exception messages. Cloudflare processes the connection IP for abuse limiting; the IP is excluded from the report stored in the private GitHub repository. Local unfiltered logs are not uploaded automatically. Disabling reporting removes queued local reports, not reports already received.

Read the [privacy policy](docs/Privacy.md) for data, providers, retention and deletion limits. The receiver's source and deployment instructions are in [tools/informes-worker](tools/informes-worker/README.md) (Spanish).

## Code signing policy

The updater uses the project's own ECDSA key. **The SignPath Foundation application was submitted on October 21, 2026; approval and Authenticode certificate access remain pending.** No sponsorship is claimed as granted. See the [code-signing policy](docs/Code-signing-policy.md), [privacy policy](docs/Privacy.md), and [signature setup status](docs/Firmas.md) (Spanish).

## Quick start

1. Copy or cut files in File Explorer, then paste into the destination as usual.
2. Follow progress in the compact window; expand it to see the file list.
3. After an interruption, reopen MegaCopier and choose Resume for the interrupted transfer.
4. Use the tray icon to open MegaCopier or disable its Explorer interception.

```text
mcopy copy D:\Photos E:\Backup --verify
mcopy move D:\Downloads\Videos E:\Videos
mcopy copy D:\Work F:\Mirror --mirror
mcopy backup D:\Documents F:\Backups
```

## Build from source

```powershell
git clone https://github.com/cbatista96/MegaCopier.git
cd MegaCopier
.\build.ps1
```

Building requires the .NET 10 SDK, Microsoft C++ build tools and Inno Setup for the installer. The script generates and validates localized resources, runs tests, and builds the application, Explorer extension, console and installer locally. It does **not** publish a GitHub release. See the [build guide](docs/Compilar.md) (Spanish).

## Documentation and contributions

- **[Complete English changelog](CHANGELOG.en.md)** / [Spanish changelog](CHANGELOG.md), including unreleased changes and screenshots. Screenshots may show the Spanish interface; the surrounding English text explains each change.
- [User guide](docs/Guia.md), [features](docs/Caracteristicas.md), [FAQ](docs/Preguntas-frecuentes.md) and [release instructions](docs/Versiones.md): currently in Spanish.
- [Contributing](CONTRIBUTING.md) and [security reporting](SECURITY.md): currently in Spanish. Report security issues privately.
- [GNU GPL v3 license](LICENSE): you may use, study, modify and redistribute MegaCopier under its license terms.

To follow published versions, use **Watch > Custom > Releases** on GitHub. No new release is created simply by updating the source code on the default branch.
