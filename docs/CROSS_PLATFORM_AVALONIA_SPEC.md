# Cross-Platform Avalonia Client — Specification

## 1. Objective

Consolidate the application into a **single cross-platform Avalonia client** that compiles and
runs on **Windows, macOS, and Linux** with a consistent UI across all three platforms. This
replaces the current approach of having separate WinForms (Windows) and Avalonia (macOS) GUI
applications.

---

## 2. Background & Current Architecture

### 2.1 Current project layout

The repository has 17 projects organized into three tiers:

| Tier | Projects | Target | Notes |
|------|----------|--------|-------|
| **Shared/portable** | `CommonTypes.lib.core`, `AuxLib.core`, `TreeDecomposition.core`, `BooksDatabase.core`, `Audible.json.core`, `CommonUtil.lib.core`, `Connect.lib.core` | `net10.0` | Core business logic, database, Audible API |
| **Windows-only** | `AuxWin32Lib.core`, `AuxWin.DialogBox.core`, `AuxWin.lib.core`, `PropGridLib.core`, `Connect.ui.lib.core`, `SystemMgmt.core`, `Connect.app.gui.core` | `net10.0` or `net10.0-windows` | WinForms UI, Win32 P/Invoke, WMI |
| **macOS-only** | `SystemMgmt.mac.core`, `Connect.ui.mac.core`, `Connect.app.mac.core` | `net10.0` | Avalonia UI, macOS system commands |

### 2.2 Key architectural observations

1. **Business logic is fully portable.** `Connect.lib.core` (Audible auth, download, decrypt,
   library management) targets plain `net10.0` with no platform-specific dependencies.

2. **Platform differences are minimal and well-isolated:**
   - `IHardwareIdProvider` — 2 implementations (WMI on Windows, sysctl/ioreg on macOS).
   - `IInteractionCallback` — 2 implementations (WinForms dialogs vs Avalonia logging).
   - `FileEx.Copy()` in `CommonUtil.lib.core` — runtime branch between Win32 kernel32 I/O and
     portable `FileStream` I/O (already cross-platform via `RuntimeInformation` check).

3. **No `#if` platform conditionals** exist in the codebase. All platform branching is done via
   runtime checks or project-level inclusion/exclusion.

4. **The macOS Avalonia UI is already fully functional** with MVVM architecture
   (`CommunityToolkit.Mvvm`), a complete set of views (Library, Downloads, Settings, About,
   Setup Wizard), and no macOS-specific Avalonia APIs.

5. **The Avalonia UI uses zero macOS-specific APIs.** Every `.axaml` view uses standard Avalonia
   controls (`DataGrid`, `TabControl`, `Expander`, `Window`, etc.). The namespace
   `Connect.ui.mac.core` is macOS by naming convention only — the code is inherently
   cross-platform.

---

## 3. Recommended Approach

### 3.1 Decision: Single multi-platform Avalonia app (NOT a per-platform copy)

**Chosen approach:** Evolve the existing macOS Avalonia projects into a single cross-platform
Avalonia application that targets Windows, macOS, and Linux from one codebase.

**Rejected alternative:** Creating a separate `Connect.app.win.avalonia.core` project that
duplicates the macOS UI. This would lead to code duplication, divergent UIs, and triple
maintenance burden — exactly what we want to avoid.

### 3.2 Rationale

| Factor | Single app | Per-platform copy |
|--------|-----------|-------------------|
| UI consistency | ✅ Guaranteed — one set of views | ❌ Drift over time |
| Maintenance | ✅ One codebase | ❌ 3 copies of UI code |
| Platform deps | ✅ Isolated via DI + runtime checks | ⚠️ Same, but duplicated wiring |
| Build complexity | ⚠️ Moderate (RID-based publishing) | ❌ 3 separate projects/filters |
| Avalonia design | ✅ Avalonia is designed for this | ❌ Fighting the framework |
| Linux support | ✅ Free — just add an RID | ❌ Need a 4th project |

Avalonia is inherently a cross-platform framework. The macOS UI views contain **zero
platform-specific code**. The only platform-specific code lives in the *application host* layer
(hardware ID, file operations, system commands), which is already abstracted behind interfaces.

---

## 4. Detailed Design

### 4.1 Project renames and restructuring

#### 4.1.1 Rename `Connect.ui.mac.core` → `Connect.ui.avalonia.core`

This project contains the MVVM ViewModels and Avalonia Views. It is already fully
cross-platform. The rename reflects its new role as the shared Avalonia UI library for all
platforms.

| Property | Old | New |
|----------|-----|-----|
| **Project file** | `Connect.ui.mac.core.csproj` | `Connect.ui.avalonia.core.csproj` |
| **Directory** | `src/Connect.ui.mac.core/` | `src/Connect.ui.avalonia.core/` |
| **AssemblyName** | `BookLibConnect.UI.Mac.Core` | `BookLibConnect.UI.Avalonia.Core` |
| **RootNamespace** | `BookLibConnect.Core.UI.Mac` | `BookLibConnect.Core.UI.Avalonia` |

**Internal changes:**
- Update all `namespace` declarations from `BookLibConnect.Core.UI.Mac` →
  `BookLibConnect.Core.UI.Avalonia` across all `.cs` files.
- Update all `xmlns:vm="using:BookLibConnect.Core.UI.Mac.ViewModels"` and similar XAML namespace
  references in all `.axaml` files.
- Update all `using BookLibConnect.Core.UI.Mac.*` statements in consuming projects.

**No functional changes.** The views and view models are identical.

#### 4.1.2 Rename `Connect.app.mac.core` → `Connect.app.avalonia.core`

This project is the Avalonia application host. It needs to become platform-aware to inject the
correct `IHardwareIdProvider` and handle platform-specific bootstrapping.

| Property | Old | New |
|----------|-----|-----|
| **Project file** | `Connect.app.mac.core.csproj` | `Connect.app.avalonia.core.csproj` |
| **Directory** | `src/Connect.app.mac.core/` | `src/Connect.app.avalonia.core/` |
| **AssemblyName** | `BookLibConnect.Mac` | `BookLibConnect` |
| **RootNamespace** | `BookLibConnect.App.Mac` | `BookLibConnect.App.Avalonia` |

#### 4.1.3 New project: `SystemMgmt.linux.core`

A new project for the Linux `IHardwareIdProvider` implementation.

| Property | Value |
|----------|-------|
| **Target** | `net10.0` |
| **AssemblyName** | `BookLibConnect.SystemManagement.Linux.Core` |
| **RootNamespace** | `BookLibConnect.SystemManagement.Linux` |
| **References** | `CommonTypes.lib.core` |
| **Files** | `LinuxHardwareIdProvider.cs` |

The Linux provider will use `/sys/class/dmi/id/` files, `lscpu`, and `/etc/machine-id` to
obtain hardware identifiers, following the same pattern as `MacHardwareIdProvider`.

### 4.2 Platform-specific dependency injection in `App.axaml.cs`

The application host (`Connect.app.avalonia.core`) will select the correct
`IHardwareIdProvider` at startup using `OperatingSystem.IsWindows()` /
`OperatingSystem.IsMacOS()` / `OperatingSystem.IsLinux()`. This avoids `#if` conditionals
entirely.

```csharp
// In App.axaml.cs → OnFrameworkInitializationCompleted()
IHardwareIdProvider hardwareIdProvider = getHardwareIdProvider();

// ...

private static IHardwareIdProvider getHardwareIdProvider()
{
    if (OperatingSystem.IsWindows())
        return new WinHardwareIdProvider();
    if (OperatingSystem.IsMacOS())
        return new MacHardwareIdProvider();
    if (OperatingSystem.IsLinux())
        return new LinuxHardwareIdProvider();
    throw new PlatformNotSupportedException();
}
```

All three `SystemMgmt.*.core` projects will be referenced by `Connect.app.avalonia.core`. At
runtime, only the relevant platform provider is instantiated. The .NET IL trimmer will remove
the unreachable platform assemblies during publish, ensuring each platform's output only
contains the assemblies it actually needs (see section 4.3 for the project configuration).

### 4.3 Project file: `Connect.app.avalonia.core.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>BookLibConnect</AssemblyName>
    <RootNamespace>BookLibConnect.App.Avalonia</RootNamespace>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationIcon Condition="$([MSBuild]::IsOSPlatform('Windows'))">Resources\audio.ico</ApplicationIcon>
    <NoWarn>$(NoWarn);CA1416</NoWarn>
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>partial</TrimMode>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.2.7" />
    <PackageReference Include="Avalonia.Desktop" Version="11.2.7" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.2.7" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.7" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AuxLib.core\AuxLib.core.csproj" />
    <ProjectReference Include="..\BooksDatabase.core\BooksDatabase.core.csproj" />
    <ProjectReference Include="..\CommonTypes.lib.core\CommonTypes.lib.core.csproj" />
    <ProjectReference Include="..\CommonUtil.lib.core\CommonUtil.lib.core.csproj" />
    <ProjectReference Include="..\Connect.lib.core\Connect.lib.core.csproj" />
    <ProjectReference Include="..\Connect.ui.avalonia.core\Connect.ui.avalonia.core.csproj" />
    <ProjectReference Include="..\SystemMgmt.core\SystemMgmt.core.csproj" />
    <ProjectReference Include="..\SystemMgmt.mac.core\SystemMgmt.mac.core.csproj" />
    <ProjectReference Include="..\SystemMgmt.linux.core\SystemMgmt.linux.core.csproj" />
    <ProjectReference Include="..\TreeDecomposition.core\TreeDecomposition.core.csproj" />
  </ItemGroup>

</Project>
```

**Key points:**
- Single `net10.0` target — no multi-targeting needed.
- References all three `SystemMgmt` projects. Runtime DI selects the correct one.
- `CA1416` is suppressed because `SystemMgmt.core` calls Windows-only WMI APIs, but these
  code paths are only reached on Windows at runtime.
- `BuiltInComInteropSupport` is needed for Avalonia on macOS.
- `PublishTrimmed=true` with `TrimMode=partial` enables IL trimming during publish.
  The trimmer performs whole-program analysis and removes assemblies (and types within
  opt-in assemblies) that are not reachable from the application entry point. When
  publishing for `linux-x64`, the trimmer sees that `WinHardwareIdProvider` and
  `MacHardwareIdProvider` are never instantiated on that code path (guarded by
  `OperatingSystem.IsWindows()` / `OperatingSystem.IsMacOS()` which the trimmer evaluates
  as platform-specific guards), and removes `SystemMgmt.core` and `SystemMgmt.mac.core`
  from the output. The same applies symmetrically for Windows and macOS publishes.
  `TrimMode=partial` is used instead of `full` to avoid breaking EF Core and Avalonia
  reflection patterns — only assemblies that explicitly opt in via `[AssemblyMetadata
  ("IsTrimmable", "True")]` are trimmed at the member level, while the rest are trimmed
  only at the assembly level (removed entirely if unreachable, kept whole otherwise).

### 4.4 `CommonUtil.lib.core` — Remove `AuxWin32Lib.core` dependency

Currently `CommonUtil.lib.core` references `AuxWin32Lib.core` to access `WinFileIO` for
high-performance Win32 file copying. However, `FileEx.cs` already has a runtime branch:

```csharp
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    return copyWin32(...);   // Uses WinFileIO from AuxWin32Lib
else
    return copyPortable(...); // Standard FileStream
```

**Two options (choose one):**

**Option A (Recommended): Move the project reference into the Avalonia app project instead.**
Remove the `AuxWin32Lib.core` reference from `CommonUtil.lib.core.csproj`. Instead, have
`Connect.app.avalonia.core` reference `AuxWin32Lib.core` directly, and use a service/DI pattern
for file copy (similar to `IHardwareIdProvider`). This keeps `CommonUtil.lib.core` fully
portable.

**Option B: Keep as-is with runtime guard.**
Since `AuxWin32Lib.core` targets plain `net10.0` (not `net10.0-windows`) and contains only
P/Invoke declarations, it will compile on all platforms. The Win32 P/Invoke calls will simply
never be reached on macOS/Linux because the `RuntimeInformation` check guards them. The
assembly reference is inert on non-Windows platforms.

**Recommendation:** Option B is simpler and maintains backward compatibility. The
`AuxWin32Lib.core` assembly will be included in the publish output on all platforms but is
harmless — it contains no static initializers or side effects. The P/Invoke declarations don't
cause errors unless actually invoked. This is the existing pattern and it works.

### 4.5 Solution file and solution filters

#### 4.5.1 Updated master solution (`AaxAudioConverter 2.x.sln`)

Add the new projects and update the macOS section to become a cross-platform section:

| Solution Folder | Projects |
|-----------------|----------|
| **global** | `AuxLib.core`, `AuxWin.DialogBox.core`, `AuxWin.lib.core`, `AuxWin32Lib.core`, `SystemMgmt.core`, `SystemMgmt.mac.core`, `SystemMgmt.linux.core`, `TreeDecomposition.core` |
| **BookLibConnect** | `Audible.json.core`, `BooksDatabase.core`, `CommonTypes.lib.core`, `CommonUtil.lib.core`, `Connect.lib.core`, `Connect.ui.lib.core`, `Connect.ui.avalonia.core`, `PropGridLib.core` |
| **app** (under BookLibConnect) | `Connect.app.gui.core`, `Connect.app.avalonia.core` |

#### 4.5.2 New solution filter: `BookLibConnect.Avalonia.slnf`

Replaces `BookLibConnect.macOS.slnf`. Includes all projects needed for the cross-platform
Avalonia app:

```json
{
  "solution": {
    "path": "AaxAudioConverter 2.x.sln",
    "projects": [
      "Audible.json.core\\Audible.json.core.csproj",
      "AuxLib.core\\AuxLib.core.csproj",
      "AuxWin32Lib.core\\AuxWin32Lib.core.csproj",
      "BooksDatabase.core\\BooksDatabase.core.csproj",
      "CommonTypes.lib.core\\CommonTypes.lib.core.csproj",
      "CommonUtil.lib.core\\CommonUtil.lib.core.csproj",
      "Connect.app.avalonia.core\\Connect.app.avalonia.core.csproj",
      "Connect.lib.core\\Connect.lib.core.csproj",
      "Connect.ui.avalonia.core\\Connect.ui.avalonia.core.csproj",
      "SystemMgmt.core\\SystemMgmt.core.csproj",
      "SystemMgmt.mac.core\\SystemMgmt.mac.core.csproj",
      "SystemMgmt.linux.core\\SystemMgmt.linux.core.csproj",
      "TreeDecomposition.core\\TreeDecomposition.core.csproj"
    ]
  }
}
```

Note: `AuxWin32Lib.core` is included because `CommonUtil.lib.core` references it (and it
compiles fine on all platforms since it's just P/Invoke declarations with `net10.0` TFM).

#### 4.5.3 `BookLibConnect.Windows.slnf` — unchanged

The WinForms app continues to work as-is. No changes needed. It remains available for users who
prefer the legacy WinForms experience on Windows.

#### 4.5.4 `BookLibConnect.macOS.slnf` — deprecate

This file is superseded by `BookLibConnect.Avalonia.slnf`. It can be removed or kept
temporarily with a note that it is deprecated.

### 4.6 Build scripts

#### 4.6.1 `build/build-windows.ps1` — update

Update the existing Windows build script to also support building the Avalonia app. The script
already handles publishing `Connect.app.gui.core` (WinForms). Add a mode or parameter to
publish the Avalonia app instead:

```powershell
# Publish the Avalonia app for Windows (trimmed):
dotnet publish src/Connect.app.avalonia.core/Connect.app.avalonia.core.csproj `
    -c Release -r win-x64 --self-contained `
    -p:PublishTrimmed=true -o artifacts/publish/avalonia-win-x64
```

The existing signing and installer logic can be reused for the Avalonia output.

#### 4.6.2 `build/build-macos.sh` — update

Update to reference the new project path:

```bash
# Before:
PROJECT="src/Connect.app.mac.core/Connect.app.mac.core.csproj"

# After:
PROJECT="src/Connect.app.avalonia.core/Connect.app.avalonia.core.csproj"
```

The `.app` bundling, code signing, notarization, and DMG creation logic remains identical.
The `dotnet publish` invocation in the script should also include `-p:PublishTrimmed=true`
to ensure the macOS output excludes Windows and Linux platform assemblies.

#### 4.6.3 `build/build-linux.sh` (new)

A new shell script for Linux builds, producing a tarball or AppImage:

```bash
#!/bin/bash
set -euo pipefail

dotnet publish src/Connect.app.avalonia.core/Connect.app.avalonia.core.csproj \
    -c Release -r linux-x64 --self-contained \
    -p:PublishTrimmed=true -o artifacts/publish/avalonia-linux-x64
```

### 4.7 Namespace migration plan

All namespace changes follow a single find-and-replace pattern:

| Scope | Old | New |
|-------|-----|-----|
| C# namespaces | `BookLibConnect.Core.UI.Mac` | `BookLibConnect.Core.UI.Avalonia` |
| C# namespaces | `BookLibConnect.App.Mac` | `BookLibConnect.App.Avalonia` |
| AXAML xmlns | `using:BookLibConnect.Core.UI.Mac.ViewModels` | `using:BookLibConnect.Core.UI.Avalonia.ViewModels` |
| AXAML xmlns | `using:BookLibConnect.Core.UI.Mac.Views` | `using:BookLibConnect.Core.UI.Avalonia.Views` |
| AXAML xmlns | `using:BookLibConnect.Core.UI.Mac.Converters` | `using:BookLibConnect.Core.UI.Avalonia.Converters` |
| AXAML x:Class | `BookLibConnect.App.Mac.*` | `BookLibConnect.App.Avalonia.*` |
| C# using | `using BookLibConnect.SystemManagement.Mac` | (keep — plus add conditional Windows/Linux usings) |

---

## 5. Implementation Plan

### Phase 1: Rename and restructure (no functional changes)

1. **Rename directory** `src/Connect.ui.mac.core/` → `src/Connect.ui.avalonia.core/`
2. **Rename project file** within it to `Connect.ui.avalonia.core.csproj`
3. **Update** `AssemblyName` and `RootNamespace` in the `.csproj`
4. **Find-and-replace** all `BookLibConnect.Core.UI.Mac` → `BookLibConnect.Core.UI.Avalonia`
   in all `.cs` and `.axaml` files within the project
5. **Rename directory** `src/Connect.app.mac.core/` → `src/Connect.app.avalonia.core/`
6. **Rename project file** within it to `Connect.app.avalonia.core.csproj`
7. **Update** `AssemblyName` to `BookLibConnect` and `RootNamespace` to
   `BookLibConnect.App.Avalonia` in the `.csproj`
8. **Find-and-replace** all `BookLibConnect.App.Mac` → `BookLibConnect.App.Avalonia` in all
   `.cs` and `.axaml` files within the project
9. **Update** the project reference in `Connect.app.avalonia.core.csproj` from
   `Connect.ui.mac.core` → `Connect.ui.avalonia.core`
10. **Update** the master solution file to reflect the new paths and names
11. **Verify** the app builds and runs on macOS (no functional changes, just renames)

### Phase 2: Add cross-platform SystemMgmt support

1. **Create** `src/SystemMgmt.linux.core/` with `LinuxHardwareIdProvider.cs`
2. **Add** `SystemMgmt.core` and `SystemMgmt.linux.core` as project references to
   `Connect.app.avalonia.core.csproj` (it already has `SystemMgmt.mac.core`)
3. **Modify** `App.axaml.cs` to use runtime platform detection for `IHardwareIdProvider`
   selection instead of hardcoding `MacHardwareIdProvider`
4. **Suppress** `CA1416` in `Connect.app.avalonia.core.csproj` since it now references
   `SystemMgmt.core` which calls Windows-only WMI APIs
5. **Add** project to master solution
6. **Verify** the app builds and runs on macOS (still no functional regression)
7. **Verify** the app builds and runs on Windows
8. **Verify** the app builds and runs on Linux

### Phase 3: Solution filters and build scripts

1. **Create** `BookLibConnect.Avalonia.slnf` with all required projects
2. **Deprecate** `BookLibConnect.macOS.slnf` (add deprecation note or remove)
3. **Update** `build/build-macos.sh` to reference the new project path
4. **Update** `build/build-windows.ps1` to support building the Avalonia app for Windows
5. **Create** `build/build-linux.sh` for Linux builds
6. **Update** `README.md` with new build instructions and platform support matrix

### Phase 4: Refinements (optional, future)

1. **Platform-specific polish:** Add Windows-native features like taskbar progress, system
   tray integration, or jump lists via Avalonia's platform extensions if desired.
2. **Linux desktop integration:** Create `.desktop` file, install script, Flatpak/Snap
   packaging.
3. **Windows installer:** Create an Inno Setup or WiX-based installer for the Avalonia
   Windows build, similar to the existing WinForms installer.
4. **Deprecate WinForms app:** Once the Avalonia Windows app is stable, consider deprecating
   `Connect.app.gui.core` and the Windows-only UI projects.
5. **Native AOT:** Optionally enable Native AOT compilation for faster startup and smaller
   deployments. This may require AOT-compatibility annotations on the reflection-heavy
   EF Core and Avalonia code. (IL trimming is already enabled in Phase 2.)

---

## 6. Dependency Graph — After Implementation

```
                     CommonTypes.lib.core (net10.0)
                            │
              ┌─────────────┼───────────────────────────┐
              ▼              ▼                           ▼
        AuxLib.core    SystemMgmt.core (Win)     SystemMgmt.mac.core
       (net10.0)       (net10.0+WMI)             (net10.0)
              │                                          │
              │         SystemMgmt.linux.core ◄──────────┘ (NEW)
              │         (net10.0)
              │
   ┌──────────┼───────────────┬──────────────────┐
   ▼          ▼               ▼                  ▼
Audible.json BooksDatabase  TreeDecomp    AuxWin32Lib.core
(net10.0)    (net10.0/EF)   (net10.0)     (net10.0, Win32 P/Invoke)
                                                  │
                                          CommonUtil.lib.core
                                          (net10.0)
              │
              ▼
      Connect.lib.core (net10.0) — CORE BUSINESS LOGIC
              │
              ▼
   Connect.ui.avalonia.core (net10.0) — SHARED AVALONIA UI  (RENAMED)
   ├── Avalonia 11.2.7
   ├── Avalonia.Controls.DataGrid
   ├── CommunityToolkit.Mvvm 8.4.0
   │   ViewModels + Views (cross-platform)
   │
   ▼
   Connect.app.avalonia.core (net10.0) — CROSS-PLATFORM APP  (RENAMED + ENHANCED)
   ├── Avalonia.Desktop 11.2.7
   ├── Avalonia.Fonts.Inter 11.2.7
   ├── SystemMgmt.core (Win)
   ├── SystemMgmt.mac.core (Mac)
   ├── SystemMgmt.linux.core (Linux)     ◄── NEW
   ├── Publishes to: win-x64, osx-arm64, osx-x64, linux-x64
   └── AssemblyName=BookLibConnect


   ┌─────────────────────────────────────────────┐
   │ LEGACY (unchanged, kept for compatibility)   │
   │                                              │
   │  Connect.ui.lib.core (net10.0-windows)       │
   │  ├── WinForms UI Controls                    │
   │  ▼                                           │
   │  Connect.app.gui.core (net10.0-windows)      │
   │  └── WinForms App (Windows only)             │
   └─────────────────────────────────────────────┘
```

---

## 7. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `SystemMgmt.core` (WMI) fails to compile on Linux/macOS CI | Low | Medium | Already handled by `EnableWindowsTargeting=true` in `Directory.Build.props` — allows restore/compile of `net10.0` projects using Windows-only NuGets on non-Windows hosts. The WMI calls are runtime-only. |
| `AuxWin32Lib.core` P/Invoke declarations cause issues on Linux | Very Low | Low | P/Invoke `DllImport` attributes are metadata-only; they don't cause load errors unless the method is actually called. The runtime guard in `FileEx.cs` prevents this. |
| Avalonia rendering differences across platforms | Low | Low | Avalonia's Fluent theme provides consistent rendering. Platform-specific quirks can be addressed with minor style overrides. |
| Linux hardware ID instability across distros | Medium | Low | Use `/etc/machine-id` as primary identifier — it's standardized across systemd-based distributions (Ubuntu, Fedora, Arch, etc.). Fallback to DMI data in `/sys/class/dmi/id/`. |
| Breaking changes from directory/namespace renames | Low | Medium | Phase 1 is purely mechanical renames with no functional changes. Git tracks renames. All changes can be validated by building and running the app before proceeding to Phase 2. |

---

## 8. Files Changed Summary

### New files
| File | Description |
|------|-------------|
| `src/SystemMgmt.linux.core/SystemMgmt.linux.core.csproj` | Linux hardware ID project |
| `src/SystemMgmt.linux.core/LinuxHardwareIdProvider.cs` | Linux `IHardwareIdProvider` impl |
| `src/BookLibConnect.Avalonia.slnf` | Solution filter for cross-platform Avalonia build |
| `build/build-linux.sh` | Linux build + packaging script |

### Renamed files (git mv)
| Old | New |
|-----|-----|
| `src/Connect.ui.mac.core/*` | `src/Connect.ui.avalonia.core/*` |
| `src/Connect.app.mac.core/*` | `src/Connect.app.avalonia.core/*` |

### Modified files
| File | Changes |
|------|---------|
| `src/Connect.ui.avalonia.core/*.cs, *.axaml` | Namespace: `Mac` → `Avalonia` |
| `src/Connect.app.avalonia.core/*.cs, *.axaml` | Namespace: `Mac` → `Avalonia`; add platform DI |
| `src/Connect.app.avalonia.core/Connect.app.avalonia.core.csproj` | Add SystemMgmt refs, update AssemblyName |
| `src/AaxAudioConverter 2.x.sln` | Update project paths and add new projects |
| `build/build-windows.ps1` | Add Avalonia app build support |
| `build/build-macos.sh` | Update project path |
| `README.md` | Update platform support, build instructions |

### Unchanged
| File | Reason |
|------|--------|
| `src/Connect.app.gui.core/*` | WinForms app remains as legacy Windows client |
| `src/Connect.ui.lib.core/*` | WinForms UI library remains for legacy client |
| `src/BookLibConnect.Windows.slnf` | WinForms solution filter unchanged |
| All shared libraries | No changes needed — already cross-platform |

---

## 9. Success Criteria

1. ✅ A single `dotnet publish -r win-x64` command produces a working Windows application
   with the Avalonia UI.
2. ✅ A single `dotnet publish -r osx-arm64` command produces a working macOS application
   (same UI as current macOS app).
3. ✅ A single `dotnet publish -r linux-x64` command produces a working Linux application
   (same UI).
4. ✅ The UI is identical across all three platforms (same views, same layout, same behavior).
5. ✅ Hardware ID generation works correctly on each platform using the appropriate provider.
6. ✅ File copy uses Win32 fast I/O on Windows and portable `FileStream` on macOS/Linux
   (existing behavior preserved).
7. ✅ The legacy WinForms Windows app continues to build and work without modifications.
8. ✅ No `#if` platform conditionals are introduced — all platform branching remains runtime.
