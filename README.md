# Oahu
A standalone Audible downloader and decrypter

[![GitHub All Releases](https://img.shields.io/github/downloads/DavidObando/Oahu/total)](https://github.com/DavidObando/Oahu/releases) [![GitHub](https://img.shields.io/github/license/DavidObando/Oahu)](https://github.com/DavidObando/Oahu/blob/main/LICENSE) [![](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)](http://microsoft.com/windows) [![](https://img.shields.io/badge/language-C%23-blue)](http://csharp.net/) [![GitHub release (latest by date)](https://img.shields.io/github/v/release/DavidObando/Oahu)](https://github.com/DavidObando/Oahu/releases/latest)

![](src/Oahu.App/Resources/audio.png?raw=true)

Oahu is an Audible downloader app for Windows, macOS, and Linux. Forked from [audiamus/BookLibConnect](https://github.com/audiamus/BookLibConnect).

## Features
- **Free** and **Open Source** software. 
- Direct download from the Audible server.
- Sign-in via standard web browser to register a _device_, app will not see user’s credentials.
- Lists your book library and lets you select titles for download.
- Downloads your books and converts to plain .m4b files.
- Detailed progress monitoring.
- Optionally exports as .aax files.

## Clients

Oahu ships two independent front-ends — a graphical app and a terminal app — both built on the same core libraries.

### GUI (Avalonia)

The cross-platform desktop app (`Oahu.App`) provides a traditional graphical interface for browsing your library, queuing downloads, and monitoring progress.

### CLI + TUI (`oahu-cli`)

A single binary that operates in three modes:

| Mode | Invocation | Description |
|------|-----------|-------------|
| **TUI** | `oahu-cli` or `oahu-cli tui` | Full-screen interactive terminal UI (Spectre.Console) — browse library, queue downloads, monitor jobs, inspect history. |
| **Command** | `oahu-cli <subcommand>` | Scriptable one-shot commands with JSON output, exit codes, and pipe-friendly behaviour. |
| **Server** | `oahu-cli serve` | Long-lived process exposing MCP (stdio) and/or loopback HTTP+SSE transports for AI hosts and local tooling. |

#### Available commands

```
oahu-cli auth login|logout|status   Manage Audible authentication
oahu-cli library list|sync          Browse and refresh your book library
oahu-cli download <ASIN…>          Download and decrypt books
oahu-cli convert <file…>           Convert .aax/.aaxc to .m4b
oahu-cli queue add|list|remove      Manage the download queue
oahu-cli history                    View download/conversion history
oahu-cli config get|set|list        View and update settings
oahu-cli doctor                     Diagnose environment issues
oahu-cli serve --mcp|--http         Start the MCP/HTTP integration server
oahu-cli completion                 Generate shell completions
```

#### MCP / HTTP server

The `serve` command exposes the full CLI surface for programmatic use:

- **MCP-stdio** — for AI assistants (Claude Desktop, Continue, etc.)
- **Loopback HTTP REST + SSE** — for scripts, automation, and local tooling (defaults to `127.0.0.1:8765`)

```sh
# Stdio MCP (for AI hosts)
oahu-cli serve --mcp --unattended

# Loopback HTTP
oahu-cli serve --http

# Both transports at once
oahu-cli serve --mcp --http
```


## Download
Go to the [Releases](https://github.com/DavidObando/Oahu/releases) section of this repository to download installers for Windows (arm64, x64), DMG images for macOS (arm64, x64), or tarballs with the compiled binaries for Linux and macOS (arm64, x64).

### Install via Homebrew (macOS / Linux)

```bash
brew tap DavidObando/oahu https://github.com/DavidObando/Oahu
brew install oahu
```
### Install via WinGet (Windows)

```powershell
winget install DavidObando.Oahu
```

## Dependencies
Oahu will run on Windows 64bit, macOS, or Linux. Minimum Windows version is 7. Minimum macOS version is 13 (Ventura).

### Building from source

The repository ships a cross-platform Avalonia GUI client and a CLI/TUI client (Windows, macOS, Linux).

```bash
# Build the full solution
dotnet build Oahu.sln

# Run the Avalonia app
dotnet run --project src/Oahu.App/Oahu.App.csproj

# Run the CLI (launches TUI by default)
dotnet run --project src/Oahu.Cli/Oahu.Cli.csproj

# Run a CLI subcommand
dotnet run --project src/Oahu.Cli/Oahu.Cli.csproj -- library list --json

# Publish for macOS (Apple Silicon)
dotnet publish src/Oahu.App/Oahu.App.csproj \
  -r osx-arm64 -c Release --self-contained

# Publish for macOS (Intel)
dotnet publish src/Oahu.App/Oahu.App.csproj \
  -r osx-x64 -c Release --self-contained

# Publish for Windows
dotnet publish src/Oahu.App/Oahu.App.csproj \
  -r win-x64 -c Release --self-contained

# Publish for Linux
dotnet publish src/Oahu.App/Oahu.App.csproj \
  -r linux-x64 -c Release --self-containe
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

The solution is organized into 11 projects:

| Project | Depends on | Description |
|---|---|---|
| `Oahu.Foundation` | — | Utilities, logging, settings, diagnostics, shared types, IO |
| `Oahu.Decrypt` | — | AAX/AAXC decryption (MP4/MPEG-4 parsing, frame filters, crypto) |
| `Oahu.Data` | Foundation | EF Core + SQLite database (entities, migrations) |
| `Oahu.Core` | Decrypt, Foundation, Data | Core business logic (Audible API, auth, library, download/decrypt) |
| `Oahu.SystemManagement` | Foundation | Platform-specific hardware ID providers (Windows, macOS, Linux) |
| `Oahu.UI` | Foundation, Data, Core | Avalonia MVVM ViewModels + Views |
| `Oahu.App` | Foundation, Data, Core, UI, SystemManagement | GUI application entry point (Avalonia) |
| `Oahu.Cli.App` | Foundation, Data, Core, SystemManagement | CLI use-case layer (auth, library, queue, jobs, config) — headless, fully testable |
| `Oahu.Cli.Tui` | Cli.App | TUI design system over Spectre.Console (widgets, screens, themes, keymap) |
| `Oahu.Cli.Server` | Cli.App | MCP + loopback HTTP/SSE server for AI hosts and local tooling |
| `Oahu.Cli` | Cli.App, Cli.Tui, Cli.Server | CLI entry point, argument parsing (System.CommandLine), command handlers |

## Acknowledgments
- [audiamus](https://github.com/audiamus/BookLibConnect) for his original implementation of BookLibConnect. This repository is a fork of audiamus' work.
- [mkb79](https://github.com/mkb79/Audible) for his Python library which served as the reference implementation of the Audible API to me, straightforward and easy to follow. 
- [Mbucari](https://github.com/Mbucari/AAXClean) for his Audible decryption library in C#. While recent FFmpeg releases can also do it, it is much more convenient to have an in-process solution.
- [rmcrackan](https://github.com/rmcrackan/AudibleApi) for _the other_ C# implementation of an Audible client library, absolutely worth the occcasional side glance.


## Anti-Piracy Notice
Note that this software does not ‘crack’ the DRM or circumvent it in any other way. The application applies account and book specific keys, retrieved directly from the Audible server via the user’s personal account, to decrypt the audiobook in the same manner as the official audiobook playing software does. 
Please only use this application for gaining full access to your own audiobooks for archiving/conversion/convenience. De-DRMed audiobooks must not be uploaded to open servers, torrents, or other methods of mass distribution. No help will be given to people doing such things. Authors, retailers and publishers all need to make a living, so that they can continue to produce audiobooks for us to listen to and enjoy.

(*This blurb is borrowed from https://github.com/KrumpetPirate/AAXtoMP3 and https://apprenticealf.wordpress.com/*).
