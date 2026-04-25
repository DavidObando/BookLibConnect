# `oahu-cli` JSON schemas

Schemas describing every JSON document produced by `oahu-cli --json`. Snapshot
tests in `tests/Oahu.Cli.Tests/Commands/` assert that the live commands emit
documents that conform to the shape declared here.

| Command | Schema |
|---------|--------|
| `oahu-cli config get [<key>] --json` | [`config.schema.json`](./config.schema.json) |
| `oahu-cli auth status --json` | [`auth-status.schema.json`](./auth-status.schema.json) |
| `oahu-cli library list --json` | [`library-list.schema.json`](./library-list.schema.json) |
| `oahu-cli library show <asin> --json` | [`library-show.schema.json`](./library-show.schema.json) |
| `oahu-cli queue list --json` | [`queue-list.schema.json`](./queue-list.schema.json) |
| `oahu-cli history list --json` | [`history-list.schema.json`](./history-list.schema.json) |
| `oahu-cli history show <id> --json` | [`history-show.schema.json`](./history-show.schema.json) |
| `oahu-cli download <asin>... --json` (per-update) | [`download-update.schema.json`](./download-update.schema.json) |
| `oahu-cli download <asin>... --json` (final summary) | [`download-summary.schema.json`](./download-summary.schema.json) |
| `oahu-cli convert <asin>... --json` (per-update) | reuses [`download-update.schema.json`](./download-update.schema.json) |
| `oahu-cli convert <asin>... --json` (final summary) | reuses [`download-summary.schema.json`](./download-summary.schema.json) |
| `oahu-cli doctor --json` | [`doctor.schema.json`](./doctor.schema.json) |

Every document includes a top-level `_schemaVersion` string. The current
version is `"1"`. Bumping the version is a **breaking change**; additive
fields are not.

Schemas for the deferred `history retry` and `convert` commands land
alongside their implementations in phase 4c.2.
