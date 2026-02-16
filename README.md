# Oahu
A standalone Audible downloader and decrypter

[![GitHub All Releases](https://img.shields.io/github/downloads/DavidObando/Oahu/total)](https://github.com/DavidObando/Oahu/releases) [![GitHub](https://img.shields.io/github/license/DavidObando/Oahu)](https://github.com/DavidObando/Oahu/blob/main/LICENSE) [![](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)](http://microsoft.com/windows) [![](https://img.shields.io/badge/language-C%23-blue)](http://csharp.net/) [![GitHub release (latest by date)](https://img.shields.io/github/v/release/DavidObando/Oahu)](https://github.com/DavidObando/Oahu/releases/latest)


Oahu is an Audible downloader app for Windows, macOS, and Linux. Forked from [audiamus/BookLibConnect](https://github.com/audiamus/BookLibConnect).

## Features
- **Free** and **Open Source** software. 
- Direct download from the Audible server.
- Sign-in via standard web browser to register a _device_, app will not see user’s credentials.
- Lists your book library and lets you select titles for download.
- Downloads your books and converts to plain .m4b files.
- Detailed progress monitoring.
- Optionally exports as .aax files.


## Download
Go to the [Releases](https://github.com/DavidObando/Oahu/releases) section of this repository.
 

## Dependencies
Oahu will run on Windows 64bit, macOS, or Linux. Minimum Windows version is 7. Minimum macOS version is 13 (Ventura).

### Building from source

The repository now ships a single cross-platform Avalonia client (Windows, macOS, Linux).

```bash
# Build the full solution
dotnet build src/Oahu.sln

# Run the Avalonia app
dotnet run --project src/Connect.app.avalonia.core/Connect.app.avalonia.core.csproj

# Publish for macOS (Apple Silicon)
dotnet publish src/Connect.app.avalonia.core/Connect.app.avalonia.core.csproj \
  -r osx-arm64 -c Release --self-contained -p:PublishTrimmed=true

# Publish for macOS (Intel)
dotnet publish src/Connect.app.avalonia.core/Connect.app.avalonia.core.csproj \
  -r osx-x64 -c Release --self-contained -p:PublishTrimmed=true

# Publish for Windows
dotnet publish src/Connect.app.avalonia.core/Connect.app.avalonia.core.csproj \
  -r win-x64 -c Release --self-contained -p:PublishTrimmed=true

# Publish for Linux
dotnet publish src/Connect.app.avalonia.core/Connect.app.avalonia.core.csproj \
  -r linux-x64 -c Release --self-contained -p:PublishTrimmed=true
```

### Build scripts

Platform-specific build scripts are provided in the `build/` directory:

```bash
# macOS — creates .app bundle + DMG (with optional code signing and notarization)
./build/build-macos.sh

# Windows — publishes Avalonia app (with optional Inno Setup installer)
./build/build-windows.ps1

# Linux — publishes Avalonia app and creates tarball
./build/build-linux.sh
```

### Project Architecture

```
Shared (platform-neutral):
  CommonTypes.lib.core    — Interfaces and enums
  AuxLib.core             — Utilities, logging, settings
  Audible.json.core       — Audible API JSON models
  BooksDatabase.core      — EF Core + SQLite database
  TreeDecomposition.core  — Diagnostics
  CommonUtil.lib.core     — File operations, online update
  Connect.lib.core        — Core business logic (Audible API, library, auth)

Cross-platform Avalonia app (Windows, macOS, Linux):
  AuxWin32Lib.core         — Win32 file I/O helper (used conditionally)
  SystemMgmt.core         — Windows hardware ID (WMI)
  SystemMgmt.mac.core     — macOS hardware ID (sysctl/IOKit)
  SystemMgmt.linux.core   — Linux hardware ID (DMI/machine-id)
  Connect.ui.avalonia.core — Avalonia MVVM ViewModels + Views
  Connect.app.avalonia.core — Avalonia application entry point
```

## Acknowledgments
- [audiamus](https://github.com/audiamus/BookLibConnect) for his original implementation of BookLibConnect. This repository is a fork of audiamus' work.
- [mkb79](https://github.com/mkb79/Audible) for his Python library which served as the reference implementation of the Audible API to me, straightforward and easy to follow. 
- [Mbucari](https://github.com/Mbucari/AAXClean) for his Audible decryption library in C#. While recent FFmpeg releases can also do it, it is much more convenient to have an in-process solution.
- [rmcrackan](https://github.com/rmcrackan/AudibleApi) for _the other_ C# implementation of an Audible client library, absolutely worth the occcasional side glance.


## Anti-Piracy Notice
Note that this software does not ‘crack’ the DRM or circumvent it in any other way. The application applies account and book specific keys, retrieved directly from the Audible server via the user’s personal account, to decrypt the audiobook in the same manner as the official audiobook playing software does. 
Please only use this application for gaining full access to your own audiobooks for archiving/conversion/convenience. De-DRMed audiobooks must not be uploaded to open servers, torrents, or other methods of mass distribution. No help will be given to people doing such things. Authors, retailers and publishers all need to make a living, so that they can continue to produce audiobooks for us to listen to and enjoy.

(*This blurb is borrowed from https://github.com/KrumpetPirate/AAXtoMP3 and https://apprenticealf.wordpress.com/*).