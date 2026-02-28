# Oahu Project Simplification Plan

## Current State

The project has **13 .csproj files** (12 active + 1 dead folder) for approximately **110 source .cs files**. Many projects are tiny (40–400 lines) and exist as separate assemblies without a compelling reason — there are no tests, no separate deployment, and no independent versioning.

### Current Projects

| # | Project | Purpose | Files | Lines (approx) | Dependencies |
|---|---------|---------|-------|-----------------|--------------|
| 1 | CommonTypes.lib.core | Shared interfaces/enums | 3 | ~40 | None |
| 2 | AuxLib.core | General utilities (logging, settings, encryption, etc.) | ~30 | ~1000 | None |
| 3 | AuxWin32Lib.core | Win32 file I/O, file associations | 2 | ~430 | None |
| 4 | TreeDecomposition.core | Object-to-text diagnostic dumper | 8 | ~450 | AuxLib |
| 5 | Audible.json.core | Audible API JSON models | 5 | ~420 | AuxLib |
| 6 | BooksDatabase.core | EF Core + SQLite data layer | 7+11 migrations | ~600 | AuxLib, CommonTypes |
| 7 | CommonUtil.lib.core | File copy, progress, update types | 7 | ~430 | AuxLib, AuxWin32Lib, TreeDecomposition |
| 8 | Connect.lib.core | Core business logic (API, auth, download) | ~21 | ~2500 | Audible.json, AuxLib, BooksDatabase, CommonTypes, CommonUtil, TreeDecomposition |
| 9 | SystemMgmt.core | Windows hardware ID (WMI) | 3 | ~360 | CommonTypes |
| 10 | SystemMgmt.mac.core | macOS hardware ID | 1 | ~120 | CommonTypes |
| 11 | SystemMgmt.linux.core | Linux hardware ID | 1 | ~110 | CommonTypes |
| 12 | Connect.ui.avalonia.core | Avalonia MVVM Views & ViewModels | 14+6 axaml | ~1500 | AuxLib, BooksDatabase, CommonTypes, Connect.lib |
| 13 | Connect.app.avalonia.core | Application entry point | 7+3 axaml | ~500 | 10 project references |
| — | PropGridLib.core | **DEAD** (empty folder, only obj/) | 0 | 0 | — |

### Current Dependency Graph

```
Layer 0 (leaves):   CommonTypes.lib.core, AuxLib.core, AuxWin32Lib.core
Layer 1:            TreeDecomposition.core, Audible.json.core, BooksDatabase.core,
                    SystemMgmt.core, SystemMgmt.mac.core, SystemMgmt.linux.core
Layer 2:            CommonUtil.lib.core
Layer 3:            Connect.lib.core
Layer 4:            Connect.ui.avalonia.core
Layer 5 (app):      Connect.app.avalonia.core
```

### Key Observations

1. **No tests exist** — no test projects anywhere.
2. **PropGridLib.core is dead** — empty folder with only `obj/`, not in the solution.
3. **FileAssociations class is dead code** — in AuxWin32Lib.core, referenced nowhere.
4. **AuxWin32Lib.core** exists only to serve `FileEx.Copy()` on Windows via runtime check.
5. **Audible.json.core** is consumed exclusively by Connect.lib.core.
6. **CommonUtil.lib.core** is consumed exclusively by the Connect layer.
7. **All 3 SystemMgmt projects** are consumed exclusively by the app entry point.
8. **TreeDecomposition.core** is lightly used — only 2 consumers, diagnostic-only.
9. **CommonTypes.lib.core** is ~40 lines of interfaces/enums used as shared contracts.
10. **The app project** has 10 direct project references, many of which are redundant transitive deps.

---

## Target State

**5 projects**, strictly layered:

```
src/
  Oahu.sln
  Directory.Build.props
  Oahu.Foundation/         ← Utilities, logging, IO, diagnostics, shared types
  Oahu.Data/               ← EF Core + SQLite database + migrations
  Oahu.Core/               ← Business logic + Audible API models
  Oahu.UI/                 ← Avalonia Views + ViewModels
  Oahu.App/                ← Entry point + platform-specific hardware ID
  InnoSetup/               ← Windows installer script (unchanged)
```

### New Dependency Graph

```
Oahu.Foundation        (leaf — no project deps)
     ↑
Oahu.Data              → Oahu.Foundation
     ↑
Oahu.Core              → Oahu.Foundation, Oahu.Data
     ↑
Oahu.UI                → Oahu.Foundation, Oahu.Data, Oahu.Core
     ↑
Oahu.App               → Oahu.Foundation, Oahu.Data, Oahu.Core, Oahu.UI
```

---

## Project Details

### Oahu.Foundation (merges 5 projects)

**Absorbs**: AuxLib.core, AuxWin32Lib.core, TreeDecomposition.core, CommonUtil.lib.core, CommonTypes.lib.core

**Subdirectory organization**:
```
Oahu.Foundation/
  Oahu.Foundation.csproj
  Platform/                ← IHardwareIdProvider, ERegion, IBookMeta, IAudioQuality
  Logging/                 ← Logging, LogGuard
  Settings/                ← SettingsManager, JsonSerialization
  IO/                      ← Win32FileIO, FileEx, BigEndianReader
  Diagnostics/             ← TreeDecomposition classes, ToStringConverters
  Process/                 ← ProcessHost, ProcessList, ShellExecute
  Crypto/                  ← SymmetricEncryptor, crc
  Threading/               ← AffineSynchronizationContext, ExtensionsSyncContext, ThreadProgress
  Interaction/             ← IInteractionCallback, InteractionCallback, InteractionMessage
  Utilities/               ← Temp, TimeUtil, Indent, EnumUtil, ChainPunctuation, extensions, etc.
  Maintenance/             ← LogTmpFileMaintenance
  Update/                  ← Records (PackageInfo), Settings (IUpdateSettings), enums (EOnlineUpdate)
```

**Settings**:
- `AllowUnsafeBlocks: true` (for Win32FileIO)
- `NoWarn: CA1416` (platform compatibility — runtime-guarded)
- No NuGet package dependencies

### Oahu.Data (rename of BooksDatabase.core)

**Contents**: All files from BooksDatabase.core, unchanged.

**References**: Oahu.Foundation (replaces AuxLib.core + CommonTypes.lib.core)

**NuGet**: `Microsoft.EntityFrameworkCore.Sqlite`, `.Proxies`, `.Design`, `.Tools`

### Oahu.Core (merges 2 projects)

**Absorbs**: Connect.lib.core, Audible.json.core

**Subdirectory organization**:
```
Oahu.Core/
  Oahu.Core.csproj
  Api/                     ← AudibleApi, AudibleClient, Authorize, Login, HttpClientEx
  Models/                  ← LibraryResponse, LicenseResponse, RegistrationResponse, Voucher, Serialization
  Library/                 ← BookLibrary, AaxExporter, ReducedChoicesDownloadQuality
  Pipeline/                ← DownloadDecryptJob
  Profile/                 ← Profile, AccountAlias, Anonymizer
  (root files)             ← Delegates, enums, Interfaces, Records, Settings, Localization, extensions, ConsoleExternalLoginCallback
```

**References**: Oahu.Foundation, Oahu.Data

**NuGet**: `AAXClean`, `HtmlAgilityPack`

### Oahu.UI (rename of Connect.ui.avalonia.core)

**Contents**: All files from Connect.ui.avalonia.core.

**References**: Oahu.Foundation, Oahu.Data, Oahu.Core

**NuGet**: `Avalonia`, `Avalonia.Controls.DataGrid`, `Avalonia.Themes.Fluent`, `CommunityToolkit.Mvvm`

### Oahu.App (merges 4 projects)

**Absorbs**: Connect.app.avalonia.core, SystemMgmt.core, SystemMgmt.mac.core, SystemMgmt.linux.core

**Subdirectory organization**:
```
Oahu.App/
  Oahu.App.csproj
  Program.cs
  App.axaml / App.axaml.cs
  MainWindow.axaml / MainWindow.axaml.cs
  Platform/
    Windows/               ← HardwareId.cs, MotherboardInfo.cs, WinHardwareIdProvider.cs
    Mac/                   ← MacHardwareIdProvider.cs
    Linux/                 ← LinuxHardwareIdProvider.cs
  Assets/                  ← Icons (ico, icns, png)
  (other app files)
```

**References**: Oahu.Foundation, Oahu.Data, Oahu.Core, Oahu.UI

**NuGet**: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Fonts.Inter`, `Avalonia.Themes.Fluent`, `System.Management`

---

## Implementation Steps

### Step 1: Delete dead code
- Remove `src/PropGridLib.core/` directory entirely
- Remove `FileAssociation.cs` from AuxWin32Lib.core (unused)

### Step 2: Create Oahu.Foundation
- Create `src/Oahu.Foundation/` directory with subdirectories
- Create `Oahu.Foundation.csproj` with `AllowUnsafeBlocks` and `NoWarn` settings
- Move all .cs files from the 5 source projects into organized subdirectories
- Update all `namespace` declarations to `Oahu.Foundation.*` sub-namespaces
- Update all internal `using` statements

### Step 3: Create Oahu.Data
- Rename `src/BooksDatabase.core/` to `src/Oahu.Data/`
- Create new `Oahu.Data.csproj` referencing Oahu.Foundation
- Update namespace from `Oahu.BooksDatabase` to `Oahu.Data`
- Update all `using` statements
- Verify migrations remain intact

### Step 4: Create Oahu.Core
- Create `src/Oahu.Core/` directory with subdirectories
- Move files from Connect.lib.core and Audible.json.core
- Create new `Oahu.Core.csproj` referencing Oahu.Foundation + Oahu.Data
- Update namespaces from `Oahu.Core` / `Oahu.Audible.Json` to organized sub-namespaces
- Update all `using` statements

### Step 5: Create Oahu.UI
- Rename `src/Connect.ui.avalonia.core/` to `src/Oahu.UI/`
- Create new `Oahu.UI.csproj` referencing Oahu.Foundation + Oahu.Data + Oahu.Core
- Update namespace from `Oahu.Core.UI.Avalonia` to `Oahu.UI`
- Update all `using` and XAML namespace references

### Step 6: Create Oahu.App
- Create `src/Oahu.App/` directory
- Move files from Connect.app.avalonia.core + all 3 SystemMgmt projects
- Create new `Oahu.App.csproj` with OutputType WinExe
- Update namespaces to `Oahu.App` / `Oahu.App.Platform`
- Update all `using`, XAML namespaces, and assembly references

### Step 7: Update solution file
- Remove all 13 old project entries from Oahu.sln
- Add 5 new project entries
- Simplify solution folders

### Step 8: Update build infrastructure
- Update `build/build-macos.sh` — project path references
- Update `build/build-linux.sh` — project path references
- Update `build/build-windows.ps1` — project path references
- Update `src/InnoSetup/Oahu setup.iss` — source directory path
- Verify `src/Directory.Build.props` still works

### Step 9: Remove old directories
- Delete all 12 old project directories
- Clean up any remaining `bin/` and `obj/` artifacts

### Step 10: Verify
- `dotnet build src/Oahu.sln` — must compile cleanly
- `dotnet ef migrations list --project src/Oahu.Data` — migrations intact
- `dotnet run --project src/Oahu.App` — app launches
- Grep for old namespace references — none should remain
- Run build scripts — verify packaging

---

## Namespace Mapping

| Old Namespace | New Namespace |
|---|---|
| `Oahu.Aux` | `Oahu.Foundation` (various sub-namespaces) |
| `Oahu.Aux.Win32` | `Oahu.Foundation.IO` |
| `Oahu.Aux.Diagnostics` | `Oahu.Foundation.Diagnostics` |
| `Oahu.Common.Util` | `Oahu.Foundation.Utilities` / `Oahu.Foundation.IO` / etc. |
| `Oahu.CommonTypes` | `Oahu.Foundation.Platform` / `Oahu.Foundation.Types` |
| `Oahu.BooksDatabase` | `Oahu.Data` |
| `Oahu.Audible.Json` | `Oahu.Core.Models` |
| `Oahu.Core` | `Oahu.Core` (stays, sub-namespaces added) |
| `Oahu.Core.UI.Avalonia` | `Oahu.UI` |
| `Oahu.App.Avalonia` | `Oahu.App` |
| `Oahu.SystemManagement` | `Oahu.App.Platform.Windows` |
| `Oahu.SystemManagement.Mac` | `Oahu.App.Platform.Mac` |
| `Oahu.SystemManagement.Linux` | `Oahu.App.Platform.Linux` |

---

## Risk Mitigation

1. **EF Core Migrations**: Migrations reference the old `BooksDbContext` namespace. The migration files themselves encode the namespace in their class declarations. We must update these carefully and verify `dotnet ef migrations list` works.

2. **Avalonia XAML namespaces**: `.axaml` files reference CLR namespaces via `xmlns:local="clr-namespace:..."`. All must be updated to new namespaces.

3. **Assembly name changes**: The main app assembly was `Oahu` — we keep it as `Oahu` for the App project to avoid breaking anything that depends on the assembly name.

4. **Resource files**: Connect.lib.core has `Properties/Resources.resx` — must be moved with correct namespace for resource lookup.

5. **InternalsVisibleTo**: BooksDatabase.core has `InternalsVisible.cs` — check if still needed.
