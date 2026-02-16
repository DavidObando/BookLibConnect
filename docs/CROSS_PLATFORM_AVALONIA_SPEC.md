# Cross-Platform Avalonia Spec (Current State)

## Status

The migration to a single Avalonia client is complete.

- Legacy WinForms projects were removed from the repository.
- Solution filter files (`*.slnf`) were removed.
- The canonical solution is `src/Oahu.sln`.

## Active App Architecture

### Application

- `Connect.app.avalonia.core` — Avalonia desktop entry point
- `Connect.ui.avalonia.core` — Avalonia views/view-models and UI support

### Shared Core Libraries

- `Connect.lib.core` — business logic
- `BooksDatabase.core` — EF Core / SQLite data layer
- `Audible.json.core` — Audible API JSON models
- `CommonUtil.lib.core` — file/update utilities
- `CommonTypes.lib.core` — shared interfaces and enums
- `AuxLib.core` — utilities/logging/settings
- `TreeDecomposition.core` — diagnostics helpers

### Platform-Specific Support

- `SystemMgmt.core` — Windows hardware ID provider
- `SystemMgmt.mac.core` — macOS hardware ID provider
- `SystemMgmt.linux.core` — Linux hardware ID provider
- `AuxWin32Lib.core` — Win32 file I/O helper used conditionally on Windows

## Build and CI

### Solution

Use this solution for restore/build across local and CI workflows:

```bash
dotnet restore src/Oahu.sln
dotnet build src/Oahu.sln
```

### Platform Build Scripts

- `build/build-macos.sh`
- `build/build-windows.ps1`
- `build/build-linux.sh`

All three scripts publish the Avalonia app.

## Notes

This document supersedes prior planning material that referenced:

- WinForms projects (`Connect.app.gui.core`, `Connect.ui.lib.core`, `AuxWin.lib.core`, `AuxWin.DialogBox.core`, `PropGridLib.core`)
- legacy solution filters (`Oahu.Avalonia.slnf`, `Oahu.Windows.slnf`, `Oahu.macOS.slnf`)
