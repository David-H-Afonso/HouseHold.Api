# CasaOS App Updates

Household queues one allowlisted app update at a time through CasaOS's official app-management contract:

- `GET /v2/app_management/compose/{projectName}` with `Accept: application/yaml` creates a private recovery backup.
- `PATCH /v2/app_management/compose/{projectName}?force=true` queues the update with no request body.
- `Authorization: <raw CasaOS JWT>` is sent server-side with no `Bearer` prefix.

Catalog IDs and CasaOS project names are mapped explicitly. For example, catalog app `seerr` updates project `big-bear-seerr`. Immich and CasaOS are intentionally excluded from updates. There is no bulk update endpoint.

## Setup

1. Persist `CASAOS_COMPOSE_BACKUP_ROOT` (default `/data/compose-backups`) with the database and Data Protection keys.
2. In Household Settings -> Apps & providers, save the bridged/LAN CasaOS URL and the access/refresh token pair from the same CasaOS session.
3. From Apps, queue an individual update. Household requires exact confirmation `UPDATE <catalogAppId>`.

The token pair and Compose backups remain server-side. Redirects, oversized responses, unsafe project IDs, and targets outside the fixed allowlist are rejected.

## Completion And Rollback

CasaOS applies updates asynchronously. Household status `Queued` means only that CasaOS accepted the request; history is an acceptance/audit trail, not live progress.

Compose backups are retained for manual disaster recovery, but `rollbackAvailable` is currently always `false`. The automated rollback endpoint returns `rollback_not_safe` because restoring YAML cannot prove restoration of mutable image tags, volumes, application data, registry images, or external dependencies. A backup ID alone never enables rollback.

Launcher responses expose explicit `monitoringEnabled`, `canUpdate`, and `canRollback` capabilities. Update availability remains nullable when CasaOS returns an unavailable, redirected, malformed, or oversized response.
