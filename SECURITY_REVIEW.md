# Security Review

> Updated 2026-07-24 for the remaining backend hardening gaps. Scope is `Household.Api` only. No `.env`, secret value, deployment, or live endpoint was accessed.

> 2026-08-01 follow-up: Seerr now has purpose-protected configuration, admin-approved per-user mappings, mandatory `X-API-User` isolation, permission-aware moderation, and focused tests. App catalog administration is metadata-only and app operations are read-only.

## Project Detection

- API: ASP.NET Core minimal API on .NET 9, EF Core/SQLite, JWT bearer auth, Data Protection, built-in rate limiting.
- Frontend: not in this repository/scope. Browser contracts are API DTOs only.
- Tests: xUnit in `Household.Api.Tests`.
- Deployment: Docker/CasaOS documentation and examples; the API uses a bridged configured CasaOS URL and does not mount the Docker socket.

## Executive Summary

The remaining high/medium backend gaps in scope are fixed: provider downloads now use bounded error taxonomy and exactly one 401 refresh; dedicated integration records cannot be changed by generic CRUD; safe correlation IDs propagate to provider clients; Games mutations reconcile only ambiguous outcomes; first-use timezone remains unset for browser selection; admin audits distinguish identity/role/activation; and Jellyfin/GitHub contracts have focused isolation/cache tests. DoIt provider/client files and frontend remained excluded.

## Changes Made

- Added safe bounded `X-Correlation-ID` middleware, response header, structured log scope, and outbound `HttpClient` propagation.
- Added exactly-one-refresh provider download behavior and safe timeout/transport/oversize handling without blind retries.
- Reserved CasaOS, Jellyfin, and GitHub Actions records from generic integration list/get/create/update/delete.
- Narrowed Games mutation reconciliation to timeout, transport, 5xx, malformed/empty success; 400/403/404 remain definitive.
- Made preference timezone nullable on first use with an EF-generated migration, allowing the browser to patch its IANA zone without a persisted UTC race.
- Distinguished admin identity, role grant/revoke, activation, and deactivation audit actions without secret/identity summaries.
- Added Jellyfin two-user mapping/image-grant tests and documented that deep links depend on an existing Jellyfin browser session.
- Added GitHub cache filtering, ETag, backoff, workflow-name/status, and timestamp-derived duration tests. PAT permission remains an operator-controlled GitHub setting, not enforceable by Household.
- Added persisted `User.RequiresPasswordChange` and EF-generated `20260724105945_AddRequiresPasswordChange`; the migration defaults existing users to false.
- Set the flag for all admin direct creation (generated or supplied temporary password) and admin reset; explicitly leave invitation-redeemed users false.
- Added `requiresPasswordChange` to login, `/auth/me`, access JWTs, and refreshed JWTs.
- Added fail-closed request middleware after authentication. It permits only `/auth/me`, `/auth/change-password`, `/auth/logout`, `/auth/logout-all`, and `/health` while the database-backed requirement is active; all normal app/admin/provider routes return `403 { "code": "password_change_required" }`.
- Refused refresh-token rotation while password change is required, so the body-authenticated `/auth/refresh` route cannot bypass the middleware allowlist.
- Successful self-service change clears the flag and retains existing session-version increment/refresh-token revocation behavior.
- Added service/JWT and real middleware tests for creation, reset, invitation redemption, login response/claim, route blocking, and self-change clearing/session invalidation.


## Findings

### Critical

- None open.

### High

- **Admin temporary passwords were not forced to change.** Risk: a disclosed or retained temporary credential had normal user/admin access indefinitely. Fix: persisted flag, issuance semantics, database-backed claim refresh during bearer validation, global route restriction, and session-invalidating self change. Status: **fixed**.

### Medium

- **Provider file downloads lacked refresh and transport taxonomy.** Risk: transient auth expiry or transport faults produced inconsistent failures, and careless retries could duplicate non-idempotent work. Fix: GET download retries only once after 401 token rotation; timeout/transport/oversize receive safe codes. Status: **fixed**.
- **Generic integration CRUD could create/delete Jellyfin or GitHub records.** Risk: duplicate names/types or data protected with the wrong purpose. Fix: all three dedicated types are hidden and rejected by generic CRUD. Status: **fixed**.
- **Games mutation failures were over-classified as ambiguous.** Risk: definitive 400/403/404 could trigger misleading canonical reconciliation. Fix: reconcile only timeout/transport/5xx/malformed-or-empty success; preserve one 401 refresh. Status: **fixed**.
- **First-use timezone defaulted to persisted UTC.** Risk: browser-local preference could be overwritten before initialization. Fix: nullable/unset response plus browser patch contract and migration. Status: **fixed**.
- **Correlation IDs were not validated or propagated.** Fix: 64-character safe alphabet, generated fallback, response/outbound propagation, and secret-free log scope. Status: **fixed**.

### Low


## Environment Variable Classification

| Variables | Classification | Frontend-safe | Action |
| --- | --- | --- | --- |
| `JWT_SECRET_KEY`, `SEED_ADMIN_PASSWORD` | backend secrets | No | Existing deployment secrets. |
| `VITE_*` values | public browser configuration | Only non-secrets | Never place CasaOS JWT/internal URL in them. |

No environment variables were added or read for the forced-change flow.

## Test Matrix

| Check | Status | Evidence |
| --- | --- | --- |
| Hosted admin vs normal-user route | existing boundary/code-reviewed | Every route checks `IsAdmin`; no new hosted test framework was added. |
| Admin-created user requires change | added | `UserAdministrationServiceTests.DirectCreation_RequiresChangingTheTemporaryPassword` |
| Admin reset requires change and revokes sessions | added/extended | `UserAdministrationServiceTests.PasswordReset_RevokesRefreshSessionsAndAdvancesSessionVersion` |
| Invitation password is not temporary | added/extended | `UserAdministrationServiceTests.Invitation_IsHashedSingleUseAndCreatesOnlyOneUser` |
| Login response and access claim | added | `AuthServiceTests.Login_ReportsAndClaimsRequiredPasswordChange` |
| Refresh-token route refused | added | `AuthServiceTests.Refresh_RequiredPasswordChange_DoesNotIssueOrRotateTokens` |
| Normal/admin/provider/app routes blocked | added | `PasswordChangeRequiredMiddlewareTests.RequiredPasswordChange_BlocksNormalEndpointsWithSafeCode` |
| Recovery routes remain reachable | added | `PasswordChangeRequiredMiddlewareTests.RequiredPasswordChange_AllowsOnlyRecoveryEndpoints` |
| Self change clears requirement and invalidates sessions | added/extended | `AuthServiceTests.ChangePassword_Success_ChangesHashRevokesSessionsAndAdvancesVersion` |
| Provider download 401/timeout/transport/oversize | added | `ProviderDownloadClientTests` |
| Dedicated integration records excluded from generic CRUD | added | `IntegrationServiceTests` |
| Correlation sanitization/response/outbound propagation | added | `CorrelationIdMiddlewareTests` |
| Games 400/403/404 vs timeout/transport/5xx/empty/malformed | added | `GamesDatabaseClientTests` |
| First-use timezone unset/browser patch | added | `UserSettingsServiceTests.FirstRead_LeavesTimezoneUnsetUntilBrowserPatchesIt` |
| Distinct admin identity/role/activation audit | added | `UserAdministrationServiceTests.Update_AuditsIdentityRoleAndActivationAsDistinctActions` |
| Jellyfin user mapping and image grants isolated | added | `JellyfinServiceTests` |
| GitHub user filtering, ETag, backoff, duration/name/status | added | `GitHubActionsMonitorTests` |

## Auth And Authorization Matrix

- Unauthenticated: rejected by group `RequireAuthorization`.
- Authenticated non-admin: explicit backend `IsAdmin` check returns forbidden for admin configuration.
- Admin: may configure Seerr metadata and manage approved user mappings.
- User A vs User B: Seerr requests always use the authenticated user's resolved provider identity.
- Invalid/expired token: existing JWT lifetime/session-version validation applies.
- Authenticated user requiring password change: only `/auth/me`, `/auth/change-password`, `/auth/logout`, `/auth/logout-all`, and `/health` continue; app, admin, and provider routes return safe 403 regardless of role.

## Password Reset Review

Admin direct creation and reset now always produce forced-change credentials. Invitation redemption is distinct because the user selects the password and therefore persists false. Self change verifies the current password, applies the password policy, clears the flag, increments `SessionVersion`, revokes all refresh tokens, and requires reauthentication. Existing users receive migration default false.

## Rate Limiting Review

- CasaOS config/history: existing admin policy, 30/minute/admin.

## CORS, Cookies, CSRF, Headers

Existing bearer-token CORS/security-header behavior is unchanged. No cookie/CSRF surface was added.
Safe correlation IDs use only letters, digits, `.`, `_`, and `-`, are capped at 64 characters, and are returned as `X-Correlation-ID`; unsafe/multiple inbound values are replaced. The ID, but no token or secret, is propagated to typed/named outbound HTTP clients.

## Scripts

- `security:secrets`, `security:deps`, `security:sast`, `security:react`, `security:zap`, `security:all`: skipped; no repo-native script runner exists and adding heavy/network tooling was not needed for this focused API implementation.

## GitHub Actions And Free Alerts

Existing workflow was not changed. Repository visibility/cost and GitHub security settings were not needed for this change and were not verified.

## ZAP And Dynamic Scanning

Skipped: no owned local/staging target was supplied, and active/live mutation was prohibited.

## React Doctor

Not applicable; this repository/scope is API-only.

## Manual Checks Still Required

- Frontend must consume `requiresPasswordChange`/`password_change_required`, route the user to password change, and clear local access/refresh credentials after the successful response; server enforcement does not depend on this UI behavior.
- Verify `/data/keys` and the SQLite database are persistent and private to the API container identity.
- Confirm the GitHub PAT is fine-grained/read-only and repository-limited in GitHub. Household can protect/use the token but cannot enforce PAT permissions.
- Confirm Jellyfin browser deep links in the deployed public URL; unauthenticated browser sessions may land on Jellyfin sign-in by design.
- Confirm single-replica deployment or add a distributed lock before scaling the API horizontally.

## Commands To Run

- `dotnet build Household.Api.sln -c Release` — passed, 0 warnings/errors.
- `dotnet test Household.Api.sln -c Release --no-build` — passed, 133/133.
- `dotnet ef migrations add AddRequiresPasswordChange --project Household.Api.csproj --startup-project Household.Api.csproj --context AppDbContext --configuration Release --no-build` — passed; generated migration `20260724105945_AddRequiresPasswordChange` with false default.
- `dotnet ef migrations list --configuration Release --project Household.Api.csproj --startup-project Household.Api.csproj --context AppDbContext --no-connect --no-build` — passed; seven migrations listed, including `20260724105945_AddRequiresPasswordChange` (applied status intentionally not queried).
- `dotnet ef migrations has-pending-model-changes --configuration Release --project Household.Api.csproj --startup-project Household.Api.csproj --context AppDbContext --no-build` — passed; no pending model changes.
- `dotnet ef migrations add MakeUserPreferenceTimeZoneOptional ...` — passed; generated `20260724120228_MakeUserPreferenceTimeZoneOptional`.

## Continuation Notes For AI Agents

Frontend work should treat nullable `timeZoneId` as first use and patch the browser IANA timezone, but frontend was explicitly excluded here. Preserve dedicated integration ownership, one-refresh-only auth handling, Games definitive-vs-ambiguous classification, per-user Jellyfin grants, and correlation-ID validation. DoIt client/provider files were intentionally untouched.

## 2026-08-01 Focused Uncommitted Integration Review

### Scope And Executive Summary

This follow-up reviewed the Seerr integration, database catalog, catalog editor, and read-only app monitoring together with `Household.Front`. No Critical, High, or Medium findings remain open in this scope.

### Changes Made

- Added a canonical resolved Seerr identity with a unique partial index, duplicate checks, race-safe persistence, and lazy backfill for existing mappings. Jellyfin and numeric override mappings can no longer resolve two Household users to the same Seerr account.
- Serialized Seerr configuration writes, reload configuration inside the lock, require the API key again for any internal URL change, and use a database concurrency token so stale writes fail atomically across contexts or application instances.
- Jellyfin preference edits now preserve the canonical reservation whenever a numeric Seerr override remains active.
- Restricted Seerr artwork to fixed TMDB proxy paths, bounded season input and response-body reads, and corrected delete authorization/error mapping.
- Reworked catalog bootstrap to merge mounted metadata while retaining trusted operational targets, remove case-insensitive duplicates, and replace known policies with canonical values before requests are served.
- Single-app reads no longer refresh the complete catalog, category reads no longer perform operational probes, and Docker and health calls share a process-wide concurrency bound.

### Findings

#### Critical

- None.

#### High

- **Fixed — concurrent Seerr reconfiguration could cross credential/server boundaries.** Configuration writes now reload under a process-wide lock, every internal URL change requires the API key again, and `Integration.ConfigurationVersion` provides optimistic concurrency across database contexts and application instances. A stale integration/secret write rolls back and returns `seerr_config_conflict`; the version is also part of every Jellyfin mapping-cache key, so another instance cannot reuse user IDs from the previous Seerr server.

#### Medium

- **Fixed — Seerr mappings were not one-to-one.** `SeerrResolvedUserId` now stores the canonical upstream identity and has a unique partial index. Admin updates reject duplicates across mapping sources, concurrent writes fail closed, and existing approved mappings are backfilled on resolution.
- **Fixed — Jellyfin preference edits could release an active numeric override reservation.** Clearing or changing Jellyfin metadata now retains `SeerrResolvedUserId` whenever `SeerrUserIdOverride` remains active.
- **Fixed — Seerr could cause browser requests to arbitrary image hosts.** Artwork now accepts only path-safe TMDB images or already-proxied images on the configured Seerr public authority; all other absolute URLs are dropped.
- **Fixed — catalog reads could create unbounded operational fan-out.** Single-item and favorite reads fetch only one item, category reads query metadata only, health requests are process-wide concurrency-bounded, and Docker inspection is bounded inside `ContainerStatusService` so direct status routes cannot bypass the limit.
- **Fixed — legacy catalog rows could become unsafe monitoring inputs.** Bootstrap groups IDs case-insensitively, disables duplicate launcher rows, removes duplicate policies, and overwrites every known operational policy with canonical project, container, capability, and health values.
- **Fixed correctness — mounted overrides for built-in apps were skipped.** Mounted name, category, description, icon, and favorite values are merged into canonical rows while canonical URLs and all operational policy fields remain trusted.
- **Fixed — Seerr TV season input was unbounded.** Requests now accept at most 100 distinct seasons in the range 0 through 200.
- **Fixed — Seerr response bodies lacked an independent deadline.** Buffered body reads and JSON parsing use a linked, bounded content-phase cancellation token.

#### Low

- **Fixed correctness — delete availability/error mapping was broader than the Seerr contract.** Non-managers now see delete only for their own pending requests, while managers retain moderation controls; upstream `401` and `403` responses map to a safe forbidden result.

### Verified Controls

- Every user-scoped Seerr discovery, detail, request-list, create, moderate, and delete call resolves the authenticated Household user's approved mapping and sends `X-API-User`; admin bootstrap/config/mapping-resolution calls are the only API-key-owner calls. `SeerrService.cs:293-535,537-656`.
- Seerr configuration and mapping endpoints require backend authentication and explicit `IsAdmin`; a normal user's self-edited Jellyfin ID clears approval. `Endpoints/SeerrEndpoints.cs:11-51`; `Application/Services/UserSettingsService.cs:74-85`.
- Seerr and CasaOS use no-redirect handlers. Server authority changes require fresh secrets, responses never return API keys/tokens, and logs reviewed here use only safe event/error-type metadata. `Program.cs:442-447`; `SeerrService.cs:138-174`; `CasaOsUpdateService.cs:117-171`.
- CasaOS uses the documented bodyless `PATCH /v2/app_management/compose/{projectName}?force=true`, a fixed exact project map, exact confirmations, no bulk endpoint, and disabled automated rollback. `Application/Interfaces/ICasaOsUpdateService.cs:38-89`; `CasaOsUpdateService.cs:309-390,486-503`.
- Backup IDs, root confinement, unpredictable names, reparse-point checks, size bounds, and owner-only Unix creation remain in place. `CasaOsUpdateService.cs:961-1202`.
- Catalog mutation uses an explicit metadata-only DTO; app IDs, health targets, container names, project names, and operation permissions are not mass-assignable. `DTOs/AppLauncherDTOs.cs:32-56`; `Application/Services/AppCatalogService.cs:143-165`.

### Environment Variable Classification

| Variables | Classification | Frontend-safe | Action |
| --- | --- | --- | --- |
| `SEERR_API_KEY` | backend secret | **No** | Keep only in API/hosting configuration; it is purpose-protected when bootstrapped. |
| `SEERR_BASE_URL` | backend internal configuration | No browser need | Keep server-side; fixed-path requests only. |
| `SEERR_OPEN_URL` | browser-visible public URL | Yes, if intentionally public | Validate deployment value; never append credentials or tokens. |
| `SEERR_TIMEOUT_SECONDS` | backend configuration | No browser need | Keep server-side. |
| `CASAOS_*` token values | backend secrets | **No** | Continue using the write-only admin endpoint; never place in frontend variables. |

### Auth, Rate Limiting, And Response Matrix

- Unauthenticated Seerr/catalog/CasaOS requests: backend group authorization rejects them.
- Normal user vs admin: catalog and Seerr configuration/mapping writes return forbidden; CasaOS operations are admin-only.
- User A vs User B: user-scoped Seerr requests carry the mapped Seerr ID in `X-API-User`; canonical resolved IDs and source-specific identifiers are unique in SQLite.
- Seerr reads/mutations: 90/minute and 20/minute per Household user. Admin Seerr config/mapping writes use the admin policy. Catalog reads use 12/minute per user and operational calls are globally concurrency-bounded.
- `401`, `403`, `404`, `429`, and `5xx` responses are converted to safe frontend messages, including delete-specific authorization failures.

### Tests And Commands

- API build: **passed**, zero warnings and zero errors.
- API tests: **passed**, 158 tests.
- EF model consistency: **passed**, no pending model changes.
- Fresh SQLite migration chain through `20260801193920_HardenSeerrIsolation`: **passed**.
- Live API startup and anonymous `/health`: **passed**, `status=healthy`, `db=ok`.
- Frontend lint: **passed**; Vitest: **passed**, 15 files / 29 tests; production build: **passed**.
- The backend verification used a temporary worktree under `C:\Users\Rikku\AppData\Local\Temp\opencode` because MSBuild hangs under the deep `K:` workspace path.

### Scripts, CI, ZAP, And Follow-up

- Security scripts, dependencies, and GitHub workflows were not changed; this review was constrained to concrete regressions in current work. Existing CI already restores, builds, and tests on pull requests.
- ZAP/full dynamic scanning and live CasaOS/Seerr mutations were skipped because they would mutate infrastructure. No containers or live configuration were changed during this review.
- Before deployment, back up the production SQLite database and execute one controlled CasaOS update while confirming that no redirect occurs and only the expected project restarts.
- Password reset, cookies/CSRF, uploads, and unrelated API modules were not re-reviewed; no change in this scope affected them.
