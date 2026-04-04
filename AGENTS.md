# PROJECT KNOWLEDGE BASE

**Commit:** `7fd6316`
**Branch:** `main`

## OVERVIEW
`STS2 LAN Connect` is a multi-runtime public repo containing the lobby server (`lobby-service/`), the Godot/.NET client mod (`sts2-lan-connect/`), and the public registry service (`server-registry/`). A separate work copy of the registry exists outside this tree, but within this subtree, `server-registry/` is the local registry source. The `releases/` directory only mirrors packaged output.

## PRECEDENCE
- Nearest `AGENTS.md` wins.
- Use this file for cross-repo routing and shared gotchas.
- Use child files for stack-specific rules in `lobby-service/`, `sts2-lan-connect/`, `sts2-lan-connect/Scripts/Lobby/`, and `server-registry/`.

## STRUCTURE
```text
STS2-Game-Lobby/
|- lobby-service/               # Node 20 + TypeScript lobby server
|- server-registry/             # Node 20 + TypeScript public registry service
|- deploy/                      # public deployment assets and stack-level env templates
|- sts2-lan-connect/            # Godot 4.5 + .NET 9 client mod
|- scripts/                     # build, package, install, sync automation
|- docs/                        # user/deployment docs in Chinese
|- research/                    # reconstructed notes and reverse-engineering context
`- releases/                    # mirrored package output; do not edit directly
```

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| Lobby API, tickets, room lifecycle | `lobby-service/src/server.ts` | HTTP, WebSocket, relay wiring |
| Room rules and compatibility checks | `lobby-service/src/store.ts` | single source of truth for room state |
| Relay fallback behavior | `lobby-service/src/relay.ts` | UDP relay manager and sessions |
| Registry API, admin routes, probe scheduling | `server-registry/src/server.ts` | public `/servers/`, admin flows, probe intervals |
| Registry persistence and review state | `server-registry/src/store.ts` | PostgreSQL schema and submission state transitions |
| Public deploy and packaging context | `deploy/`, `scripts/`, `server-registry/README.md` | use this tree for Docker/systemd/package context; use the outer work-copy tree for active registry-only source edits |
| Client startup and install path | `sts2-lan-connect/Scripts/Entry.cs` | mod entrypoint installs runtime and monitor |
| Client config and persisted overrides | `sts2-lan-connect/Scripts/LanConnectConfig.cs` | lock-based config store |
| Lobby UI and join/create flows | `sts2-lan-connect/Scripts/Lobby/` | dense hotspot; read child AGENTS first |
| Packaging and release sync | `scripts/` | source of truth for release contents |

## CONVENTIONS
- TypeScript services are strict ESM: keep `node:` imports for built-ins and include `.js` extensions in local imports.
- Service tests live beside source as `*.test.ts` and run through Node's built-in test runner after compilation.
- C# mod code keeps the `LanConnect*` naming family and uses `Sts2LanConnect.Scripts` namespace.
- Godot/UI hooks prefer deferred installation (`Callable.From(...).CallDeferred()`) and `TaskHelper.RunSafely(...)` for fire-and-forget async work.
- **Cross-runtime DTOs:** Lobby DTOs are manually mirrored between `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs` and `lobby-service/src/store.ts` / `server.ts`. Keep them synchronized.
- User-facing docs and many runtime messages are Chinese; preserve existing language unless the surrounding file uses English.

## ANTI-PATTERNS
- Do not edit `releases/` copies directly; update source and rerun the package/sync scripts.
- Do not treat relay as the primary game protocol; it is a room-level fallback after direct connection fails.
- Do not add separate guidance files for `docs/`, `research/`, or `releases/` unless those areas become active editing domains.
- Do not bypass the existing validation/error-code flow in the service just to simplify handlers.

## COMMANDS
```bash
# lobby service
cd lobby-service && npm ci && npm run check && npm run test

# registry service
cd server-registry && npm ci && npm run check && npm run test

# client mod
./scripts/build-sts2-lan-connect.sh
./scripts/package-sts2-lan-connect.sh

# service package / deploy helpers
./scripts/package-lobby-service.sh
./scripts/package-server-registry.sh
./scripts/sync-release-repo.sh --repo-dir <public-repo-clone>
```
