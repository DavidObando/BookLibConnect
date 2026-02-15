# Book Lib Connect
A standalone Audible downloader and decrypter

[![GitHub All Releases](https://img.shields.io/github/downloads/DavidObando/BookLibConnect/total)](https://github.com/DavidObando/BookLibConnect/releases) [![GitHub](https://img.shields.io/github/license/DavidObando/BookLibConnect)](https://github.com/DavidObando/BookLibConnect/blob/main/LICENSE) [![](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)](http://microsoft.com/windows) [![](https://img.shields.io/badge/language-C%23-blue)](http://csharp.net/) [![GitHub release (latest by date)](https://img.shields.io/github/v/release/DavidObando/BookLibConnect)](https://github.com/DavidObando/BookLibConnect/releases/latest)

![](res/mainwnd.png?raw=true)

![](res/libwnd.png?raw=true)

Book Lib Connect is an Audible downloader app for Windows, macOS, and Linux. Forked from [audiamus/BookLibConnect](https://github.com/audiamus/BookLibConnect).

## Features
- **Free** and **Open Source** software. 
- Direct download from the Audible server.
- Sign-in via standard web browser to register a _device_, app will not see user’s credentials.
- Lists your book library and lets you select titles for download.
- Downloads your books and converts to plain .m4b files.
- Detailed progress monitoring.
- Optionally exports as .aax files.


## Download
Go to the [Releases](https://github.com/DavidObando/BookLibConnect/releases) section of this repository.
 

## Dependencies
Book Lib Connect will run on Windows 64bit, macOS, or Linux. Minimum Windows version is 7. Minimum macOS version is 13 (Ventura).

### Building from source

The solution contains a legacy Windows WinForms client and a cross-platform Avalonia client (Windows, macOS, Linux). Use the solution filter files for targeted builds:

```bash
# Build everything (requires EnableWindowsTargeting on non-Windows)
dotnet build "AaxAudioConverter 2.x.sln"

# Build only the cross-platform Avalonia projects
dotnet build BookLibConnect.Avalonia.slnf

# Build only Windows WinForms projects
dotnet build BookLibConnect.Windows.slnf

# Run the Avalonia app
dotnet run --project Connect.app.avalonia.core/Connect.app.avalonia.core.csproj

# Publish for macOS (Apple Silicon)
dotnet publish Connect.app.avalonia.core/Connect.app.avalonia.core.csproj \
  -r osx-arm64 -c Release --self-contained -p:PublishTrimmed=true

# Publish for macOS (Intel)
dotnet publish Connect.app.avalonia.core/Connect.app.avalonia.core.csproj \
  -r osx-x64 -c Release --self-contained -p:PublishTrimmed=true

# Publish for Windows
dotnet publish Connect.app.avalonia.core/Connect.app.avalonia.core.csproj \
  -r win-x64 -c Release --self-contained -p:PublishTrimmed=true

# Publish for Linux
dotnet publish Connect.app.avalonia.core/Connect.app.avalonia.core.csproj \
  -r linux-x64 -c Release --self-contained -p:PublishTrimmed=true
```

### Build scripts

Platform-specific build scripts are provided in the `build/` directory:

```bash
# macOS — creates .app bundle + DMG (with optional code signing and notarization)
./build/build-macos.sh

# Windows — publishes WinForms app (with optional Inno Setup installer)
./build/build-windows.ps1

# Windows — publishes Avalonia app (with IL trimming)
./build/build-windows.ps1 -AvaloniaApp

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
  SystemMgmt.core         — Windows hardware ID (WMI)
  SystemMgmt.mac.core     — macOS hardware ID (sysctl/IOKit)
  SystemMgmt.linux.core   — Linux hardware ID (DMI/machine-id)
  Connect.ui.avalonia.core — Avalonia MVVM ViewModels + Views
  Connect.app.avalonia.core — Avalonia application entry point

Legacy Windows-only (WinForms):
  AuxWin32Lib.core        — Win32 file I/O, registry
  AuxWin.DialogBox.core   — Win32 dialog hooks
  AuxWin.lib.core         — WinForms helpers
  PropGridLib.core        — WinForms PropertyGrid
  Connect.ui.lib.core     — WinForms UI controls
  Connect.app.gui.core    — WinForms application
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