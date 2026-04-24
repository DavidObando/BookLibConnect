# `oahu-cli` — Design & Implementation Plan

Status: **Draft / Proposed**
Owner: TBD
Target framework: `net10.0` (matches the rest of `Oahu.sln`)
Distribution: `dotnet tool` + self-contained binaries (Homebrew, WinGet, tarballs)

---

## 1. Goals & non-goals

### Goals

- A single binary, `oahu-cli`, that is **dual-mode** by design:
  - **Interactive TUI mode** when launched with no arguments (or `oahu-cli` / `oahu-cli tui`): a full-screen terminal app from which the user can sign in, browse their library, queue downloads, run the conversion pipeline, and inspect history — without ever typing another command.
  - **Scriptable command mode** when invoked with subcommands (`oahu-cli download …`, `oahu-cli library list --json`, …): unattended, pipe-friendly, exit-code-driven.
- **One core, two front-ends.** All Audible/decryption/conversion work goes through the existing `Oahu.Core` / `Oahu.Data` / `Oahu.Decrypt` / `Oahu.Foundation` libraries — the same ones used by the Avalonia GUI. The CLI never duplicates business logic.
- **CLI design discipline**:
  - Semantic colors; `NO_COLOR` and non-TTY respected.
  - Fixed-width status prefixes; no layout shift.
  - Pinned hint bar on every interactive screen.
  - Progressive `Ctrl+C`.
  - Errors are documentation; `--help` everywhere; `Try 'oahu --help'` footers on parse errors.
- **Crash-only and resumable** for any long-running operation (download, decrypt, mux).

### Non-goals (v1)

- Embedding any non-.NET runtime. Specifically, **no `node-api-dotnet`**.
- Replacing the Avalonia GUI. The CLI is additive.
- Becoming a general media-library manager (no Calibre-style metadata editing, no remote serving).
- Cloud sync, multi-user, or daemon mode in v1.

---

## 2. High-level architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│                      oahu-cli  (single binary)                       │
│                                                                      │
│   System.CommandLine  ─►  Mode dispatcher                            │
│                              │                                       │
│           ┌──────────────────┴──────────────────┐                    │
│           ▼                                     ▼                    │
│   Command-mode runner                    TUI-mode runner             │
│   (one-shot, scriptable)                 (Spectre.Console.Live)      │
│           │                                     │                    │
│           └──────────────► Oahu.Cli.App ◄───────┘                    │
│                            (command/use-case handlers,               │
│                             state machine, queue, jobs)              │
│                                     │                                │
│                                     ▼                                │
│   ┌──────────────────────────────────────────────────────────────┐   │
│   │  Oahu.Cli.Tui          (design-system layer over Spectre)    │   │
│   │  tokens · widgets · screens · themes · keymap                │   │
│   └──────────────────────────────────────────────────────────────┘   │
│                                     │                                │
│                                     ▼                                │
│   ┌──────────────────────────────────────────────────────────────┐   │
│   │  Existing core (project references, no forks):               │   │
│   │  Oahu.Core · Oahu.Data · Oahu.Decrypt · Oahu.Foundation      │   │
│   └──────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────┘
```

### New projects

| Project | Purpose |
|---------|---------|
| `Oahu.Cli` | Entry point, argument parsing, command handlers, output formatting. References everything below. |
| `Oahu.Cli.App` | Use-case layer (sign in, sync library, queue, run jobs, history). Pure C#, no UI dependency, fully testable. |
| `Oahu.Cli.Tui` | TUI design system over Spectre.Console: tokens, widgets (StatusLine, TimelineItem, HintBar, Dialog, Table, Select), screen components, theme/keymap, breakpoints. |
| `Oahu.Cli.Server` | MCP + HTTP façade over `Oahu.Cli.App`. Auth handlers, capability-class enforcement, audit log. |
| `Oahu.Cli.Tests` | Unit + snapshot tests for handlers and rendering (`Spectre.Console.Testing.TestConsole`, `Verify`). |

All four target `net10.0` to match the rest of the solution.

### Stack

| Concern | Library |
|--------|---------|
| Argument parsing | `System.CommandLine` (modern, beta-stable, supports subcommands and middleware) |
| Rendering | `Spectre.Console` (tables, trees, live, prompts, progress, layouts, markup) |
| Async streaming | `IAsyncEnumerable<T>` + `Spectre.Console.Live` |
| Logging | `Microsoft.Extensions.Logging` → file + an in-memory ring buffer for the `Logs` screen |
| Credentials | OS keychain via a `ICredentialStore` abstraction (Windows DPAPI, macOS Keychain, libsecret on Linux) |
| Persistence | The existing `BooksDbContext` (SQLite, EF Core) for library + per-book state. A new lightweight `Oahu.Cli.App` state file (`queue.json`, `history.json`) for CLI-only state. |
| Snapshot tests | `Verify.Xunit` |

---

## 3. The dual-mode contract

### 3.1 Mode selection

```
oahu-cli                       → TUI mode (alt-screen, full-screen)
oahu-cli tui                   → TUI mode (explicit; same as above)
oahu-cli <subcommand …>        → command mode (one-shot)
oahu-cli --help                → command-mode help
oahu-cli --version
```

If stdout is **not** a TTY (`Console.IsOutputRedirected`) or `TERM=dumb`, an attempt to enter TUI mode aborts with a clear error:

```
✗ TUI mode requires an interactive terminal.

  Run a subcommand instead, e.g.:
    oahu-cli library list --json
  Or run `oahu-cli --help` for the full command set.
```

### 3.2 Shared use-case layer

Both modes call the same `Oahu.Cli.App` methods:

```csharp
public interface ILibraryService     { Task<LibraryView> ListAsync(...); Task SyncAsync(...); }
public interface IAuthService        { AuthState State { get; } Task LoginAsync(...); Task LogoutAsync(...); }
public interface IJobService         { Task<JobId> EnqueueDownloadAsync(BookRef b, JobOptions o); IAsyncEnumerable<JobUpdate> ObserveAsync(JobId? id = null, CT ct = default); Task CancelAsync(JobId id); }
public interface IQueueService       { IReadOnlyList<QueueItem> Items { get; } event Action Changed; Task EnqueueAsync(BookRef b); Task RemoveAsync(JobId id); ... }
public interface IConfigService      { OahuConfig Current { get; } Task SaveAsync(...); }
public interface IDoctorService      { Task<DoctorReport> RunAsync(...); }
```

These wrap (and only wrap) the existing `IAudibleApi`, `IBookLibrary`, and the decrypt pipeline. The CLI never reaches into Audible internals directly. This is the same wrapping the Avalonia GUI does in its view-models, just headless.

The job pipeline is implemented as a **single in-process worker** with a bounded concurrency setting. Jobs go through phases:

```
queued → downloading → decrypting → muxing → exporting? → completed
                                                      \─► failed (recoverable)
                                                      \─► cancelled
```

State is persisted after every phase transition so a `Ctrl+C` or crash leaves the queue in a recoverable state.

---

## 4. Command-mode UX

### 4.1 Command surface (v1)

```
oahu-cli                              # → TUI
oahu-cli tui                          # → TUI (explicit)

oahu-cli auth login   [--region us|uk|de|fr|jp|it|au|in|ca|es|br]
oahu-cli auth status  [--json]
oahu-cli auth logout  [--profile <alias>]

oahu-cli library list   [--json|--plain] [--filter <q>] [--unread] [--limit N]
oahu-cli library sync   [--full]
oahu-cli library show   <asin>            # detail view, json by default in pipes

oahu-cli queue list     [--json]
oahu-cli queue add      <asin|title>...   # supports `-` for stdin (one per line)
oahu-cli queue remove   <jobId>...
oahu-cli queue clear

oahu-cli download       <asin|title>...   [--all-new] [--quality <q>] [--concurrency N] [--no-decrypt] [--export aax|m4b|both] [--output-dir <path>]
oahu-cli convert        <file>            [--export aax|m4b|both] [--output-dir <path>]

oahu-cli history list   [--json] [--since <when>] [--status completed|failed|all]
oahu-cli history show   <jobId>
oahu-cli history retry  <jobId>

oahu-cli config get     [<key>]
oahu-cli config set     <key> <value>
oahu-cli config path

oahu-cli doctor         [--json] [--fix]
oahu-cli completion     <bash|zsh|fish|pwsh>
```

### 4.2 Conventions

- Standard flags: `-h/--help`, `-v/--version`, `-q/--quiet`, `--verbose`, `-f/--force`, `-n/--dry-run`, `--json`, `--no-color`, `--config-dir`, `--log-dir`, `--log-level`.
- Every `--flag` has a `--no-flag` partner where it makes sense.
- `kebab-case` only.
- Validation via `.FromAmong(...)` (System.CommandLine equivalent of commander's `.choices()`); enums fail fast with the valid set.
- Auto-degrade to plain output when stdout is not a TTY.
- `stdout = data, stderr = messages.` Exit `0` on success, non-zero on failure.
- Never accept secrets via flags. Audible login is browser-delegated (existing flow); MFA / CVF prompts go to stdin in command mode (or to a Dialog in TUI mode).
- Rewritten parse errors append `Try 'oahu-cli --help' for more information.` and offer corrections (`Did you mean: oahu-cli download "Project Hail Mary"?` for stray positionals).

---

## 5. TUI-mode design

### 5.1 Buffer & lifecycle

- Enters the **alt-screen buffer** on start; restores on exit.
- A **last-resort `try/finally`** plus `Console.CancelKeyPress` plus `AppDomain.ProcessExit` ensures the terminal is restored even on unhandled crash.
- Disables the system cursor, raw-mode key reads (Spectre's input subsystem), and re-enables them on exit.
- Detects screen reader / non-TTY and refuses to enter — falling back to a plain `oahu-cli --help` hint.

### 5.2 Screen taxonomy

```
                         AppShell
   ┌─────────┬───────────┬──────────┬─────────┬─────────┬───────────┐
   │  Home   │  Library  │  Queue   │  Jobs   │ History │  Settings │
   └─────────┴───────────┴──────────┴─────────┴─────────┴───────────┘
                                           [Logs] (overlay/modal)
```

- **AppShell**: header (logo + active profile + region), tab bar / left rail (`1`–`6` jumps directly), main content area, pinned `HintBar` footer.
- **Home**: greeting, account summary, quick-actions ("Sync library", "Resume queue", "Open downloads folder"), tip-of-the-day, last-N events.
- **Library** (the most-used screen):
  - Left: searchable, filterable book list (Spectre `Table` inside a virtualized scroller). Columns: `[ ]` selection, `Title`, `Author`, `Length`, `Downloaded?`, `Queued?`.
  - Right: detail panel (cover ASCII fallback, summary, status, actions).
  - Multi-select (`Space`), filter (`/`), bulk-queue (`q`).
- **Queue**: ordered list of pending jobs, drag-reorder (`Shift+↑/↓`), per-row actions (start, pause, remove). Top: aggregate status. Bottom: "Run all" / "Pause all".
- **Jobs**: live timeline of in-flight jobs. Each row is a `TimelineItem` whose status icon transitions in place: `◐ → ✓` (or `✗`). Expanded view shows phase progress bars (download → decrypt → mux → export).
- **History**: paged log of completed / failed / cancelled jobs. Re-run, view error, open file in OS file browser.
- **Settings**: read/write what `oahu-cli config get/set` exposes — region, default quality, output directory, concurrency, export formats, themes, keymap.
- **Logs**: overlay modal showing the in-memory log ring buffer (last N lines). `L` toggles. Filter by level.
- **Help / About**: discoverable via `?`. Lists keybinds for the current screen.

### 5.3 Sign-in flow inside the TUI

This is the hard one because the existing `IAudibleApi` flow uses callbacks for captcha, MFA, CVF, and **external browser login**. The CLI must surface those as `Dialog`s without blocking the render loop.

```
1. User selects "Sign in" from Home or via `:login`.
2. A SignInDialog asks for region.
3. We call into Oahu.Core's Authorize/Login with a Callbacks record whose
   functions post requests to a CallbackBroker.
4. The TUI's main loop owns the broker; when a callback is requested
   (e.g. ExternalLoginCallback wants the user to visit a URL and paste
   the redirect URL back), the broker raises an event and the AppShell
   pushes a modal Dialog with:
     - The URL (with OSC 8 hyperlink and a QR code via Spectre).
     - A "Open in browser" action (uses System.Diagnostics.Process).
     - An input field for the redirect URL.
   The user submits → the Dialog returns the value to the broker → the
   awaiting Task in Oahu.Core continues.
5. CAPTCHA/MFA/CVF callbacks use the same broker pattern with appropriate
   inputs (image rendered as ASCII for CAPTCHA where feasible; raw image
   saved + path shown otherwise).
6. On success, AuthService stores the device registration via the
   existing Profile/ProfileKey machinery (no new persistence), and the
   Home screen updates.
```

The same `CallbackBroker` is also used in command mode — but there it bridges to stdin prompts, and in `--no-prompt` / non-TTY contexts it fails fast with a clear message naming the missing input.

### 5.4 Browse → queue → download flow

```
[Library] ──(/)──► filter ──(↑↓)──► select ──(Space)──► multi-select ──(q)──► [Queue]
                                                                                  │
                                                                                  ▼
                                                                       [Run]──►[Jobs]
                                                                                  │
                                                                                  ▼
                                                                            [History]
```

The Jobs screen renders streaming `IAsyncEnumerable<JobUpdate>` from `IJobService.ObserveAsync()` — each update mutates a `TimelineItem` row in place. No row ever shifts because the status prefix is fixed-width.

### 5.5 Keybinds (global)

| Key | Action |
|-----|--------|
| `1`–`6` | Jump to tab |
| `Tab` / `Shift+Tab` | Next / previous tab |
| `?` | Open contextual help |
| `:` | Open command palette (vim-style; types a subcommand name) |
| `/` | Focus search/filter on current screen |
| `Esc` | Close dialog → back to previous screen → exit dialog confirm |
| `q` (in lists) | Queue selected |
| `r` | Refresh / re-fetch |
| `L` | Toggle Logs overlay |
| `Ctrl+L` | Clear current screen artifacts |
| `Ctrl+C` | **Progressive**: cancel running job → close dialog → leave TUI confirm → exit |
| `Ctrl+Z` | Suspend (where supported) |

Arrow keys + vim `j/k` + emacs `Ctrl+N/P` all navigate lists. Number keys `1`–`9` jump to indexed items in pickers. Hint bar always shows the current keybinds.

---

## 6. The `Oahu.Cli.Tui` design system

A direct adaptation of TUIkit's principles to `Spectre.Console`.

### 6.1 Tokens

```csharp
public readonly record struct SemanticColor(Color Value);

public static class Tokens
{
    // Text
    public static SemanticColor TextPrimary   => Theme.Current.TextPrimary;
    public static SemanticColor TextSecondary => Theme.Current.TextSecondary;
    public static SemanticColor TextTertiary  => Theme.Current.TextTertiary;
    // Status
    public static SemanticColor StatusInfo, StatusSuccess, StatusWarning, StatusError;
    // Brand / UI
    public static SemanticColor Brand, Selected, BorderNeutral, BackgroundSecondary;
    // Diff (for log/diff views)
    public static SemanticColor DiffAdd, DiffRemove;
}

public sealed class Theme
{
    public static Theme Current { get; private set; } = Themes.Default;
    public static IReadOnlyList<Theme> Available { get; }    // Default, HighContrast, Colorblind, Mono
    public static void Use(string name);
}
```

Components consume tokens, never `Color.Red`. `NO_COLOR` and `Console.IsOutputRedirected` swap in a `Mono` theme automatically.

### 6.2 Icons

Single-width Unicode only, with ASCII fallback for hostile terminals (`OAHU_ASCII_ICONS=1` or `TERM=dumb`):

```
✓ success    ✗ error    ! warning    ⊘ disabled    ❯ prompt
● filled     ◐ working  ○ empty
↑ ↓ ← → ❯    ├ └ │      · •
```

Each icon bundles glyph + semantic color + screen-reader label.

### 6.3 Widgets (minimum useful set)

| Widget | Purpose |
|--------|---------|
| `StatusLine` | spinner + verb + hint + optional metric ("Authenticating · Esc to cancel · 1.2 KB") |
| `TimelineItem` | fixed-width 4-char status prefix + title + description + optional sub-items + expandable detail. Variants: `Success`, `Error`, `Loading`, `Warning`, `Info`, custom. |
| `HintBar` | pinned footer with auto-formatted keybinds (`↑↓ navigate · Enter select · Esc back`) |
| `Dialog` | centered, bordered modal with title, body, footer (typically a `HintBar`) |
| `Tabs` | top tab bar + content area; `1`–`6` and `Tab`/`Shift+Tab` |
| `Select` | keyboard-navigable list, multi-select, fuzzy filter |
| `Table` | thin wrapper over Spectre's `Table` that uses semantic tokens by default |
| `ProgressTimeline` | Jobs screen primitive — combines a `TimelineItem` with phase progress bars |
| `PulseSpinner` | indeterminate "thinking" animation — `● ◉ ◎ ○` cycle + color shimmer, single-width frames; embedded in `StatusLine` for opaque waits |
| `KeyHint` | utility for formatting one keybind for embedding in arbitrary text |

### 6.4 Hooks (idiomatic to Spectre/.NET)

Spectre doesn't have React hooks, but we can ship the same ergonomics via small helper classes:

```csharp
public sealed class TerminalSize       // raises Changed; exposes Width, Height
public sealed class Breakpoint         // Compact (<80) / Narrow (80–119) / Wide (≥120)
public sealed class ScreenReaderProbe  // detects screen reader (env vars, Win32 SPI on Windows)
public sealed class SshDetector        // SSH_TTY/SSH_CONNECTION/SSH_CLIENT
```

Widgets read these and adapt: animations off when screen reader detected; reduced animation cadence over SSH; layouts collapse below 80 cols.

### 6.5 Layout stability

- Status icons are **always 4 cells wide** (icon + 3 trailing spaces) so swapping `◐` for `✓` never reflows.
- Footer (HintBar) and header are pinned with `Layout` ratios 0/1/0; only the middle content scrolls/compresses.
- Lists use known column widths computed once per render frame.
- No "loading…" placeholder gets *replaced* by content in a different shape — placeholders match the eventual layout.

### 6.6 Animations

- Color-shimmer spinners only (constant character width).
- Two-tier loop: idle (event-driven, 0 FPS) when nothing animates; active (~12 FPS) only when something is in flight.
- Disabled entirely when screen reader detected or SSH detected (degraded to a single static character).
- Progress also emitted via OSC 9;4 so terminal title/dock shows progress when window is backgrounded.

#### 6.6.1 Pulse vs. bar — when to use each

Borrowed from Copilot's `TextSpinner` pattern (`src/cli/tuikit/components/TextSpinner.tsx`): a calm pulsing icon answers *"is it alive? what is it doing? how do I stop it?"* in one line. Use it **only when you have no real progress signal**; otherwise a determinate bar is more honest and more useful.

| Situation                                                                       | Indicator                                  | Why                                                                                       |
|--------------------------------------------------------------------------------|--------------------------------------------|-------------------------------------------------------------------------------------------|
| Indeterminate wait (Audible API call, MFA/CVF callback, library-sync handshake, doctor checks, `Authenticating…`) | `PulseSpinner` inside a `StatusLine`       | No quantifiable progress — pulse signals liveness without lying about ETA.                |
| Phase with byte/percent progress (download, decrypt, mux, export)              | Determinate `█░` bar in `ProgressTimeline` | Real numbers exist; a pulse on top adds noise without information.                        |
| A row in Jobs that is "in flight" but whose phases render their own bars       | Single static `◐` glyph in the 4-char prefix | Binary "in flight" indicator — animation lives in the phase bars below, not the row icon. |
| Screen reader detected, `OAHU_NO_TUI=1`, or `NO_COLOR=1`                       | Static `*` (or omitted, paired with text)  | Per-frame announcements are unusable; layout must not shift.                              |

`PulseSpinner` rules:

- Frame set is `● ◉ ◎ ○` (and back); every frame is single-width and the same width as the others — the column never reflows when the spinner becomes a `✓` or `✗`.
- Color shimmer sweeps a brighter shade across the *characters* of the verb without changing the characters themselves; the shimmer carries the animation when the glyph itself can only snap to the grid.
- Always pair the pulse with **a verb and an escape hint** — `◉ Authenticating · Esc to cancel · 1.2 KB`. The animation alone is decoration; the verb plus hint is what makes it informative.
- Cadence: ~12 FPS in alt-screen, ~6 FPS inline; the two-tier loop in §6.6 means the animation cost is zero when no `PulseSpinner` is registered.
- Disabled (replaced with a static `*`) when `ScreenReaderProbe` reports a screen reader, when stdout is not a TTY, when `NO_COLOR` / `OAHU_NO_TUI=1` is set, or when `SshDetector` indicates a remote session and the user hasn't opted into full-cadence animation.

Concretely in `oahu-cli`, this maps to:

- **Pulse:** the `Authenticating…` step in the sign-in dialog, `Awaiting 2FA…` while the broker is blocked, `Syncing library…`, `Contacting Audible…`, `Running checks…` (`doctor`), and the header activity verb when no determinate work is in flight.
- **Bar:** every phase row on the Jobs screen (download/decrypt/mux/export) and the doctor screen's per-check row once a check completes.
- **Static `◐`:** the row-level "in flight" indicator on a Jobs row whose phase bars are doing the real reporting.

---

## 7. Persistence & state

Reuse what's already there; add only the minimum.

| Concern | Storage | Owner |
|--------|---------|-------|
| Audible profile / device registration | Existing profile store via `Oahu.Core` | shared with Avalonia GUI |
| Library cache + per-book conversion state | `BooksDbContext` (SQLite via EF) | shared |
| User config (region default, quality, output dir, concurrency, export formats, theme, keymap overrides) | `~/.config/oahu/config.json` (Linux/macOS), `%APPDATA%\oahu\config.json` (Windows) | new, CLI-only |
| Job queue (pending) | **Alongside the GUI's user-data dir** (same directory the Avalonia app writes profiles / `BooksDbContext` into), as `queue.json` | new, **shared with GUI** |
| Job history (completed/failed) | Same dir, `history.jsonl` (append-only) | new, **shared with GUI** |
| Logs | XDG-style: `~/.local/state/oahu/logs/oahu-cli-YYYYMMDD.log` (Linux/macOS), `%LOCALAPPDATA%\oahu\logs\…` (Windows), daily rotated | new, CLI-only (per-binary) |
| OAuth/device tokens & decryption keys | OS keychain via `ICredentialStore` | new, replaces any plaintext usage if currently present |

The CLI **never** writes anything inside the Avalonia GUI's user-data area beyond what the existing `Oahu.Data` / profile machinery already does — so the two front-ends coexist on the same machine using the same library cache.

---

## 8. Concurrency & job pipeline

- Single in-process worker (`JobScheduler`) consumes a bounded `Channel<JobRequest>`.
- Concurrency knob (default `2`) controls how many books run their pipeline phases in parallel; phases inside one job run sequentially (download → decrypt → mux → optional export).
- Each phase reports progress via `IProgress<PhaseProgress>` → published as `JobUpdate` events on a `Channel<JobUpdate>` that both modes subscribe to.
- After every phase, persistent state is saved to SQLite (existing `SavePersistentState`) and the `queue.json` is rewritten atomically (write-temp-then-rename). Resuming after a crash:
  1. Read `queue.json`.
  2. For each item, ask `IAudibleApi.GetPersistentState` what phase it last reached.
  3. Re-enter the pipeline at that phase.
- `Ctrl+C` semantics in TUI mode (progressive):
  1. Cancel any active job's CTS.
  2. If no active jobs, close the topmost dialog.
  3. If no dialog open, prompt "Quit Oahu? [y/N]" — Enter quits.
  4. A second `Ctrl+C` within 2 s skips the prompt and exits immediately.

In command mode `Ctrl+C` cancels the current operation, waits up to a configurable grace period (default 5 s) for clean shutdown, then exits with code 130.

---

## 9. Output formats (command mode)

| Mode | Trigger | Shape |
|------|---------|-------|
| Pretty | TTY, no flag | Spectre tables, colors, spinners |
| Plain | `--plain` or non-TTY | Tab- or pipe-separated text, no escapes |
| JSON | `--json` | Stable, documented schema (versioned via `_schemaVersion`); arrays for collections, objects for single resources |

A small `IOutputWriter` interface handles all three. Every command picks a default and lets `--json`/`--plain` override.

JSON schemas for `library list`, `queue list`, `history list`, `auth status`, `doctor` are checked into `docs/cli-schemas/` and snapshot-tested.

---

## 10. Error handling

- One central `Errors` static class produces structured `OahuCliException`s with: a *what*, a *why*, and a *fix*.
- Every command's outer `try/catch` formats the error using semantic colors:
  ```
  ✗ Could not download "Project Hail Mary"
  
    The Audible API returned 401 Unauthorized.
    Re-authenticate with `oahu-cli auth login` and try again.
  ```
- Catch and rewrite: HTTP errors from `Oahu.Data` get translated; decrypt/mux errors from `Oahu.Decrypt` get a "Run `oahu-cli doctor` to verify your environment" hint.
- Most-important sentence last (eye is drawn there).
- Exit codes:
  - `0` success
  - `1` generic failure
  - `2` usage error (parse/validation)
  - `3` authentication required / failed
  - `4` Audible API error
  - `5` decryption / conversion error
  - `130` cancelled by user

---

## 11. Accessibility

- `ScreenReaderProbe` checks: `OAHU_NO_TUI=1`, `OAHU_SCREEN_READER=1`, plus on Windows `SystemParametersInfo(SPI_GETSCREENREADER)` via `Oahu.Foundation.Win32` (already present), plus `VOICEOVER_RUNNING` heuristics on macOS. On detection:
  - All animations disabled (spinners become a static `*`).
  - Decorative box drawing replaced with simple text labels.
  - Tables emit `Border = NoBorder`.
  - Status icons are always paired with their text label (`✓ Success`).
- Keyboard-only navigation is mandatory; no mouse-only feature.
- Focus order follows visual order; `Esc` always escapes; `?` always opens contextual help.

---

## 12. Distribution

- **`dotnet tool install -g Oahu.Cli`** (a `.nupkg` with `<PackAsTool>true</PackAsTool>`).
- **Self-contained single-file** binaries for `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`. Trimmed where possible; AOT is a stretch goal once we audit `Oahu.Core` for reflection.
- Existing channels:
  - **Homebrew tap** (`DavidObando/oahu`) — add an `oahu-cli` formula alongside the GUI.
  - **WinGet** — add `DavidObando.Oahu.Cli` manifest.
  - **GitHub Releases** — tarballs / zips per RID.
- Versioning aligned with the GUI via `version.json`.

---

## 13. Testing

| Layer | Approach |
|------|----------|
| Use cases (`Oahu.Cli.App`) | Pure C# unit tests; mock `IAudibleApi`, `IJobService`, etc. |
| Command parsing | `System.CommandLine` parser tests — every command & flag, plus error rewrites |
| Output formatting | `Spectre.Console.Testing.TestConsole` snapshots via `Verify` — one snapshot per command × output mode |
| TUI screens | TestConsole-based render tests; assert layout doesn't shift across state transitions |
| End-to-end | A `tests/e2e/` project that spawns the binary on Win/macOS/Linux runners; asserts `--version`, `--help`, `doctor` exit cleanly |
| Themes | Render every widget against every theme; assert no exceptions and sane contrast (manual inspection in CI artifact) |

A built-in `oahu-cli ui-preview` (hidden / experimental flag in v1) renders every widget under the current theme — invaluable when adding themes later, and reusable in CI as a "golden image" snapshot.

---

## 14. Implementation plan

The plan is split into **eight phases**, each independently mergeable. Phases 1–4 produce a working command-mode CLI; phases 5–7 add the TUI; phase 8 polishes. Each phase ends in a tagged release.

### Phase 0 — Spec acceptance
- Owner: David
- Deliverable: this document, reviewed and merged.
- Exit criteria: `docs/OAHU_CLI_DESIGN.md` checked in; tracking issue opened.

### Phase 1 — Skeleton & plumbing
- New solution items: `Oahu.Cli`, `Oahu.Cli.App`, `Oahu.Cli.Tui`, `Oahu.Cli.Tests` projects added to `Oahu.sln`.
- Add NuGet refs: `System.CommandLine`, `Spectre.Console`, `Microsoft.Extensions.Logging`, `Verify.Xunit`, `Spectre.Console.Testing`.
- Project-references to `Oahu.Core`, `Oahu.Data`, `Oahu.Decrypt`, `Oahu.Foundation`.
- Implement `oahu-cli --version`, `oahu-cli --help`, `oahu-cli doctor` (env checks: write perms on output dir, can read profile store, library cache reachable, Audible API reachable, disk free). **No FFmpeg check** — decryption and muxing are in-process via `Oahu.Decrypt` (AAXClean-derived), with no external binary dependency.
- Implement `setupProcess()` equivalent: `NO_COLOR`/`FORCE_COLOR` handling, `Console.OutputEncoding = UTF8`, console-redirector check, exit-trap (`try/finally` + `CancelKeyPress` + `ProcessExit`).
- Wire `Microsoft.Extensions.Logging` to a daily-rotating file logger.
- CI: add a build/test job for the four new projects.
- Exit criteria: `oahu-cli --version` works on all three OSes from CI.

### Phase 2 — Design system v1
- `Oahu.Cli.Tui`: tokens, icon set, themes (`Default`, `Mono`, `HighContrast`), `Theme.Use()`.
- Widgets: `StatusLine`, `HintBar`, `TimelineItem`, `Table`, `Select`, `Dialog`.
- Hooks: `TerminalSize`, `Breakpoint`, `ScreenReaderProbe`, `SshDetector`.
- Hidden command: `oahu-cli ui-preview` rendering each widget under the current theme.
- Snapshot tests for every widget.
- Exit criteria: `ui-preview` renders cleanly under `Default`, `Mono`, `HighContrast` themes; no layout shifts between states.

### Phase 3 — Use-case layer (`Oahu.Cli.App`)
- `IAuthService`, `ILibraryService`, `IJobService`, `IQueueService`, `IConfigService`, `IDoctorService` — interfaces and concrete implementations wrapping existing core libraries.
- `CallbackBroker` for Audible interaction callbacks (CAPTCHA/MFA/CVF/external login). In command mode it bridges to stdin; later in TUI mode it bridges to dialogs.
- `OahuConfig` model + JSON load/save with atomic write.
- `JobScheduler` with bounded `Channel<JobRequest>`, configurable concurrency, phase-by-phase persistence.
- `ICredentialStore` with three platform implementations (DPAPI / Keychain / libsecret).
- Unit tests against in-memory fakes of `IAudibleApi`.
- Exit criteria: tests pass; can run a fake end-to-end download against a recorded API.

### Phase 4 — Command-mode v1
- Commands: `auth login/status/logout`, `library list/sync/show`, `queue list/add/remove/clear`, `download`, `convert`, `history list/show/retry`, `config get/set/path`, `completion`.
- Three output writers (Pretty, Plain, JSON); `--json` / `--plain` flags; auto-degrade on non-TTY.
- Parse-error rewriter (`Try 'oahu-cli --help' for more information.`, "Did you mean…?" suggestions).
- JSON schemas checked into `docs/cli-schemas/` with snapshot tests.
- Shell completion scripts for bash/zsh/fish/pwsh.
- Exit criteria: every command runs end-to-end against a real account in a smoke test (manual once; recorded for CI replay).

### Phase 5 — `oahu-cli serve` v1 (MCP-stdio + loopback HTTP)

- New project `Oahu.Cli.Server` (depends on `Oahu.Cli.App`).
- Bring in the chosen MCP SDK; bring in `WebApplication.CreateSlimBuilder` for HTTP.
- Implement protocol adapters that map MCP tools and HTTP endpoints 1:1 to use cases (per §15.1 table). Stream `JobUpdate`s via MCP notifications and SSE.
- Auth (per §15.2):
  - **stdio MCP**: implicit trust + per-tool confirmation policy on stderr; `--unattended` bypass.
  - **loopback HTTP / MCP-streamable HTTP**: pre-shared bearer token in `0600` file (auto-generated on first run), **and** Unix-socket / named-pipe support (`--listen unix`).
  - Server **refuses to bind to a non-loopback address** in 1.0.
  - Capability-class enforcement (`safe`/`mutating`/`expensive`/`destructive`) wired through `AuthorizationHandler<>`.
  - Audit log to `<user-data-dir>/logs/server-audit.jsonl`.
  - Cooperative file lock with the GUI on the user-data dir; fail fast on conflict.
- `oahu-cli serve token rotate` rotates the loopback token.
- Documentation: a short `docs/OAHU_CLI_SERVER.md` with a Claude Desktop config snippet and a `curl` recipe.
- Tests: protocol-level integration tests against a fake `Oahu.Cli.App`; end-to-end smoke calling `auth_status` over stdio MCP and `GET /v1/library` over loopback HTTP from the test harness.
- Exit criteria: Claude Desktop can drive a real `library_list` → `queue_add` → `download` → `jobs_observe` flow against a recorded API; loopback HTTP reachable with the token; non-loopback bind refused.

### Phase 6 — TUI shell

- `AppShell` with header, tab bar, content area, pinned `HintBar` footer.
- Tabs: Home, Library, Queue, Jobs, History, Settings (each with placeholder content).
- Global keymap: `1`–`6`, `Tab`/`Shift+Tab`, `Esc`, `?`, `:`, `/`, `Ctrl+C` (progressive), `Ctrl+L`, `L`.
- Alt-screen entry/exit with full restoration on crash.
- Logs overlay (in-memory ring buffer of `Microsoft.Extensions.Logging` output).
- Refusal to enter TUI when not a TTY, with helpful message.
- Exit criteria: empty TUI navigable on Windows Terminal, macOS Terminal, iTerm2, VS Code terminal, Linux (gnome-terminal, alacritty); restores cleanly on `Ctrl+C` and on forced kill.

### Phase 7 — TUI screens & sign-in

- **Home**: greeting, profile, quick actions, last-N events.
- **Library**: searchable table with multi-select and detail panel; `q` queues.
- **Sign-in**: dialog-based flow that drives `IAuthService.LoginAsync` via `CallbackBroker`. Region picker, MFA dialog, external-login dialog with URL + QR code + paste-back input.
- **Settings**: read/write config + theme switcher.
- Exit criteria: a fresh user can `oahu-cli` → sign in → see their library inside the TUI without a single subcommand.

### Phase 8 — Queue, Jobs, History

- **Queue**: ordered list, drag-reorder (`Shift+↑/↓`), per-row remove/start.
- **Jobs**: live `TimelineItem` rows fed by `IJobService.ObserveAsync`; phase progress bars; expand row for detail; cancel per-job.
- **History**: paged list; re-run, view error; "open file" delegates to OS file browser.
- OSC 9;4 progress emission for terminal title/dock.
- Exit criteria: can queue 5 books, run them with concurrency=2, kill the process mid-flight, restart `oahu-cli`, and resume each from the right phase.

### Phase 9 — Polish, accessibility, distribution

- Screen-reader path verified with NVDA + VoiceOver.
- High-contrast and colorblind themes audited (APCA-style contrast check; CI checks contrast for body text Lc ≥ 30).
- Crash reporting (write a minidump-like report under logs dir on unhandled exception with environment context).
- Packaging: `dotnet tool` `.nupkg`, self-contained per-RID binaries, Homebrew formula update, WinGet manifest.
- Documentation: README section, `docs/OAHU_CLI_USAGE.md`, screen recording of TUI.
- Exit criteria: `oahu-cli` 1.0 released through all three channels.

### Stretch goals (post-1.0)

- **1.1 — LAN-capable `serve`**: `--bind 0.0.0.0` unlocked behind mandatory bearer token + TLS (`--cert`/`--key` or `--self-sign`); scoped tokens (`oahu-cli serve token create --scope read|write|admin --name <label>`) backed by `ICredentialStore`.
- **1.2 — MCP OAuth 2.1**: standards-compliant authorization-code-with-PKCE flow for streamable-HTTP MCP, only if real multi-client demand materializes.
- **1.3 — Auto-attach client mode**: TUI/command-mode detect a running local server and become clients (long downloads survive across CLI invocations).
- Native AOT publishing (after auditing core for reflection).
- Per-keybind override file (`keymap.json`).
- Plugin system (`oahu-cli plugin install …`) — unlikely to be needed.
- Daemon mode for headless servers (`oahu-cli daemon`).

---

## 15. Risks & open questions

| Risk | Mitigation |
|------|------------|
| `Oahu.Core` callbacks (CAPTCHA/MFA/CVF/external login) don't all map cleanly to a TUI dialog model | Spike in Phase 3 — write a `CallbackBroker` test harness against the real `Authorize`/`Login` paths before committing to Phase 6 dates |
| `BooksDbContext` is shared with the Avalonia app — concurrent access | EF Core + SQLite supports WAL mode; verify the GUI uses WAL or add a file-lock check before write paths in CLI |
| Spectre.Console's input handling not as rich as Ink for some keybinds (e.g. Kitty protocol, bracketed paste) | Accept v1 limitations; document; revisit if users complain |
| Native AOT may break reflection-heavy paths in `Oahu.Decrypt` | Stretch goal only; don't gate v1 on it |
| Cross-platform terminal-title (OSC 9;4) varies (Windows Terminal/iTerm2 yes, others no) | Detect via `TERM_PROGRAM` and degrade silently |
| ~~FFmpeg dependency for muxing~~ | **Not a risk:** decrypt/mux is fully in-process via `Oahu.Decrypt` (AAXClean-derived). No external FFmpeg binary is invoked anywhere in the Oahu codebase; the FFmpeg references in `Oahu.Decrypt/*` are URL comments to FFmpeg source as a format spec reference only. `oahu-cli doctor` does **not** check for ffmpeg. |

Resolved during design review:

1. **No `--gui` flag.** `oahu-cli` and the Avalonia GUI stay as separate, independently-launched binaries. Shipping them together is a packaging concern, not a CLI concern.
2. **Share queue/history with the GUI's user-data directory.** `queue.json` and `history.jsonl` live alongside the existing profile / `BooksDbContext` files (the same dir the Avalonia app writes to today), not under XDG `~/.local/state/oahu/`. This means the GUI can later surface the CLI's queue and history without any migration step. Logs and the CLI-only `config.json` stay under XDG-style dirs because they're per-binary.
3. **`oahu-cli serve` (MCP/HTTP façade): post-1.0, included in the roadmap.** See §15.1 below for the design sketch.

### 15.1 `oahu-cli serve` — MCP / HTTP façade

Goal: expose the same `Oahu.Cli.App` use cases that the TUI and command-mode use, over a network-ish boundary, so external automation (LLM agents, scripts, Home Assistant, a future web dashboard) can drive Oahu without spawning a subprocess per call.

**Roadmap placement:** MCP-stdio + loopback HTTP land in **1.0** (Phase 5). LAN exposure, scoped tokens, and OAuth 2.1 are post-1.0 (1.1+).

Two protocols, one binary, same façade:

| Surface | Transport | Audience | Status |
|---------|-----------|----------|--------|
| **MCP** (Model Context Protocol) | JSON-RPC over **stdio** (default) and **streamable HTTP** | LLM agents — Claude Desktop, Copilot CLI, Cursor, custom agents | stdio in 1.0; streamable HTTP loopback in 1.0; LAN post-1.0 |
| **HTTP/JSON** | REST-ish over loopback (or LAN with `--bind`) | scripts, Home Assistant, future web UI, curl | loopback in 1.0; LAN post-1.0 |

Both surfaces are thin adapters over the existing `IAuthService`, `ILibraryService`, `IQueueService`, `IJobService`, etc. — no new business logic.

**Tools / endpoints (1:1 with use cases):**

| Use case | MCP tool | HTTP endpoint | Capability class |
|----------|----------|---------------|------------------|
| Auth status | `auth_status` | `GET /v1/auth` | safe |
| Library list | `library_list` (args: `filter`, `unread`, `limit`) | `GET /v1/library` | safe |
| Library show | `library_show` (args: `asin`) | `GET /v1/library/{asin}` | safe |
| Library sync | `library_sync` (args: `full`) | `POST /v1/library/sync` | expensive |
| Queue list | `queue_list` | `GET /v1/queue` | safe |
| Queue add | `queue_add` (args: `asins`) | `POST /v1/queue` | mutating |
| Queue remove | `queue_remove` (args: `jobIds`) | `DELETE /v1/queue/{jobId}` | mutating |
| Run | `download` (args: `asins`, `quality`, …) | `POST /v1/jobs` | expensive |
| Observe jobs | `jobs_observe` (streams `JobUpdate`s) | `GET /v1/jobs?stream=sse` | safe |
| History list/show | `history_list`, `history_show` | `GET /v1/history`, `GET /v1/history/{jobId}` | safe |
| History delete | `history_delete` (args: `jobIds`) | `DELETE /v1/history/{jobId}` | destructive |
| Doctor | `doctor` | `GET /v1/doctor` | safe |
| Config get/set | `config_get`, `config_set` | `GET/PUT /v1/config` | safe / mutating |

`auth_login` is **never** exposed over the server — it requires interactive browser + MFA/CVF/CAPTCHA flows on the user's terminal. Login stays in TUI / command mode.

**Why MCP first.** MCP is the lowest-friction integration with LLM agents. A user can add Oahu to Claude Desktop or Copilot CLI with three lines of config, then say *"Claude, queue the unread sci-fi books from my Audible library and start downloading them"* and Claude calls `library_list` → `queue_add` → `download` → `jobs_observe` on its own.

**Why HTTP too.** Some integrations aren't agentic — Home Assistant automations, shell scripts, `curl` from anywhere on the LAN, a future small web dashboard. HTTP is the universal back-stop. Server-Sent Events for `jobs_observe` streaming.

**Resources (MCP) and links (HTTP).** MCP also has a "resources" concept (read-only data the agent can fetch on demand). Natural fits: `oahu://library`, `oahu://queue`, `oahu://history`, `oahu://logs/today`. HTTP exposes the same shapes as `GET` endpoints.

**Process model.**

```
$ oahu-cli serve --mcp                          # stdio MCP — Claude Desktop / Copilot CLI
$ oahu-cli serve --mcp --http                   # stdio MCP + loopback HTTP REST + MCP-streamable HTTP
$ oahu-cli serve --http --listen unix           # local-only via Unix socket / named pipe
$ oahu-cli serve --http --bind 0.0.0.0:7331 \   # opt-in LAN exposure (post-1.0)
              --token-file ./tok --tls-self-sign
```

- One process at a time owns the database/queue. The server takes a file lock on the GUI's user-data dir (matching the GUI's lock scheme). Trying to run the GUI and `oahu-cli serve` simultaneously surfaces a clear "already in use by …" error rather than silent corruption.
- The TUI and command-mode become *clients* of the server when one is running on the local machine — they detect the lock-file and switch to talking to the server over MCP-stdio (`oahu-cli serve --mcp` they spawn themselves) or loopback HTTP. Optional; v1 doesn't require it. But it makes long-running downloads survive across CLI invocations cleanly.

### 15.2 Authentication & authorization model

Three surfaces, three threat models, layered defenses.

#### Surface 1 — MCP-stdio

Threat: whatever local process spawned us. No network.

- **Implicit trust.** Standard for MCP-over-stdio (Claude Desktop, Cursor, Copilot CLI all assume this).
- **Per-tool confirmation policy** for non-`safe` capability classes when stderr is a TTY (i.e. a human is watching). Confirmation prompts go to **stderr** so they don't pollute the JSON-RPC stream on stdout. Bypass with `--unattended`.
  - `safe` → always auto-allowed.
  - `mutating` / `expensive` → confirm under stdio (allow when `--unattended` or under HTTP-with-token).
  - `destructive` → always confirm; require `confirm: true` argument under `--unattended`.

#### Surface 2 — Loopback HTTP (default for `--http`, `--mcp --http`)

Threat: other local users on a multi-user host; rogue local processes.

Two interchangeable mechanisms, pick one (both supported in 1.0):

- **Pre-shared bearer token in a `0600` file** (default for TCP loopback).
  - Generated on first `serve` run; stored at `~/.config/oahu/server.token` (Unix mode `0600`) / `%APPDATA%\oahu\server.token` (Windows ACL: current user only).
  - HTTP requires `Authorization: Bearer <token>` on every request.
  - The server **refuses to start** if the token file's permissions are too loose.
  - TUI/command-mode/local clients read the same file before connecting.
  - Rotate by deleting the file and restarting.
- **Unix domain socket / named pipe** (`--listen unix`).
  - Server listens on `~/.local/state/oahu/server.sock` (mode `0600`) or `\\.\pipe\oahu-cli` (SDDL: current user only).
  - No token needed; OS enforces who can connect.
  - Best option for purely local consumers, but not all clients speak Unix sockets out of the box.

Optional defense-in-depth (off by default in 1.0, opt-in via `--strict-peer`):

- **Per-process verification** — `SO_PEERCRED` on Linux, `getpeereid` on macOS, `GetExtendedTcpTable` on Windows. Reject any connection from a process not owned by the current user. Only relevant on multi-user boxes.

#### Surface 3 — LAN / external (`--bind 0.0.0.0`) — **post-1.0**

Threat: the entire network.

The 1.0 server **refuses to bind to a non-loopback address**. Post-1.0 unlocks two paths:

- **Bearer token + TLS** (1.1 target).
  - Mandatory `--token <file>` (or `OAHU_TOKEN` env var).
  - Mandatory TLS — `--cert`/`--key`, or `--self-sign` (mints a self-signed cert at startup, prints fingerprint to stderr for client trust).
  - Optional **scoped tokens** — `oahu-cli serve token create --scope read|write|admin --name <label>` mints a token, prints it once, stores hashed verifier (Argon2id) on disk and the active server's verifier secret in the OS keychain via `ICredentialStore`. Issue read-only tokens to Home Assistant; full tokens to your own scripts.
- **MCP-spec OAuth 2.1 with PKCE** (1.2+ target, only if real demand).
  - The MCP spec's authorization-code flow for streamable-HTTP transports. Per-client identity, real audit, scope enforcement, refresh and revocation.
  - Heavyweight; only worth implementing if multi-client / multi-user scenarios materialize.

#### Cross-cutting rules (apply to all surfaces)

- **`auth_login` is never callable** over the server. Period.
- **Capability-class enforcement is a server-side check**, not a client convention. Even a valid token can't call a tool outside its scope.
- **Audit log**: every authenticated call appended to `<user-data-dir>/logs/server-audit.jsonl` as `{ts, transport, principal, tool, args_hash, outcome, latency_ms}`. `args_hash` is a SHA-256 of the canonicalized arg JSON so we can correlate without storing book titles or ASINs in the audit trail.
- **Audible credentials are never on the wire.** The server can return that auth is required, but never exposes the underlying tokens or device key. `auth_logout` is destructive but doesn't leak anything.
- **Rate limit** writes (10 req/min/principal default for `mutating`+`expensive` tools) on loopback even — defends against a runaway agent.
- **Discovery hardening**: in 1.0, the server prints a clear "ALLOWED FROM: localhost only" line at startup. Add a `--print-config` flag for `oahu-cli doctor` to dump the active auth posture.

#### What the user actually sees

- **First MCP-stdio integration** (Claude Desktop):
  ```jsonc
  // claude_desktop_config.json
  {
    "mcpServers": {
      "oahu": { "command": "oahu-cli", "args": ["serve", "--mcp"] }
    }
  }
  ```
  Zero auth config. Confirmation prompts come up in the user's existing `oahu-cli` log/notification channel for destructive/expensive tools (configurable per-tool in `config.json`).
- **First HTTP integration** (curl / Home Assistant):
  ```bash
  $ oahu-cli serve --http &
  Listening on http://127.0.0.1:7331
  Token: ~/.config/oahu/server.token  (mode 0600)
  $ curl -H "Authorization: Bearer $(cat ~/.config/oahu/server.token)" \
         http://127.0.0.1:7331/v1/library
  ```
- **First Unix-socket integration**:
  ```bash
  $ oahu-cli serve --http --listen unix &
  Listening on unix:///home/.../server.sock  (mode 0600)
  $ curl --unix-socket ~/.local/state/oahu/server.sock http://localhost/v1/library
  ```

#### Stack (proposed for the implementation phase)

- MCP: an existing .NET MCP SDK (e.g. the [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) NuGet) — small enough to hand-roll JSON-RPC if the SDK churns.
- HTTP: minimal-API ASP.NET Core (`WebApplication.CreateSlimBuilder`) — trim/AOT-friendly. Auth implemented as a thin `AuthenticationHandler<>` + a custom `IAuthorizationRequirement` for capability classes.
- Both bound to the same `Oahu.Cli.App` services via DI; the existing `IJobService.ObserveAsync()` `IAsyncEnumerable<JobUpdate>` stream maps directly to MCP notifications and SSE.

#### Open questions to resolve before 1.0 ships

- Multi-account: do MCP tools need a `profile` argument, or do we infer the active profile? (Probably infer, with `auth_status` returning the active one.)
- Long-running tools: confirm the chosen MCP SDK supports streaming progress notifications; otherwise fall back to a `start + observe` pattern.
- Confirmation UX under stdio: when no TTY on stderr, we must auto-deny non-`safe` tools — verify Claude Desktop / Copilot CLI surface the resulting error nicely.

---

## 16. Appendix — file/project layout

```
Oahu.sln                        (existing)
src/
  Oahu.App/                     (existing, Avalonia)
  Oahu.Core/                    (existing)
  Oahu.Data/                    (existing)
  Oahu.Decrypt/                 (existing)
  Oahu.Foundation/              (existing)
  Oahu.UI/                      (existing)
  Oahu.Cli/                     (NEW — entry point + command handlers)
  Oahu.Cli.App/                 (NEW — use cases, services, broker, scheduler)
  Oahu.Cli.Tui/                 (NEW — design system + screens)
  Oahu.Cli.Server/              (NEW — MCP + HTTP façade, auth, audit)
tests/
  Oahu.Cli.Tests/               (NEW — unit + snapshot)
  Oahu.Cli.E2E/                 (NEW — spawn-binary smoke tests)
docs/
  OAHU_CLI_DESIGN.md            (this file)
  OAHU_CLI_SERVER.md            (NEW — Claude Desktop config + curl recipes)
  cli-schemas/                  (NEW — JSON schemas, snapshot-tested)
  CROSS_PLATFORM_AVALONIA_SPEC.md   (existing)
  …
```

---

## 17. References

- Internal: `docs/CROSS_PLATFORM_AVALONIA_SPEC.md`.
- External: [Spectre.Console docs](https://spectreconsole.net/), [System.CommandLine](https://learn.microsoft.com/dotnet/standard/commandline/), [no-color.org](https://no-color.org/), [clig.dev](https://clig.dev/).
