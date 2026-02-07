# macOS Client Change Plan

## Goal

Create a macOS GUI client for BookLibConnect that maximizes reuse of existing
platform-neutral business logic while providing a native-feeling macOS experience.

---

## Architecture Overview

### Current Project Classification

| Project | TFM | Portable? | Notes |
|---------|-----|-----------|-------|
| CommonTypes.lib.core | net10.0 | ✅ Yes | Pure interfaces & enums. No changes needed. |
| AuxLib.core | net10.0 | ⚠️ Mostly | `Temp.cs` uses `System.Drawing.Image`/`Bitmap` for image format detection — Windows-only on .NET 7+. |
| Audible.json.core | net10.0 | ✅ Yes | JSON deserialization models. No changes needed. |
| BooksDatabase.core | net10.0 | ✅ Yes | EF Core + SQLite. Fully cross-platform. |
| TreeDecomposition.core | net10.0 | ✅ Yes | Diagnostics utility. No changes needed. |
| SystemMgmt.core | net10.0 | ❌ No | Uses WMI (`System.Management`) — Windows-only at runtime. |
| AuxWin32Lib.core | net10.0 | ❌ No | kernel32 P/Invokes, Windows Registry APIs. |
| CommonUtil.lib.core | net10.0 | ⚠️ Mostly | `FileEx.cs` uses Win32 file I/O from AuxWin32Lib; `OnlineUpdate.cs` uses Windows download path. |
| Connect.lib.core | net10.0 | ⚠️ Mostly | Core business logic. Depends on SystemMgmt for hardware IDs and on System.Drawing.Common transitively. |
| PropGridLib.core | net10.0-windows | ❌ No | WinForms PropertyGrid adapter. |
| AuxWin.DialogBox.core | net10.0-windows | ❌ No | Win32 hook-based centered dialogs. |
| AuxWin.lib.core | net10.0-windows | ❌ No | WinForms helpers, wizard framework, interaction callback. |
| Connect.ui.lib.core | net10.0-windows | ❌ No | All WinForms UI controls and forms. |
| Connect.app.gui.core | net10.0-windows | ❌ No | WinForms application entry point. |

### Dependency Graph (Simplified)

```
CommonTypes.lib.core  ◄── AuxLib.core  ◄── Audible.json.core
                               │
                               ├──► TreeDecomposition.core
                               ├──► BooksDatabase.core
                               │
               SystemMgmt.core ◄── AuxWin32Lib.core ◄── CommonUtil.lib.core
                               │
                     Connect.lib.core  (BUSINESS LOGIC)
                               │
              ┌────────────────┼────────────────┐
              │ (Windows)      │                │ (macOS - NEW)
     Connect.ui.lib.core       │       Connect.ui.mac.core
              │                │                │
   Connect.app.gui.core        │       Connect.app.mac.core
                               │
                    (shared portable code)
```

---

## Toolkit Choice: Avalonia UI

**Avalonia UI** is recommended because it:
- Is the most mature cross-platform .NET UI framework
- Runs natively on macOS without Mac Catalyst
- Uses XAML (familiar to .NET developers)
- Supports MVVM pattern for clean separation
- Can share projects by direct reference (no special packaging)
- Has DataGrid, TreeView, and all controls needed
- Produces native-feeling macOS apps with proper menu bar integration

---

## Execution Plan

### Phase 1: Extract Platform Abstractions from Shared Code

The goal is to make the business logic truly cross-platform by abstracting
away the remaining Windows dependencies hiding in `net10.0`-targeted projects.

#### Step 1.1 — Abstract hardware ID generation

**Problem:** `SystemMgmt.core` uses WMI (`Win32_processor`, `Win32_BaseBoard`,
`win32_logicaldisk`) to generate hardware IDs for encryption tokens. WMI does
not exist on macOS.

**Action:**
- Create an `IHardwareIdProvider` interface in `CommonTypes.lib.core`:
  ```csharp
  public interface IHardwareIdProvider {
      string GetProcessorId();
      string GetMotherboardId();
      string GetDiskId();
  }
  ```
- Refactor `SystemMgmt.core/Processor.cs` and `Disk.cs` to implement this
  interface using WMI (Windows implementation).
- `Connect.lib.core` should accept `IHardwareIdProvider` via dependency
  injection instead of calling `SystemMgmt` directly.

#### Step 1.2 — Abstract image format detection

**Problem:** `AuxLib.core/Temp.cs` uses `System.Drawing.Image.FromStream()` and
`Bitmap` to detect image file formats from byte arrays. `System.Drawing.Common`
is Windows-only on .NET 7+.

**Action:**
- Replace the `System.Drawing`-based image detection in `AuxLib.core/Temp.cs`
  with a magic-byte header approach that reads JPEG (`FF D8 FF`), PNG
  (`89 50 4E 47`), GIF (`47 49 46`), BMP (`42 4D`), and TIFF (`49 49` /
  `4D 4D`) signatures from the byte array.
- Remove the `System.Drawing.Common` PackageReference from `AuxLib.core.csproj`.
- Update `Connect.lib.core.csproj` to also remove its
  `System.Drawing.Common` PackageReference if no longer needed directly.

#### Step 1.3 — Abstract high-performance file I/O

**Problem:** `CommonUtil.lib.core/FileEx.cs` uses `Win32FileIO` from
`AuxWin32Lib.core` (kernel32 `CreateFile`/`ReadFile`/`WriteFile` P/Invokes)
for high-performance file copy. This crashes on macOS.

**Action:**
- Create an `IFileOperations` interface in `CommonTypes.lib.core`:
  ```csharp
  public interface IFileOperations {
      void CopyFile(string source, string destination, Action<long> progress = null);
  }
  ```
- Refactor `FileEx.cs` to accept an `IFileOperations` implementation.
- The Windows implementation (already in `AuxWin32Lib.core`) keeps using
  kernel32 P/Invokes.
- A cross-platform implementation will use standard `FileStream` with
  buffered copy.

#### Step 1.4 — Fix cross-platform download path

**Problem:** `CommonUtil.lib.core/OnlineUpdate.cs` uses
`%USERPROFILE%\Downloads` which does not resolve on macOS.

**Action:**
- Replace with:
  ```csharp
  Path.Combine(Environment.GetFolderPath(
      Environment.SpecialFolder.UserProfile), "Downloads")
  ```
  This resolves correctly on both Windows and macOS.

#### Step 1.5 — Remove Windows project dependencies from Connect.lib.core

**Problem:** `Connect.lib.core` currently has project references to
`SystemMgmt.core` and transitive dependencies on `AuxWin32Lib.core` and
`CommonUtil.lib.core` (which pulls in Win32 code).

**Action:**
- After Steps 1.1–1.4, replace direct project references with interface-based
  injection so `Connect.lib.core` depends only on:
  - `CommonTypes.lib.core` (interfaces)
  - `AuxLib.core` (utilities)
  - `Audible.json.core` (JSON models)
  - `BooksDatabase.core` (database)
  - `TreeDecomposition.core` (diagnostics)
  - `CommonUtil.lib.core` (after refactoring away Win32 dependency)
- The `SystemMgmt.core` and `AuxWin32Lib.core` references should move to the
  platform-specific app projects that provide the concrete implementations.

---

### Phase 2: Create macOS Platform Support Project

#### Step 2.1 — Create `SystemMgmt.mac.core` project

**Purpose:** macOS implementation of `IHardwareIdProvider`.

**Action:**
- Create project: `src/SystemMgmt.mac.core/SystemMgmt.mac.core.csproj`
  targeting `net10.0-macos`.
- Implement hardware ID retrieval using:
  - `sysctl` calls via P/Invoke (`libSystem.B.dylib`) for processor info
  - `IOKit` framework for motherboard serial / disk identifiers
  - Or shell out to `system_profiler SPHardwareDataType -json` as a simpler
    approach.
- Implement `IHardwareIdProvider`.

#### Step 2.2 — Create cross-platform `FileOperations` implementation

**Action:**
- Add a default cross-platform `FileOperations` class (either in
  `CommonUtil.lib.core` behind a runtime check, or in a new small project)
  that uses `FileStream` with a large buffer (e.g. 1 MB) for file copy.
- On Windows, the DI container will still inject the Win32 version.

---

### Phase 3: Create macOS GUI Application

#### Step 3.1 — Create `Connect.ui.mac.core` project (Avalonia UI library)

**Purpose:** macOS UI controls and views equivalent to `Connect.ui.lib.core`.

**Action:**
- Create project: `src/Connect.ui.mac.core/Connect.ui.mac.core.csproj`
  targeting `net10.0` with Avalonia NuGet packages.
- Create MVVM ViewModels (shared, platform-agnostic):
  - `MainWindowViewModel` — overall app state, navigation
  - `BookLibraryViewModel` — book list, filtering, selection
  - `ConversionViewModel` — download/convert list, progress
  - `ProfileWizardViewModel` — multi-step profile setup
  - `SettingsViewModel` — settings management
  - `AboutViewModel` — app info
- Create Avalonia Views (AXAML):
  - `BookLibraryView` — DataGrid displaying books with cover images,
    selection checkboxes, sorting, filtering
  - `ConversionView` — DataGrid for download/conversion status with
    progress bars
  - `ProfileWizardView` — step-by-step wizard (marketplace selection, login,
    profile naming, completion)
  - `SettingsView` — property grid equivalent using Avalonia controls
  - `AboutView` — app version and credits
  - `WaitOverlay` — loading indicator overlay
- Project references:
  - `CommonTypes.lib.core`
  - `AuxLib.core`
  - `BooksDatabase.core`
  - `Connect.lib.core`

#### Step 3.2 — Create `Connect.app.mac.core` project (Avalonia application)

**Purpose:** macOS application entry point, equivalent to
`Connect.app.gui.core`.

**Action:**
- Create project: `src/Connect.app.mac.core/Connect.app.mac.core.csproj`
  targeting `net10.0` with Avalonia NuGet packages.
- Set `OutputType` to `Exe`.
- Create:
  - `Program.cs` — Avalonia application entry point
  - `App.axaml` / `App.axaml.cs` — application definition with DI container
    setup
  - `MainWindow.axaml` / `MainWindow.axaml.cs` — main window with:
    - macOS-native menu bar (File, Edit, Help)
    - Split-pane layout: book library on top, conversion list on bottom
    - Status bar
  - `InteractionCallbackMac.cs` — macOS implementation of
    `IInteractionCallback` using Avalonia dialogs
- DI container registrations:
  - `IHardwareIdProvider` → `SystemMgmt.mac.core` implementation
  - `IFileOperations` → cross-platform `FileStream` implementation
  - `IInteractionCallback` → `InteractionCallbackMac`
  - `IAudibleApi`, `IBookLibrary` → existing Connect.lib.core classes
- Project references:
  - `Connect.ui.mac.core`
  - `SystemMgmt.mac.core`
  - `CommonUtil.lib.core`
  - `TreeDecomposition.core`

#### Step 3.3 — macOS application bundling

**Action:**
- Add macOS `.app` bundle metadata:
  - `Info.plist` with `CFBundleIdentifier`, `CFBundleName`,
    `CFBundleVersion`, `LSMinimumSystemVersion`
  - App icon as `.icns` file (convert from existing `audio.ico`)
- Configure `dotnet publish` for macOS:
  ```xml
  <RuntimeIdentifier>osx-arm64</RuntimeIdentifier>
  ```
  (and/or `osx-x64` for Intel Macs)

---

### Phase 4: Update Solution and Build Infrastructure

#### Step 4.1 — Update solution file

**Action:**
- Add 3 new projects to `AaxAudioConverter 2.x.sln`:
  - `SystemMgmt.mac.core`
  - `Connect.ui.mac.core`
  - `Connect.app.mac.core`
- Create a `macOS` solution folder for the new projects.

#### Step 4.2 — Conditional build configuration

**Action:**
- Windows projects should only build on Windows (or with
  `EnableWindowsTargeting`).
- macOS projects should only build on macOS (or when explicitly requested).
- Update `Directory.Build.props` to conditionally set properties based on OS:
  ```xml
  <PropertyGroup Condition="$([MSBuild]::IsOSPlatform('OSX'))">
    <BuildMacProjects>true</BuildMacProjects>
  </PropertyGroup>
  ```
- Consider separate solution filter files:
  - `BookLibConnect.Windows.slnf` — includes only Windows projects
  - `BookLibConnect.macOS.slnf` — includes only macOS + shared projects

#### Step 4.3 — Update README

**Action:**
- Document the macOS build instructions.
- Document the architecture and how the projects are organized.

---

## Detailed File Listing (New Files to Create)

```
src/
├── SystemMgmt.mac.core/
│   ├── SystemMgmt.mac.core.csproj
│   └── HardwareIdProvider.cs          # IHardwareIdProvider via sysctl/IOKit
│
├── Connect.ui.mac.core/
│   ├── Connect.ui.mac.core.csproj
│   ├── ViewModels/
│   │   ├── MainWindowViewModel.cs
│   │   ├── BookLibraryViewModel.cs
│   │   ├── ConversionViewModel.cs
│   │   ├── ProfileWizardViewModel.cs
│   │   ├── SettingsViewModel.cs
│   │   └── AboutViewModel.cs
│   ├── Views/
│   │   ├── BookLibraryView.axaml
│   │   ├── BookLibraryView.axaml.cs
│   │   ├── ConversionView.axaml
│   │   ├── ConversionView.axaml.cs
│   │   ├── ProfileWizardView.axaml
│   │   ├── ProfileWizardView.axaml.cs
│   │   ├── SettingsView.axaml
│   │   ├── SettingsView.axaml.cs
│   │   ├── AboutView.axaml
│   │   ├── AboutView.axaml.cs
│   │   ├── WaitOverlay.axaml
│   │   └── WaitOverlay.axaml.cs
│   └── Converters/
│       └── (value converters as needed)
│
├── Connect.app.mac.core/
│   ├── Connect.app.mac.core.csproj
│   ├── Program.cs
│   ├── App.axaml
│   ├── App.axaml.cs
│   ├── MainWindow.axaml
│   ├── MainWindow.axaml.cs
│   ├── InteractionCallbackMac.cs
│   ├── Info.plist
│   └── Resources/
│       └── audio.icns
```

---

## Execution Order Summary

| # | Step | Risk | Effort |
|---|------|------|--------|
| 1.1 | Abstract hardware ID generation | Low | Small |
| 1.2 | Replace System.Drawing image detection | Low | Small |
| 1.3 | Abstract file I/O operations | Low | Small |
| 1.4 | Fix cross-platform download path | Low | Trivial |
| 1.5 | Remove Windows deps from Connect.lib.core | Medium | Medium |
| 2.1 | Create SystemMgmt.mac.core | Low | Small |
| 2.2 | Create cross-platform FileOperations | Low | Small |
| 3.1 | Create Connect.ui.mac.core (ViewModels + Views) | Medium | Large |
| 3.2 | Create Connect.app.mac.core (app shell) | Medium | Medium |
| 3.3 | macOS app bundling | Low | Small |
| 4.1 | Update solution file | Low | Trivial |
| 4.2 | Conditional build configuration | Low | Small |
| 4.3 | Update README | Low | Trivial |

---

## Notes

- **Phase 1 changes are safe** — they refactor shared code without changing
  behavior for the existing Windows client. The Windows client should continue
  to compile and function identically after Phase 1.
- **Phase 3 is the largest effort** — the UI layer is where most new code
  lives. The MVVM pattern ensures the ViewModels are testable without a UI.
- The `IInteractionCallback` interface is the **critical abstraction** — the
  business logic in `Connect.lib.core` already communicates with the UI
  through this interface. The macOS app just needs to provide its own
  implementation.
- If Avalonia is not desired, the same Phase 1 & 2 work enables using
  **.NET MAUI** (via Mac Catalyst) or **native AppKit** bindings instead.
