# SERVER REGISTRY

## OVERVIEW
Node 20 + strict TypeScript registry/admin service for public server submissions, approvals, heartbeats, scheduled probes, and the public `/servers/` directory.

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| Public API, admin routes, probe scheduling | `src/server.ts` | Express entrypoint, env parsing, intervals, auth wiring |
| Submission review flow, sessions, persistence | `src/store.ts` | PostgreSQL schema + all state transitions live here |
| Probe logic and quality grading | `src/probe.ts` | health checks, RTT, bandwidth sampling, runtime state |
| Capacity resolution and create-room guards | `src/capacity.ts` | manual vs probe-derived capacity source |
| Password/session/token handling | `src/security.ts` | scrypt hashing, signed admin cookies, server tokens |
| Embedded admin console | `src/admin-ui.ts` | large inline HTML/React/Ant Design page |
| Deploy/env changes | `.env.example`, `README.md` | keep operational docs and defaults aligned |

## CONVENTIONS
- ESM only. Use `node:` built-in imports and keep `.js` suffixes in local imports.
- Keep route handlers thin; push persistence and transition rules into `RegistryStore` instead of spreading SQL and state checks across `server.ts`.
- Preserve structured API errors via `RegistryStoreError` or `InputError`; admin/public clients already expect `{ code, message }` responses.
- Tests stay adjacent to source as `*.test.ts` and run through `npm run test` after compilation with Node's built-in runner.
- `admin-ui.ts` is intentionally inline and CDN-backed; treat it as a self-contained console rather than starting a separate frontend stack.

## ANTI-PATTERNS
- Do not edit mirrored copies under `../releases/sts2_server_registry/`; package from source.
- Do not move database schema rules or submission state transitions out of `src/store.ts` unless the persistence model changes broadly.
- Do not bypass `verifyPassword`, `verifySession`, or token hashing helpers with ad hoc auth shortcuts.
- Do not add a separate SPA/build pipeline for the admin console unless the whole service architecture changes.

## COMMANDS
```bash
npm ci
npm run build
npm run check
npm run test
npm start
```

## NOTES
- Startup auto-creates required PostgreSQL tables and indexes through `RegistryStore.init()`.
- Default port is `18787`; required boot secrets are `ADMIN_PASSWORD_HASH`, `ADMIN_SESSION_SECRET`, and `SERVER_TOKEN_SECRET`.
- Probe cadence is split: light probes run every few minutes; bandwidth probes run less frequently and reuse the same `probe.ts` grading model.
