# `oahu-cli serve` — MCP + loopback HTTP server

Phase 5 ships the long-lived integration surface for `oahu-cli`: a single
process that exposes the existing CLI services (auth, library, queue, jobs,
history, doctor, config) through two transports:

- **MCP-stdio** for AI hosts (Claude Desktop, Continue, etc.).
- **Loopback HTTP REST** (with SSE for job updates) for scripts and other
  local tooling.

Both transports share one tool surface, one capability policy, one audit
log, and one cooperative file lock under the user data directory.

## Quick start

```sh
# Stdio MCP (no port) — for Claude Desktop, etc.
oahu-cli serve --mcp --unattended

# Loopback HTTP — defaults to 127.0.0.1:8765
oahu-cli serve --http
oahu-cli serve --http --port 18765 --bind 127.0.0.1

# Both transports at once.
oahu-cli serve --mcp --http
```

The first time the HTTP server starts, it generates a 32-byte base64url
bearer token at `<config-dir>/server.token` (Unix mode 0600, Windows ACL
restricted to the current user).

```sh
# Inspect / rotate the token (server must be stopped to rotate).
oahu-cli serve token path
oahu-cli serve token show
oahu-cli serve token rotate
```

## REST recipe

```sh
TOKEN=$(oahu-cli serve token show)
H="Authorization: Bearer $TOKEN"
BASE=http://127.0.0.1:8765/v1

curl -H "$H" "$BASE/library?limit=5"
curl -H "$H" "$BASE/auth/status"
curl -H "$H" "$BASE/doctor"

# Submit a download and stream progress.
curl -H "$H" -H 'Content-Type: application/json' \
     -d '{"asins":["B0123ABCDE"],"quality":"high"}' \
     "$BASE/jobs"
curl -N -H "$H" "$BASE/jobs/stream"
```

The SSE stream emits `event: snapshot` for each currently-active job on
connect, then `event: update` for every subsequent `JobUpdate`. Late
clients always start from a consistent snapshot.

## Claude Desktop config

```json
{
  "mcpServers": {
    "oahu-cli": {
      "command": "oahu-cli",
      "args": ["serve", "--mcp", "--unattended"]
    }
  }
}
```

`--unattended` tells the server it is allowed to perform mutating tool
calls without an interactive confirmation prompt. Without it, the stdio
transport auto-denies any tool tagged `Mutating` or `Expensive`.

## Capability classes

Every tool is tagged with one of four capability classes. The capability
policy combines that class with the active transport and the
`--unattended` flag.

| Class       | Examples                                  | Stdio default | Stdio + `--unattended` | HTTP        |
|-------------|-------------------------------------------|---------------|------------------------|-------------|
| Safe        | `library_list`, `auth_status`, `doctor`   | allowed       | allowed                | allowed     |
| Mutating    | `queue_add`, `queue_remove`, `auth_logout`| denied        | allowed                | allowed     |
| Expensive   | `library_sync`, `download`                | denied        | allowed                | allowed     |
| Destructive | `queue_clear`                             | denied        | denied without confirm | needs confirm |

Destructive tools require an explicit `confirm: true` argument (or
`?confirm=true` query string on REST) regardless of transport.

## Authentication & networking

- **Token file** (HTTP only): generated automatically. `TokenStore` writes
  with `UnixCreateMode.UserRead | UserWrite` from creation (no
  chmod-after window) and a Windows ACL stripped to the current user.
- **Constant-time compare** for the bearer token check.
- **Loopback only**: `--bind` accepts `127.0.0.1`, `::1`, `localhost`, or
  another `IsLoopback` address. Any other value is rejected at startup.
- **No TLS**: loopback only, full stop.

## Audit log

Every tool invocation appends a JSON line to
`<shared-user-data>/logs/server-audit.jsonl`:

```json
{"ts":"…","transport":"http","actor":"http","tool":"library_list",
 "argsHash":"sha256:…","outcome":"ok","durationMs":12}
```

Arguments are hashed (canonical JSON → SHA-256) so secrets and titles
never leak into the audit trail. `outcome` is `ok` / `error` / `denied`.

## Cooperative file lock

`oahu-cli serve` acquires `<shared-user-data>/server.lock` and writes its
PID. A second `serve` invocation refuses to start with a friendly
"already running (PID 12345)" message.

> **Known gap**: the GUI does not yet take a matching lock. v1 prevents
> two CLI servers from running simultaneously, but a GUI editing the
> queue/library at the same time as a CLI server is still possible.

## Known gaps (v1)

- No MCP **Streamable HTTP** transport — stdio only.
- No **Unix socket** / **named pipe** transport.
- No **scoped tokens** — single bearer token has full surface.
- No `history_delete` tool — would need rewrite-then-rename of the jsonl
  history file.
- No `download` overrides for `--no-decrypt` / `m4b` / `all-new` (these
  are also missing from the underlying CLI command).

## Token rotation

```sh
# Stop the server first (Ctrl-C). Rotation refuses while the lock is held.
oahu-cli serve token rotate
```

Rotation deletes the existing token file and generates a fresh one with
the same restrictive permissions.
