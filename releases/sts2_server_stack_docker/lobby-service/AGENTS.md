# LOBBY SERVICE

## OVERVIEW
Node 20 + strict TypeScript lobby server for room directory, join tickets, control-channel WebSocket traffic, and UDP relay fallback.

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| New route or request validation | `src/server.ts` | all HTTP endpoints and WS `/control` live here |
| Room rules, ticket TTL, saved-run logic | `src/store.ts` | most business logic is centralized here |
| Relay allocation and packet forwarding | `src/relay.ts` | custom UDP relay implementation |
| Relay-only readiness checks | `src/join-guard.ts` | small guards; do not inline into routes |
| Expired-room cleanup flow | `src/room-cleanup.ts` | orchestration wrapper over `LobbyStore` + relay cleanup |
| Deployment/env changes | `.env.example`, `deploy/sts2-lobby.service.example`, `README.md` | keep docs and defaults aligned |

## CONVENTIONS
- ESM only. Use `node:` built-in imports and keep `.js` suffixes in local imports.
- Keep request parsing explicit with helpers like `requiredString`, `optionalString`, and `positiveInt`.
- Return structured API errors via `LobbyStoreError` or `InputError`; preserve `{ code, message, details }` responses.
- Keep tests adjacent to source as `*.test.ts`; current suite uses `node:test` and `assert/strict`, not Jest/Vitest.
- `store.ts` is the state authority. Prefer small route handlers that delegate there instead of spreading room rules across files.

## ANTI-PATTERNS
- Do not use `require()` or CommonJS syntax.
- Do not add external test frameworks unless the whole service changes direction.
- Do not hardcode relay ports, hostnames, or strategy defaults outside env parsing.
- Do not skip `assertRelayCreateReady` / `assertRelayJoinReady` for `relay-only` mode.
- Do not edit mirrored copies under `../releases/sts2_lobby_service/`; package from source.

## COMMANDS
```bash
npm ci
npm run build
npm run check
npm run test
npm start
```

## NOTES
- Environment defaults live in `.env.example`; README explains operational meaning.
- Open ports are `8787/TCP` plus `39000-39149/UDP` by default, unless env overrides narrow them further.
- Log prefixes matter: `[http]`, `[lobby]`, `[relay]`, and control-channel errors help trace failures quickly.
