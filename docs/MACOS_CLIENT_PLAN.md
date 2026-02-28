# macOS Client Plan (Archived)

## Status

Completed. Oahu now uses a single cross-platform Avalonia client for macOS, Windows, and Linux.

This file is kept as an archive marker and intentionally does not contain the old migration checklist.

## Current macOS Build Path

Use the shared Avalonia solution and app project:

```bash
dotnet restore Oahu.sln
dotnet build Oahu.sln
dotnet run --project src/Connect.app.avalonia.core/Connect.app.avalonia.core.csproj
```

Or use the build script:

```bash
./build/build-macos.sh
```

## Current Project References

macOS app/runtime depends on:

- `Connect.app.avalonia.core`
- `Connect.ui.avalonia.core`
- `SystemMgmt.mac.core`
- shared core libraries under `src/`

## Removed Legacy Artifacts

The following were removed from the repository:

- WinForms client/UI projects (`Connect.app.gui.core`, `Connect.ui.lib.core`, `AuxWin.lib.core`, `AuxWin.DialogBox.core`, `PropGridLib.core`)
- Solution filter files (`*.slnf`)
