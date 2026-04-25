# `oahu-cli` JSON schemas

Schemas describing every JSON document produced by `oahu-cli --json`. Snapshot
tests in `tests/Oahu.Cli.Tests/Commands/` assert that the live commands emit
documents that conform to the shape declared here.

| Command | Schema |
|---------|--------|
| `oahu-cli config get [<key>] --json` | [`config.schema.json`](./config.schema.json) |
| `oahu-cli queue list --json` | [`queue-list.schema.json`](./queue-list.schema.json) |
| `oahu-cli history list --json` | [`history-list.schema.json`](./history-list.schema.json) |
| `oahu-cli history show <id> --json` | [`history-show.schema.json`](./history-show.schema.json) |
| `oahu-cli doctor --json` | [`doctor.schema.json`](./doctor.schema.json) |

Every document includes a top-level `_schemaVersion` string. The current
version is `"1"`. Bumping the version is a **breaking change**; additive
fields are not.

Schemas for the deferred `auth status`, `library list`, `library show`, and
`history retry` commands land alongside their implementations in phases 4b
and 4c.
