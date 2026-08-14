# Ghostlist

<p align="center">
  <img src="Ghostlist.App/Assets/ghostlist-icon.png" width="180" alt="Ghostlist neon application icon" />
</p>

**Ghostlist finds and safely removes broken entries from the Windows Installed apps list.**

Windows can keep showing an application after its files or uninstaller have already disappeared. Clicking **Uninstall** then produces a “Windows cannot find…” error because the registered `unins000.exe`, setup program, or other uninstall target no longer exists. Ghostlist scans those registrations, explains what is wrong, and lets you remove only confirmed orphaned entries after creating a restorable backup.

![A broken Windows uninstall entry whose uninstaller no longer exists](assets/ornek-bozuk-kaldirma-kaydi.png)

## Install with one PowerShell command

Open PowerShell and run:

```powershell
irm https://raw.githubusercontent.com/Teknesyum/Ghostlist/main/scripts/install.ps1 | iex
```

The installer downloads the latest Windows x64 release, installs Ghostlist to `%LOCALAPPDATA%\Programs\Ghostlist`, creates a **Ghostlist** desktop shortcut, and opens the application. Administrator rights are not required for installation; fixing machine-wide `HKLM` entries may require running Ghostlist as administrator.

## What Ghostlist does

- Scans per-user and machine-wide uninstall registrations in both 32-bit and 64-bit Registry views.
- Resolves uninstall commands without executing them.
- Classifies entries as **Healthy**, **Broken**, **Suspicious**, or **Unsupported**.
- Detects missing Inno Setup uninstallers such as `unins000.exe` and other missing executable targets.
- Shows the exact missing path and Registry location before any change is made.
- Fixes individually selected broken entries or all confirmed broken entries at once.
- Creates a separate JSON backup before removing every Registry entry.
- Restores backups from the application when needed.
- Protects MSI packages and Windows system components from automatic cleanup.

Ghostlist never deletes application folders, game files, documents, saves, or other personal data. It does not run uninstall commands. A “fix” removes only the selected orphaned entry from the Windows Installed apps list.

## Safety model

Ghostlist offers a fix only when an ordinary executable uninstall target can be resolved and that file is confirmed missing. Entries managed by Windows Installer (`msiexec`), entries marked as system components, and commands that cannot be parsed safely remain protected.

Every change requires confirmation and is backed up first. Backups are stored under:

```text
%LOCALAPPDATA%\Ghostlist\Backups
```

Registry cleanup still deserves care. Review the displayed application name, missing target, and Registry path before confirming a fix.

## Requirements

- Windows 10 or Windows 11, x64.
- PowerShell 5.1 or newer for installation.

Ghostlist is distributed as a self-contained application and does not require a separate .NET installation.

## Uninstall Ghostlist

Run:

```powershell
& "$env:LOCALAPPDATA\Programs\Ghostlist\uninstall.ps1"
```

Removing Ghostlist does not delete Registry backups under `%LOCALAPPDATA%\Ghostlist\Backups`.

## Build from source

Requirements: Windows x64 and the .NET 8 SDK.

```powershell
git clone https://github.com/Teknesyum/Ghostlist.git
cd Ghostlist
dotnet test Ghostlist.sln -c Release
dotnet publish Ghostlist.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Release

Current version: **v1.0.0**  
Download: [Ghostlist v1.0.0 for Windows x64](https://github.com/Teknesyum/Ghostlist/releases/tag/v1.0.0)

---

## Support

This application is built in spare time and is free.

<a href="https://github.com/sponsors/Teknesyum"><img src="https://img.shields.io/badge/Buy_me_a_coffee-b026ff?style=for-the-badge&logo=githubsponsors&logoColor=b026ff&labelColor=0d0d0f" alt="Sponsor" /></a>

**[github.com/Teknesyum](https://github.com/Teknesyum)** · [MIT](LICENSE)

