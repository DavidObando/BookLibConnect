# `oahu-cli` — TUI Exploration

Status: **Exploratory / Draft**
Companion to: [`OAHU_CLI_DESIGN.md`](./OAHU_CLI_DESIGN.md)

This document explores how the TUI mode of `oahu-cli` could *look and feel*.
Every mockup below is rendered in plain ASCII/Unicode (single-width glyphs
only) so it doubles as both a design artifact and a visual contract for the
`Oahu.Cli.Tui` widget set. Workflows are described alongside each screen.

The mockups intentionally apply the constraints from the good-practices
report:

- Fixed-width 4-char status prefix (`◐   `, `✓   `, `✗   `) — never reflows.
- Pinned `HintBar` footer on every screen.
- Bordered alt-screen container with `╭ ╮ ╰ ╯` for the primary frame.
- Single-width Unicode icons only; ASCII fallbacks listed where relevant.
- Three responsive breakpoints: `compact` (<80), `narrow` (80–119), `wide` (≥120).
- `stdout = data`, but in TUI mode the *whole* surface is one alt-screen frame.

Frames below are drawn at **120 columns × 32 rows** unless explicitly
labelled as a different breakpoint. Treat the outer border as the
alt-screen edge. The header is pinned at the top, the `HintBar` is pinned
at the bottom, and only the middle scrolls.

---

## Table of contents

1. [The AppShell chrome](#1-the-appshell-chrome)
2. [Home](#2-home)
3. [Library — wide layout](#3-library--wide-layout)
4. [Library — narrow / compact reflow](#4-library--narrow--compact-reflow)
5. [Queue](#5-queue)
6. [Jobs (live)](#6-jobs-live)
7. [History](#7-history)
8. [Settings](#8-settings)
9. [Logs overlay](#9-logs-overlay)
10. [Sign-in dialog flow](#10-sign-in-dialog-flow)
11. [Command palette (`:`)](#11-command-palette-)
12. [Doctor](#12-doctor)
13. [Errors and recoveries](#13-errors-and-recoveries)
14. [Progressive `Ctrl+C`](#14-progressive-ctrlc)
15. [Accessibility / mono fallback](#15-accessibility--mono-fallback)
16. [Animation cookbook](#16-animation-cookbook)
17. [Open questions](#17-open-questions)

---

## 1. The AppShell chrome

Every screen lives inside the same chrome:

```
╭─ oahu ─────────────────────────────── david@us · 2 jobs running ─ v0.1.0 ─╮
│  1 Home   2 Library   3 Queue   4 Jobs   5 History   6 Settings          │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│                          ( screen content )                              │
│                                                                          │
├──────────────────────────────────────────────────────────────────────────┤
│ 1-6 tabs · Tab next · / search · : palette · ? help · Ctrl+C quit        │
╰──────────────────────────────────────────────────────────────────────────╯
```

Key design decisions:

- **Header** (line 1): app name, active profile + region, a *coalesced*
  activity summary (`2 jobs running`, `idle`, `syncing library…`), and the
  binary version. Activity is the only piece that animates; everything else
  is static text so the eye isn't pulled to the chrome.
- **Tab strip** (line 2): always visible. Number prefixes double as
  one-keystroke jumps (`1`–`6`). The active tab is rendered with the
  `Selected` token (background fill); the others use `TextSecondary`.
- **Body**: scrollable region. *Only this region* compresses on small
  terminals; header + tabs + footer use `flex-shrink: 0` (Spectre `Layout`
  ratios `0/1/0`).
- **HintBar** (last line): pinned, contextual, auto-formatted from a
  `Dictionary<string,string>` like `{ ["1-6"]="tabs", ["/"]="search", … }`.
  Falsy values are filtered, so a screen can drop hints conditionally
  without conditionals in markup.

Layout stability rule: the chrome itself **never reflows** when the active
verb in the header changes from `idle` → `syncing…` → `2 jobs running`
because the header right-aligns the activity verb inside a fixed-width
slot.

---

## 2. Home

The first screen after launch (or after `oahu-cli tui`). Its job is to
answer four questions in one glance:

1. Am I signed in? As whom? Where (region)?
2. Is anything happening right now?
3. What did I do last?
4. What can I do next?

```
╭─ oahu ─────────────────────────────── david@us · idle ─────── v0.1.0 ─╮
│  1 Home   2 Library   3 Queue   4 Jobs   5 History   6 Settings       │
├───────────────────────────────────────────────────────────────────────┤
│                                                                       │
│   Aloha, david.                                                       │
│                                                                       │
│   Account ──────────────────────────────────────────────────────────  │
│     ✓  Signed in as david@example.com  (region: us, last sync 2 h ago)│
│     ●  3 books queued, 0 running                                      │
│                                                                       │
│   Quick actions ────────────────────────────────────────────────────  │
│     ❯ Sync library                          (also:  s )               │
│       Resume queue   (3 items)              (also:  r )               │
│       Open downloads folder                 (also:  o )               │
│       Sign in with another account                                    │
│                                                                       │
│   Recent ───────────────────────────────────────────────────────────  │
│     ✓  Project Hail Mary             — converted to .m4b  · 2 h ago   │
│     ✓  The Three-Body Problem        — downloaded         · 2 h ago   │
│     ✗  The Way of Kings              — decrypt failed     · yesterday │
│        └ activation bytes mismatch — see Logs (L)                     │
│                                                                       │
├───────────────────────────────────────────────────────────────────────┤
│ ↑↓ navigate · Enter run · s sync · r resume · L logs · ? help         │
╰───────────────────────────────────────────────────────────────────────╯
```

**Workflow.** Arrow keys move between the four quick actions; `Enter`
runs the selected one. The recent-events list is read-only but each entry
is selectable so `Enter` on a `✗` row jumps to the relevant Jobs/History
entry. The `s`, `r`, `o` shortcuts are *aliases* of the menu items —
discoverable via the parenthesized hints, but not required.

---

## 3. Library — wide layout

The most-used screen. At ≥ 120 cols we render a master/detail split: a
filterable book list on the left and a detail panel on the right.

```
╭─ oahu ─────────────────────────────── david@us · idle ─────────────────────────── v0.1.0 ─╮
│  1 Home   2 Library   3 Queue   4 Jobs   5 History   6 Settings                           │
├──────────────────────────────────────────┬────────────────────────────────────────────────┤
│  / project                               │  Project Hail Mary                             │
│                                          │  by Andy Weir                                  │
│   ✓  ●  Project Hail Mary       Weir     │  16 h 10 m · narrated by Ray Porter            │
│   ◐  ○  The Martian             Weir     │                                                │
│   ✗  ○  Artemis                 Weir     │  Status                                        │
│                                          │    ✓ Downloaded  (.aax · 1.2 GB)               │
│   ●  ○  The Three-Body Problem  Liu      │    ✓ Decrypted   (.m4b · 1.1 GB)               │
│   ○  ○  The Dark Forest         Liu      │    ✓ Exported    ~/Audiobooks/Project Hail Mary│
│   ○  ○  Death's End             Liu      │                                                │
│                                          │  Actions                                       │
│   ○  ○  The Way of Kings        Sanderson│    ❯ Re-export to .m4b                         │
│   ○  ○  Words of Radiance       Sanderson│      Re-decrypt                                │
│   ○  ○  Oathbringer             Sanderson│      Reveal in Finder                          │
│                                          │      Remove from local library                 │
│   ○  ○  Foundation              Asimov   │                                                │
│   ○  ○  Foundation and Empire   Asimov   │  Description                                   │
│   ○  ○  Second Foundation       Asimov   │    A lone astronaut wakes on a spaceship with  │
│                                          │    no memory of his mission…                   │
│                                          │                                                │
│   showing 12 of 287 · selection: 2       │                                                │
├──────────────────────────────────────────┴────────────────────────────────────────────────┤
│ ↑↓ navigate · Space select · q queue · / filter · u toggle unread · Enter actions · ?     │
╰───────────────────────────────────────────────────────────────────────────────────────────╯
```

Symbols used in the list columns:

| Col | Glyph                          | Meaning                                       |
|-----|--------------------------------|-----------------------------------------------|
| 1   | `✓` / `◐` / `✗` / blank        | Local file state (downloaded / running / failed / none) |
| 2   | `●` / `○`                      | Multi-select toggle (filled = selected)       |
| 3+  | Title, Author                  | Two-column right-padded text                  |

**Workflow.**

```
[Library] ─(/)─► filter "project" ─(↑↓)─► focus row ─(Space)─► toggle selection
                                                       │
                                                       ├─(q)─► enqueue selection ─► [Queue]
                                                       └─(Enter)─► open Actions menu in detail panel
```

The filter is a live filter (Copilot-style), not a modal prompt — typing
in `/` mode rewrites the visible rows on every keystroke. `Esc` clears
the filter without losing selection. `u` toggles "unread/undownloaded
only", which is just a sugar over `--unread` from command mode.

Scrolling is virtualized (Spectre table inside a viewport); the indicator
`showing 12 of 287` is the only place we tell the user there's more.

---

## 4. Library — narrow / compact reflow

At `narrow` (80–119) the detail panel collapses into an expandable row
underneath the focused list item:

```
╭─ oahu ─────────────────────────── david@us · idle ─── v0.1.0 ─╮
│  1 Home   2 Library   3 Queue   4 Jobs   5 History   6 Set..  │
├───────────────────────────────────────────────────────────────┤
│  / project                                                    │
│                                                               │
│   ✓  ●  Project Hail Mary       Weir              16 h 10 m   │
│   ▼  Status: downloaded · decrypted · exported                │
│      Actions: Re-export · Re-decrypt · Reveal · Remove        │
│                                                               │
│   ◐  ○  The Martian             Weir              10 h 53 m   │
│   ✗  ○  Artemis                 Weir               8 h 56 m   │
│                                                               │
├───────────────────────────────────────────────────────────────┤
│ ↑↓ nav · Space sel · q queue · → expand · / filter · ?        │
╰───────────────────────────────────────────────────────────────╯
```

At `compact` (<80) the Author column drops, the duration drops, and the
hint bar collapses to the highest-priority three:

```
╭─ oahu ─────────────────────────────────╮
│ 1 Home  2 Lib  3 Q  4 Jobs  5 Hx  6 ⚙  │
├────────────────────────────────────────┤
│ /                                      │
│                                        │
│  ✓ ● Project Hail Mary                 │
│  ◐ ○ The Martian                       │
│  ✗ ○ Artemis                           │
│  ● ○ The Three-Body Problem            │
│  ○ ○ The Dark Forest                   │
│  ○ ○ Death's End                       │
│                                        │
├────────────────────────────────────────┤
│ ↑↓ nav · Space sel · q queue           │
╰────────────────────────────────────────╯
```

This mirrors the Copilot "messages adapt to width" pattern — same
information, three densities. Importantly, neither the icon column nor the
selection column ever changes width across breakpoints.

---

## 5. Queue

The queue is the staging area between Library and Jobs. Items here are
not running yet (or are paused); reorder them, prune them, then `Run all`
or per-row `Run`.

```
╭─ oahu ─────────────────────────────── david@us · idle ─────────────────── v0.1.0 ─╮
│  1 Home   2 Library   3 Queue   4 Jobs   5 History   6 Settings                   │
├───────────────────────────────────────────────────────────────────────────────────┤
│  Pending (3) · concurrency 2 · default export .m4b                                 │
│                                                                                   │
│  #  Title                                Author        Quality    Plan            │
│  1  ❯ The Three-Body Problem             Liu           Enhanced   dl → dec → m4b  │
│  2    The Dark Forest                    Liu           Enhanced   dl → dec → m4b  │
│  3    The Way of Kings                   Sanderson     Enhanced   dl → dec → m4b  │
│                                                                                   │
│  Actions                                                                          │
│    ❯ Run all                                                                      │
│      Run selected                                                                 │
│      Pause all                                                                    │
│      Clear queue                                                                  │
│                                                                                   │
├───────────────────────────────────────────────────────────────────────────────────┤
│ ↑↓ navigate · Shift+↑↓ reorder · x remove · Enter run · p pause · c clear · ?     │
╰───────────────────────────────────────────────────────────────────────────────────╯
```

**Workflow.**

- `Shift+↑/↓` swaps the focused row with its neighbour. The `#` column is
  the only thing that changes on reorder; everything else is positionally
  stable thanks to fixed column widths.
- `x` removes the focused row (with a one-keystroke `Esc` to cancel a
  fresh removal, vim-style).
- `Enter` on a row runs *just that book*, leaving the rest queued.
- `Enter` on `Run all` pushes everything to Jobs and switches the active
  tab to `4 Jobs` so the user sees the live timeline immediately.

A subtle but important rule: the Queue screen is *not* a job tracker.
Once an item starts running it disappears from here and appears in Jobs.
That keeps each screen single-purpose and prevents an item from being
in two visual states at once.

---

## 6. Jobs (live)

The most "alive" screen. Each running job is a `TimelineItem`; phase
progress is rendered as inline bars *inside* the timeline detail.

```
╭─ oahu ─────────────────────── david@us · 2 jobs running · 18.4 MB/s ─── v0.1.0 ─╮
│  1 Home   2 Library   3 Queue   4 Jobs   5 History   6 Settings                 │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                 │
│  ◐    The Three-Body Problem                                       Liu          │
│       ├ download   ████████████████████░░░░░░░░░░░░  62 %  712 / 1144 MB        │
│       ├ decrypt    ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░   0 %  pending              │
│       ├ mux        ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░   0 %  pending              │
│       └ export     ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░   0 %  pending              │
│                                                                                 │
│  ◐    The Dark Forest                                              Liu          │
│       ├ download   ████████████████████████████████ 100 %  done                 │
│       ├ decrypt    █████████████░░░░░░░░░░░░░░░░░░░  41 %  ETA 0:45             │
│       ├ mux        ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░   0 %  pending              │
│       └ export     ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░   0 %  pending              │
│                                                                                 │
│  ✓    Project Hail Mary                                            Weir         │
│       Completed in 4 m 12 s · /Users/david/Audiobooks/Project Hail Mary.m4b     │
│                                                                                 │
│  ✗    Artemis                                                      Weir         │
│       └ decrypt failed: activation bytes mismatch                               │
│         Try `oahu-cli auth login` then retry, or open Logs (L)                  │
│                                                                                 │
├─────────────────────────────────────────────────────────────────────────────────┤
│ ↑↓ nav · Enter expand · c cancel · r retry · L logs · Esc back · Ctrl+C cancel  │
╰─────────────────────────────────────────────────────────────────────────────────╯
```

Key behaviours:

- **Status prefix is 4 cells wide.** `◐   ` and `✓   ` and `✗   ` all
  occupy the same column, so when a job finishes the title text does not
  shift one character to the right. This is the single most important
  detail of the screen.
- **Inline progress bars** are rendered with `█` and `░`, both single
  width. Percentages are right-padded to `100 %` so the trailing fields
  (`MB`, `ETA`) stay column-aligned across rows.
- **Aggregate metric** in the header (`18.4 MB/s`) is the sum across
  active downloads, throttled to update at most twice a second so it
  isn't jittery to read.
- **Failed rows** carry their own next-step hint inline, mirroring the
  "errors are documentation" rule. The same exception object renders
  here, in the Logs overlay, and in `oahu-cli history show` JSON — one
  source of truth, three surfaces.

`OSC 9;4` progress is also emitted so Windows Terminal / iTerm2 / Konsole
shows progress on the title bar / dock when the window is backgrounded.

**Streaming model.** The screen subscribes to
`IJobService.ObserveAsync()` (an `IAsyncEnumerable<JobUpdate>`). Each
update mutates exactly one row; nothing is re-rendered top-to-bottom on
every tick. Spectre's `Live` display handles the diff.

---

## 7. History

A pageable list of completed / failed / cancelled jobs, with a detail
view that mirrors what `oahu-cli history show <jobId> --json` returns.

```
╭─ oahu ─────────────────────────────── david@us · idle ─────────────────── v0.1.0 ─╮
│  1 Home   2 Library   3 Queue   4 Jobs   5 History   6 Settings                   │
├───────────────────────────────────────────────────────────────────────────────────┤
│  filter: all (✓ ✗ ⊘) · since: 7d · 24 results                                     │
│                                                                                   │
│  When           Status   Title                              Took     Output       │
│  2026-04-24 14:02 ✓     Project Hail Mary                   4:12     .m4b 1.1 GB  │
│  2026-04-24 13:48 ✓     The Three-Body Problem (download)   2:31     .aax 1.2 GB  │
│  2026-04-23 22:11 ✗     The Way of Kings (decrypt)          0:08     —            │
│  2026-04-23 19:55 ⊘     Foundation (cancelled by user)      0:42     —            │
│  2026-04-22 09:02 ✓     Dune                                7:48     .m4b 1.5 GB  │
│  …                                                                                │
│                                                                                   │
├───────────────────────────────────────────────────────────────────────────────────┤
│ ↑↓ nav · Enter open · r retry · f filter · / search · j JSON · Esc back · ?       │
╰───────────────────────────────────────────────────────────────────────────────────╯
```

`Enter` on a row opens a detail dialog with all phase timings, the final
output path (with OSC 8 hyperlink), the captured error if any, and a
`Retry` button that re-enqueues the same `BookRef` with the same options.

`j` toggles a JSON view of the focused row — useful for debugging and
for users who want to see exactly what the `--json` flag would emit
from command mode.

---

## 8. Settings

Settings is read/write over the same keys exposed by `oahu-cli config
get/set`. The screen is intentionally boring — a vertical list of
labelled fields grouped by section.

```
╭─ oahu ─────────────────────────────── david@us · idle ─────────────────── v0.1.0 ─╮
│  1 Home   2 Library   3 Queue   4 Jobs   5 History   6 Settings                   │
├───────────────────────────────────────────────────────────────────────────────────┤
│  Account ───────────────────────────────────────────────────────────────────────  │
│    Region                       us                       (Enter to change)        │
│    Profile                      david                    (Enter to change)        │
│    [Sign in with another account]                                                 │
│    [Sign out]                                                                     │
│                                                                                   │
│  Downloads ─────────────────────────────────────────────────────────────────────  │
│    Default quality              Enhanced                                          │
│    Output directory             ~/Audiobooks                                      │
│    Concurrency                  2          ◀  ▶                                   │
│    Default export               .m4b only                                         │
│                                                                                   │
│  Appearance ────────────────────────────────────────────────────────────────────  │
│    Theme                        Default       (Default · HighContrast · Mono · …) │
│    Animations                   Auto          (Auto · Off)                        │
│    Icons                        Unicode       (Unicode · ASCII)                   │
│                                                                                   │
│  Advanced ──────────────────────────────────────────────────────────────────────  │
│    Config file                  ~/.config/oahu/config.json   (Enter to open)      │
│    Log directory                ~/.local/state/oahu/logs     (Enter to open)      │
│    [Run doctor]                                                                   │
│                                                                                   │
├───────────────────────────────────────────────────────────────────────────────────┤
│ ↑↓ nav · Enter edit · ◀ ▶ adjust · s save · Esc back · ?                          │
╰───────────────────────────────────────────────────────────────────────────────────╯
```

Numerical fields use **arrow stepper** widgets (`◀  ▶`) rather than free
text where the value space is small (concurrency 1–8). This avoids modal
input dialogs for trivial edits and keeps the screen non-blocking.

`Esc` discards unsaved edits; `s` saves and shows a transient `✓ Saved`
toast in the HintBar (replacing the hints for ~1.5 s, then restoring).

---

## 9. Logs overlay

Toggled by `L` from any screen. Modal overlay (Dialog primitive) with
its own HintBar. Underlying screen darkens but is still partially visible
for context.

```
╭─ oahu ─────────────────────────────── david@us · 2 jobs running ──────── v0.1.0 ─╮
│  1 Home   2 Library   3 Queue   4 Jobs   5 History   6 Settings                  │
│                                                                                  │
│             ╭─ Logs ──────────────────────────────────────────────╮              │
│             │  level: all  ▾    filter: ___________________       │              │
│             │                                                     │              │
│             │  14:02:11.341 INF  Job 7d3a started: download …     │              │
│             │  14:02:11.402 DBG  Audible API: GET /1.0/library …  │              │
│             │  14:02:13.115 INF  Phase complete: download (2.3s)  │              │
│             │  14:02:13.118 INF  Phase started: decrypt           │              │
│             │  14:02:14.901 WRN  Activation bytes cache miss …    │              │
│             │  14:02:15.000 ERR  Decryption failed: bytes mismatch│              │
│             │  14:02:15.001 INF  Job 7d3a transitioned: failed    │              │
│             │  …                                                  │              │
│             │                                                     │              │
│             │  4 of 1 248 entries shown                           │              │
│             ╰─────────────────────────────────────────────────────╯              │
│                                                                                  │
├──────────────────────────────────────────────────────────────────────────────────┤
│ ↑↓ scroll · / filter · l level · c copy · L close · Esc close                    │
╰──────────────────────────────────────────────────────────────────────────────────╯
```

Notes:

- The dialog never grows past 80 % of the terminal width / height. On
  small terminals it occupies the full body region.
- `c` copies the visible (filtered) lines to the system clipboard via
  OSC 52 — works in iTerm2, Windows Terminal, Kitty, recent xterm.
- The `Logs` data is a bounded ring buffer (~10k entries) backed by the
  same `Microsoft.Extensions.Logging` provider that writes the rolling
  file logs.

---

## 10. Sign-in dialog flow

This is the trickiest interaction in the TUI because the underlying
`IAudibleApi` flow is *callback-driven* — captcha, MFA, CVF, and
external browser login all surface through `Callbacks`. The TUI bridges
those into modal `Dialog`s using a `CallbackBroker`.

### 10.1 Step 1 — region picker

```
            ╭─ Sign in to Audible ──────────────────────────╮
            │                                               │
            │   Choose your Audible region:                 │
            │                                               │
            │     ❯ United States       (us)                │
            │       United Kingdom      (uk)                │
            │       Germany             (de)                │
            │       France              (fr)                │
            │       Japan               (jp)                │
            │       Canada              (ca)                │
            │       Australia           (au)                │
            │       … 3 more                                │
            │                                               │
            │   [ Cancel ]   [ Continue ❯ ]                 │
            │                                               │
            ╰───────────────────────────────────────────────╯
                ↑↓ select · Enter continue · Esc cancel
```

### 10.2 Step 2 — credentials (default) with browser fallback

The TUI mirrors the Avalonia `ProfileWizardView` sign-in step exactly:
**direct email + password login is the default**, and the browser-based
flow is reachable by *unchecking* the "Sign in directly with email and
password" toggle. This keeps muscle memory portable between the GUI and
the CLI, and it keeps the most common case to two text fields.

#### 10.2.a Direct login (default)

```
            ╭─ Sign in to Audible (us) ─────────────────────────╮
            │                                                   │
            │   [✓] Sign in directly with email and password    │
            │                                                   │
            │   Email                                           │
            │   ╭───────────────────────────────────────────╮   │
            │   │ your@email.com                            │   │
            │   ╰───────────────────────────────────────────╯   │
            │                                                   │
            │   Password                                        │
            │   ╭───────────────────────────────────────────╮   │
            │   │ ••••••••••••                              │   │
            │   ╰───────────────────────────────────────────╯   │
            │                                                   │
            │   [ Cancel ]                       [ Sign in ❯ ]  │
            │                                                   │
            ╰───────────────────────────────────────────────────╯
              Tab fields · Space toggle · Enter submit · Esc cancel
```

Behavioural notes:

- The toggle row uses the same `[✓] / [ ]` checkbox glyph used in the
  Library multi-select column — single source of truth for "checked".
- The password field masks input with `•` (matches the Avalonia
  `PasswordChar="•"`), and never echoes characters even at debug level.
  We deliberately accept paste; the field width (`300px` in the GUI)
  maps to a fixed 43-column field in the TUI for visual parity.
- Credentials are held in memory only for the duration of this dialog;
  on submit they are passed to `Oahu.Core.ProgrammaticLogin` via the
  `CallbackBroker` and then immediately zeroed.
- If `Submit` returns a CAPTCHA / MFA / CVF / approval challenge, the
  credential fields are *hidden* and replaced by the challenge panel
  (10.3) — same behaviour as `ShowChallenge` in `ProfileWizardView`.
- A submit-in-flight state replaces the `[ Sign in ❯ ]` button with an
  inline `StatusLine` (`◐ Signing in… · Esc to cancel`) and disables
  the form fields. No layout shift — the button slot is fixed-width.

#### 10.2.b Browser fallback (toggle unchecked)

When the user unchecks the box, the dialog body re-renders to the
external-browser flow. This is the *backup* path — used when direct
login is blocked (e.g. some federated accounts, account recovery in
progress, or a region that requires WebAuthn).

```
            ╭─ Sign in to Audible (us) ─────────────────────────╮
            │                                                   │
            │   [ ] Sign in directly with email and password    │
            │                                                   │
            │   1. Open this URL in your browser:               │
            │                                                   │
            │      https://www.amazon.com/ap/signin?openid…     │
            │      ╔══════════════════╗                         │
            │      ║ █▀▀▀▀▀█  ▄▀ █▀▀▀ ║   ← scan with phone     │
            │      ║ █ ███ █ ▄▀▄ █ ██ ║                         │
            │      ║ █▄▄▄▄▄█ ▄ █ █▄▄▄ ║                         │
            │      ║ ████▄ ▀▀▄  ▀▀ ▀▄ ║                         │
            │      ╚══════════════════╝                         │
            │                                                   │
            │   [ Open in browser ]   [ Copy URL ]              │
            │   ✓ Login URL copied to clipboard                 │
            │                                                   │
            │   2. After signing in, paste the final URL your   │
            │      browser ended up on:                         │
            │                                                   │
            │      ╭───────────────────────────────────────╮    │
            │      │  https://www.amazon.com/ap/maplanding…│    │
            │      ╰───────────────────────────────────────╯    │
            │                                                   │
            │   [ Cancel ]                       [ Submit ❯ ]   │
            │                                                   │
            ╰───────────────────────────────────────────────────╯
              Space toggle · Tab fields · b open browser · y copy URL · Esc cancel
```

This panel is a 1:1 of the GUI's "browser-based login" branch, including
the inline `✓ Login URL copied/opened` confirmation that appears after
either action and disappears after the redirect URL is submitted. The QR
code renders at the smallest readable size (≈ 21×21 using Spectre's
block characters); on terminals that can't render block characters
legibly (`OAHU_ASCII_ICONS=1`, `TERM=dumb`) the QR is omitted and only
the URL + `Open in browser` / `Copy URL` actions remain.

Toggling the checkbox at any point preserves whatever was typed in the
*other* mode's fields (email/password kept while the browser panel is
showing, and vice-versa), so a user who guesses wrong about which mode
their account needs doesn't lose their input. This matches the Avalonia
behaviour where the two `StackPanel`s simply swap visibility on
`UseDirectLogin`.

### 10.3 Step 3 — secondary callbacks

CAPTCHA / MFA / CVF / approval all use the same dialog skeleton with
different bodies:

```
            ╭─ Two-factor code required ────────────────╮
            │                                           │
            │   Open the Authenticator app and enter    │
            │   the 6-digit code:                       │
            │                                           │
            │      ╭──────────────╮                     │
            │      │  _ _ _ _ _ _ │                     │
            │      ╰──────────────╯                     │
            │                                           │
            │   [ Cancel ]   [ Submit ❯ ]               │
            │                                           │
            ╰───────────────────────────────────────────╯
```

The `CallbackBroker` is the *same* broker used in command mode — there
it bridges to stdin prompts. In `--no-prompt` / non-TTY contexts it
fails fast naming the missing input (e.g. `error: a 2FA code is
required and stdin is not a TTY`).

### 10.4 Sequence

```
  User                    AppShell           CallbackBroker      Oahu.Core
   │                         │                      │                │
   │  selects "Sign in"      │                      │                │
   ├────────────────────────►│                      │                │
   │  picks region (10.1)    │                      │                │
   │                         │                      │                │
   │  enters email + pw      │                      │                │
   │  Enter on [Sign in ❯]   │                      │                │
   ├────────────────────────►│ ProgrammaticLogin ──────────────────► │
   │                         │  (email, password, region)            │
   │                         │                      │                │
   │                         │                      │      MFA?      │
   │                         │ ◄────────────────────┼──── awaits ────┤
   │                         │ push Dialog #3 (10.3)│                │
   │  enters code            │                      │                │
   ├────────────────────────►│ resolve(code) ──────►│ unblocks Task  │
   │                         │                      │                │
   │                         │ ◄─── Result.Ok ───────────────────────┤
   │                         │ Home updates: ✓ signed in             │
```

If the user unchecks "Sign in directly with email and password", the
flow swaps to the browser branch (10.2.b): the broker awaits an
`ExternalLoginCallback` instead of submitting credentials, the user
pastes the redirect URL, and the rest of the sequence (challenges,
final `Result.Ok`) is identical.

---

## 11. Command palette (`:`)

Press `:` from any screen to open a vim-style palette that accepts the
*same verbs* as command mode. This is the secret weapon for power
users — every TUI screen has a parity command, so muscle memory transfers
to scripts.

```
            ╭─ : ──────────────────────────────────────────────╮
            │  : queue add Project_                            │
            │                                                  │
            │    queue add  <asin|title>...                    │
            │    queue list                                    │
            │    queue clear                                   │
            │    queue remove  <jobId>...                      │
            │                                                  │
            │  ↑↓ navigate · Tab complete · Enter run · Esc    │
            ╰──────────────────────────────────────────────────╯
```

Behaviour:

- Typing filters the verb list with fuzzy matching (typo-tolerant).
- `Tab` autocompletes the longest common prefix.
- `Enter` runs the verb. If it's a TUI-equivalent action (e.g. `library
  list`) it just navigates to that screen with the same filter applied;
  if it's a destructive or slow action (e.g. `download Foo`) it
  *enqueues* and switches to Jobs.
- The palette never blocks the render loop; long-running verbs return a
  `JobId` and the palette closes immediately.

---

## 12. Doctor

`oahu-cli doctor` runs a series of environment checks. In TUI mode the
output renders as a `TimelineItem` stream (so checks tick in real time);
in command mode the same data renders as a Spectre `Table` (or JSON with
`--json`).

```
╭─ oahu ─────────────────────────────── david@us · running doctor ──────── v0.1.0 ─╮
│  Doctor                                                                          │
│                                                                                  │
│  ✓    .NET runtime              10.0.0 (osx-arm64)                               │
│  ✓    Output dir writable       ~/Audiobooks                                     │
│  ✓    Profile store readable    ~/Library/Application Support/Oahu/profile.db    │
│  ◐    Audible reachable …                                                        │
│  ✓    Audible reachable         api.audible.com (123 ms)                         │
│  !    Activation bytes          cache empty — will fetch on first decrypt        │
│  ✓    Library cache             287 books · last sync 2 h ago                    │
│  ✓    Disk free                 184 GB free on /                                 │
│                                                                                  │
│  Summary: 7 ok · 1 warning · 0 failed                                            │
│                                                                                  │
├──────────────────────────────────────────────────────────────────────────────────┤
│ Enter copy as JSON · r re-run · f try --fix · Esc back                           │
╰──────────────────────────────────────────────────────────────────────────────────╯
```

`f` triggers `oahu-cli doctor --fix` — currently a no-op for most checks,
but the affordance is present so it can grow (e.g. "create missing
output dir", "prime activation bytes cache").

---

## 13. Errors and recoveries

Every error in the TUI follows the same three-line shape — *what
happened, why, what to do* — borrowed verbatim from the good-practices
doc:

```
            ╭─ Couldn't sync library ───────────────────────────╮
            │                                                   │
            │  ✗  The Audible API returned 401 Unauthorized.    │
            │                                                   │
            │     Your session may have expired. Sign in again  │
            │     and retry.                                    │
            │                                                   │
            │     [ Cancel ]   [ Sign in ❯ ]   [ Retry ]        │
            │                                                   │
            ╰───────────────────────────────────────────────────╯
              Tab buttons · Enter activate · L logs · Esc dismiss
```

The "most-important sentence last" rule: the call-to-action button row
is what the user's eye lands on. The detailed payload (HTTP status,
correlation ID, etc.) is one keystroke away in `Logs` (`L`) — the
modal's HintBar advertises it.

---

## 14. Progressive `Ctrl+C`

`Ctrl+C` never just *kills the app* in TUI mode. It walks down a
priority list, escalating only as needed:

```
   press 1                   press 2 (within 2 s)
   ───────────────────────   ────────────────────
   any active job? ─► cancel that CTS, return.
   open dialog?    ─► close it, return.
   any unsaved settings edits? ─► confirm "Discard?".
   otherwise:
     show toast in HintBar:                  ─► exit immediately
       "Press Ctrl+C again to quit"
```

Visual treatment of the toast (replaces hints for ~2 s):

```
│ ⚠  Press Ctrl+C again to quit · Esc to stay                                       │
```

Two presses always exits, but a single press never wipes the alt-screen
when only the inner operation needs to stop. The cooperative two-press
exit returns process code `0` — `Ctrl+C` is the documented quit gesture
and no in-flight work is being interrupted at the moment of the second
press (the only branch that opens the exit window is the
"otherwise/idle" branch above). `Shift+Q` is the discoverable, non-SIGINT
alternative that also exits `0`. Code `130` is reserved for the runtime
force-exit fallback in `CliEnvironment` (when the cooperative state
machine fails to drain in time) and for command-mode `Ctrl+C`.

---

## 15. Accessibility / mono fallback

When `OAHU_NO_TUI=1`, `OAHU_SCREEN_READER=1`, `NO_COLOR=1`, or the
`ScreenReaderProbe` reports a screen reader active, the same `Library`
screen renders without animation, without box drawing, and with explicit
status labels.

```
oahu-cli — Library  (screen reader mode)

Filter: project
Selection: 2 of 12 visible (287 total)

  Downloaded · Selected · Project Hail Mary by Andy Weir
  Working    · Not selected · The Martian by Andy Weir
  Failed     · Not selected · Artemis by Andy Weir
  Downloaded · Selected · The Three-Body Problem by Liu Cixin
  …

Press question mark for help. Press q to enqueue selected items.
```

Rules:

- All animations off (spinners become a static `*` or are dropped).
- Decorative box drawing replaced with simple text labels.
- Tables render with `Border = NoBorder`.
- Status icons are *always* paired with their text label (`Downloaded`,
  `Working`, `Failed`) — never icon-only.
- The HintBar becomes a single text line per render (not pinned),
  because pinning a footer past a screen reader is hostile.

The `Mono` theme is also automatically selected when `NO_COLOR` is set
or when `Console.IsOutputRedirected` is true (the latter would normally
prevent TUI mode entirely, but the Mono palette is also what `--plain`
output uses for icons/labels).

---

## 16. Animation cookbook

A quick reference for which screen owns which kind of motion. Everything
else should be static. See `OAHU_CLI_DESIGN.md §6.6.1` for the full
"pulse vs. bar" decision rules; this table is the per-surface
application of them.

### 16.1 Per-surface motion

| Surface                          | Indicator                            | Animates?                                   | Cadence       |
|----------------------------------|--------------------------------------|---------------------------------------------|---------------|
| Header activity verb (idle)      | `PulseSpinner` + verb                | only while indeterminate work is in flight  | ~12 FPS       |
| Header activity verb (jobs running) | text + throttled throughput count | text swap only; number throttled            | 2 Hz max      |
| Tab strip                        | —                                    | never                                       | —             |
| Home → Recent list               | —                                    | never (static after render)                 | —             |
| Library row icon                 | `◐` (binary in-flight glyph)         | static; no per-frame animation              | —             |
| Queue rows                       | —                                    | never                                       | —             |
| Jobs row prefix                  | `◐` static while running             | binary in-flight only; phase bars do work   | —             |
| Jobs phase progress bar fill     | `█░` determinate                     | one per active phase                        | ~4 FPS        |
| Doctor "running check" row       | `PulseSpinner` + check name          | one per check while it's running            | ~12 FPS       |
| Header throughput                | numeric, throttled                   | numeric tween                               | 2 Hz max      |
| Logs overlay                     | —                                    | autoscroll only when pinned to bottom       | event-driven  |
| Dialog `StatusLine` (auth, sync) | `PulseSpinner` + verb + Esc hint     | only while awaiting a broker callback       | ~12 FPS (alt) / ~6 FPS (inline) |
| Sign-in QR                       | —                                    | never (static frame)                        | —             |

### 16.2 Pulse vs. bar — per surface

Mirrors `OAHU_CLI_DESIGN.md §6.6.1`. "Pulse" = `PulseSpinner`
(`● ◉ ◎ ○` cycle + color shimmer). "Bar" = determinate `█░` fill.
"Static `◐`" = single in-flight glyph that never animates (the row's
phase bars are doing the real reporting underneath).

| Activity                                                          | Indicator    | Why                                                         |
|-------------------------------------------------------------------|--------------|-------------------------------------------------------------|
| `Authenticating…`, `Awaiting 2FA…` (sign-in dialog)               | Pulse        | No quantifiable progress — pulse signals liveness only.     |
| `Syncing library…`, `Contacting Audible…`                         | Pulse        | API handshake, no byte/percent signal.                      |
| `Running checks…` (Doctor screen, per-check)                      | Pulse        | Each check is opaque; flips to `✓ / ! / ✗` on completion.   |
| Header verb when idle but a background fetch is in flight         | Pulse        | Same reasoning — keeps the chrome calm but alive.           |
| Download / decrypt / mux / export phase                           | Bar          | Real bytes / percent exist; bar is more honest than a pulse.|
| Jobs row that contains its own phase bars                         | Static `◐`   | Animating both row icon and bars below is noise.            |
| Settings "Saving…" toast (~1.5 s)                                 | Pulse → `✓`  | Brief opaque op; pulse for the duration, then a static check.|

### 16.3 PulseSpinner contract

`PulseSpinner` is the only "thinking"-style animation in the system. Its
contract is non-negotiable so screens can compose it freely:

- Frame set `● ◉ ◎ ○` (and back), each frame **single-width and the
  same width** as the others. Swapping the spinner for a `✓` or `✗`
  must not shift any column.
- Always paired with **a verb and an escape hint**:
  `◉ Authenticating · Esc to cancel · 1.2 KB`. The animation alone is
  decoration; the verb + hint is what makes it informative.
- **Color shimmer** sweeps a brighter shade across the verb's
  characters without changing the characters themselves — motion
  carried by color when the glyph snaps to the grid.
- Cadence: **~12 FPS** in alt-screen (the TUI's home turf), **~6 FPS**
  inline (e.g. one-shot command-mode renders that opt into a spinner).
- **Disabled** (replaced by a static `*`, or omitted entirely while the
  verb stays) when any of these are true:
  - `ScreenReaderProbe` reports a screen reader.
  - stdout is not a TTY.
  - `NO_COLOR=1` or `OAHU_NO_TUI=1` is set.
  - `SshDetector` reports a remote session and the user hasn't opted
    into full-cadence animation.

### 16.4 Two-tier render loop

Idle render is event-driven — **0 FPS** when nothing has registered as
animating. The ~12 FPS active loop spins up only when at least one
widget (a `PulseSpinner`, a Jobs phase bar, etc.) declares itself
in-flight, and goes back to sleep when the last one completes. This
keeps the animation cost at zero on idle screens and matters on SSH and
on battery — the system is silent when there's nothing to show.

---

## 17. Open questions

These are deliberately left open for the design review.

1. **Master/detail vs. modal detail.** The Library wide layout uses a
   permanent right pane. Do we want a `Tab`-toggle to maximise the list
   when the user is bulk-selecting? (Argues for: more density. Argues
   against: layout change per-screen breaks "predictable chrome".)
2. **Queue persistence semantics.** When the GUI and CLI are both open,
   whose `queue.json` wins on conflicting writes? Last-writer-wins is
   simplest but surprising; a file lock is safer but blocks. Probably
   need a `crash-only` merge step on startup.
3. **Multi-account UX.** Today the header shows one profile. Do we
   surface a profile switcher in the AppShell header (Spectre dropdown)
   or only in Settings? Tilt: Settings, with a `:profile use foo`
   palette command for power users.
4. **Cover art rendering.** The Library detail panel currently shows
   only text. Sixel / iTerm2 inline images / Kitty graphics protocol
   could render the cover. Worth it for v1, or strictly v2?
5. **Inline mode for one-shot commands.** Should `oahu-cli queue add`
   on a TTY render a small inline `TimelineItem` (à la `gh`) instead of
   plain text? Hybrid mode is mentioned in the practices doc; we'd need
   to decide screen-by-screen whether it's worth the implementation
   cost.
6. **Mouse support.** Spectre.Console doesn't ship rich mouse support,
   but click-to-focus on tab strip + scroll-wheel in the body would
   reduce the friction for casual users. Stretch goal.

These are all answerable; surfacing them now keeps the design honest.

---

## Appendix A — widget-to-screen matrix

A quick map from the design system widgets in `OAHU_CLI_DESIGN.md §6.3`
to the screens above, useful when prioritising the widget build-out.

| Widget            | Home | Library | Queue | Jobs | History | Settings | Logs | Sign-in | Palette | Doctor |
|-------------------|:----:|:-------:|:-----:|:----:|:-------:|:--------:|:----:|:-------:|:-------:|:------:|
| `StatusLine`      |  ✓   |         |       |  ✓   |         |    ✓     |      |    ✓    |         |   ✓    |
| `TimelineItem`    |  ✓   |         |       |  ✓   |    ✓    |          |      |         |         |   ✓    |
| `ProgressTimeline`|      |         |       |  ✓   |         |          |      |         |         |        |
| `HintBar`         |  ✓   |    ✓    |   ✓   |  ✓   |    ✓    |    ✓     |  ✓   |    ✓    |    ✓    |   ✓    |
| `Dialog`          |      |         |       |      |    ✓    |    ✓     |  ✓   |    ✓    |    ✓    |        |
| `Tabs`            |  ✓   |    ✓    |   ✓   |  ✓   |    ✓    |    ✓     |      |         |         |        |
| `Select`          |  ✓   |    ✓    |   ✓   |      |    ✓    |    ✓     |      |    ✓    |    ✓    |        |
| `Table`           |      |    ✓    |   ✓   |      |    ✓    |          |  ✓   |         |         |   ✓    |
| `KeyHint`         |  ✓   |    ✓    |   ✓   |  ✓   |    ✓    |    ✓     |  ✓   |    ✓    |    ✓    |   ✓    |

If the build order follows this matrix, `HintBar`, `Tabs`, `Select`, and
`TimelineItem` cover roughly 80% of the surface area and should be the
first widgets implemented in `Oahu.Cli.Tui`.
