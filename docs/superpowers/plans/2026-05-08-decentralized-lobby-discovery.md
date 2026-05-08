# 去中心化 Lobby 服务发现 — 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 D-Day（2026-05-18）之前替换中心化 server-registry，让 10+ 第三方服主通过 gossip 互发现，CF Workers 充当只读聚合层，客户端启动改为弹窗选服并支持三源 bootstrap + 本地缓存。

**Architecture:** CF Workers + KV 提供只读 `/v1/seeds`/`/v1/servers`/`/v1/announcements` 与 10 分钟 cron 聚合；lobby-service v0.3 新增 `/peers/*` 四端点 + ed25519 身份 + JSON 持久化 + gossip 调度；sidecar 是 v0.3 peer 模块的独立打包；客户端 v0.3 用 Godot 弹窗 + 三源并联 + 本地 JSON 缓存 + HTTP HEAD ping。

**Tech Stack:** Node.js 20 + TypeScript（lobby-service / sidecar / CF Worker）；Wrangler（CF 部署）；C# 12 / .NET 9 / Godot 4.5（客户端）；Node 内建 `node:test`（TS 测试）；ed25519（`node:crypto` `generateKeyPair`）。

**参考 spec：** [docs/superpowers/specs/2026-05-08-decentralized-lobby-discovery-design.md](../specs/2026-05-08-decentralized-lobby-discovery-design.md)

**章节映射：**
- 第 P0 章：CF Workers + KV（spec §5.4，对应日历 D-10 → D-9）
- 第 P1 章：lobby-service v0.3 协议核心（spec §5.2 + §5.5，日历 D-9 → D-6）
- 第 P2 章：Sidecar 抽取（spec §5.3，日历 D-6 → D-4）
- 第 P3 章：客户端 v0.3（spec §5.1，日历 D-9 → D-5）

**通用约定：**
- TS 文件用 ESM；本地 import 必须带 `.js` 扩展（编译目标）；`node:` 前缀引内建模块
- 测试文件 `*.test.ts` 与源码同目录；`npm test` 会先编译再用 `node --test dist/**/*.test.js` 跑
- 每个任务结尾必须 commit；commit message 用中英结合即可（参考现有 git log 风格）
- DTO 跨运行时同步遵循 `AGENTS.md` 规则：TS 改 `lobby-service/src/store.ts` 或 `server.ts` 时，对应 C# 文件 `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs` 必须同步

---

## 第 P0 章 · CF Workers + KV

### Task P0.1：仓库内固化初始 seeds 快照

**Files:**
- Create: `data/seeds.json`

- [ ] **Step 1：写文件**

```json
{
  "version": 1,
  "updated_at": "2026-05-08T00:00:00Z",
  "seeds": [
    { "address": "http://lt.syx2023.icu:52000",  "note": "" },
    { "address": "http://223.167.230.132:52000", "note": "" },
    { "address": "https://120.55.3.230",         "note": "" },
    { "address": "http://112.74.164.10",         "note": "" },
    { "address": "http://106.75.7.210:8787",     "note": "" },
    { "address": "http://110.42.9.59:37000",     "note": "" }
  ]
}
```

- [ ] **Step 2：commit**

```bash
git add data/seeds.json
git commit -m "feat(p0): bootstrap seeds snapshot for cf worker and client packaging"
```

---

### Task P0.2：CF Worker 项目脚手架

**Files:**
- Create: `cf-worker/package.json`
- Create: `cf-worker/wrangler.toml`
- Create: `cf-worker/tsconfig.json`
- Create: `cf-worker/src/index.ts`（占位）
- Create: `cf-worker/.gitignore`

- [ ] **Step 1：建立 npm 工程**

`cf-worker/package.json`：

```json
{
  "name": "sts2-cf-worker",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "scripts": {
    "build": "tsc -p tsconfig.json --noEmit",
    "test": "tsc -p tsconfig.json --noEmit && node --test dist/**/*.test.js",
    "dev": "wrangler dev",
    "deploy": "wrangler deploy"
  },
  "devDependencies": {
    "@cloudflare/workers-types": "^4.20251001.0",
    "typescript": "^5.8.3",
    "wrangler": "^3.95.0"
  }
}
```

`cf-worker/tsconfig.json`：

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ES2022",
    "moduleResolution": "bundler",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "lib": ["ES2022", "WebWorker"],
    "types": ["@cloudflare/workers-types"],
    "outDir": "dist",
    "rootDir": "src"
  },
  "include": ["src/**/*"]
}
```

`cf-worker/wrangler.toml`（占位域名 `<your-domain>` 部署时替换）：

```toml
name = "sts2-discovery"
main = "src/index.ts"
compatibility_date = "2026-04-01"

[[kv_namespaces]]
binding = "DISCOVERY_KV"
id = "REPLACE_WITH_PRODUCTION_ID"

[triggers]
crons = ["*/10 * * * *"]
```

`cf-worker/src/index.ts`：

```typescript
export interface Env {
  DISCOVERY_KV: KVNamespace;
}

export default {
  async fetch(_request: Request, _env: Env): Promise<Response> {
    return new Response("sts2-discovery worker placeholder", { status: 200 });
  },
  async scheduled(_event: ScheduledEvent, _env: Env, _ctx: ExecutionContext): Promise<void> {
    // cron placeholder
  },
};
```

`cf-worker/.gitignore`：

```
node_modules/
dist/
.wrangler/
```

- [ ] **Step 2：安装依赖**

```bash
cd cf-worker && npm install
```

Expected: 无错误，生成 package-lock.json。

- [ ] **Step 3：commit**

```bash
git add cf-worker/
git commit -m "feat(p0): scaffold cf worker project with wrangler + ts"
```

---

### Task P0.3：定义 KV schema 与共享类型

**Files:**
- Create: `cf-worker/src/types.ts`

- [ ] **Step 1：写共享类型**

```typescript
// cf-worker/src/types.ts
export interface PeerEntry {
  address: string;
  publicKey?: string;
  displayName?: string;
  lastSeen: string;
  note?: string;
}

export interface SeedsDocument {
  version: 1;
  updated_at: string;
  seeds: Array<{ address: string; note?: string }>;
}

export interface ActiveServersDocument {
  version: 1;
  updated_at: string;
  servers: PeerEntry[];
}

export interface AnnouncementsDocument {
  version: 1;
  updated_at: string;
  items: Array<{
    id: string;
    title: string;
    body: string;
    publishedAt: string;
  }>;
}

export const KV_KEY_SEEDS = "peers:seeds";
export const KV_KEY_ACTIVE = "peers:active";
export const KV_KEY_ANNOUNCEMENTS = "announcements";
```

- [ ] **Step 2：commit**

```bash
git add cf-worker/src/types.ts
git commit -m "feat(p0): define cf worker kv schema types"
```

---

### Task P0.4：实现 GET /v1/seeds（含测试）

**Files:**
- Create: `cf-worker/src/handlers/seeds.ts`
- Create: `cf-worker/src/handlers/seeds.test.ts`
- Modify: `cf-worker/src/index.ts`

- [ ] **Step 1：写失败测试**

```typescript
// cf-worker/src/handlers/seeds.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import { handleGetSeeds } from "./seeds.js";

class FakeKV {
  private store = new Map<string, string>();
  async get(key: string): Promise<string | null> {
    return this.store.get(key) ?? null;
  }
  set(key: string, value: string): void {
    this.store.set(key, value);
  }
}

test("GET /v1/seeds returns 200 with seeds payload", async () => {
  const kv = new FakeKV();
  kv.set("peers:seeds", JSON.stringify({
    version: 1,
    updated_at: "2026-05-08T00:00:00Z",
    seeds: [{ address: "https://a.example", note: "" }],
  }));
  const env = { DISCOVERY_KV: kv as unknown as KVNamespace };
  const res = await handleGetSeeds(env);
  assert.equal(res.status, 200);
  const body = await res.json() as { seeds: unknown[] };
  assert.equal(body.seeds.length, 1);
});

test("GET /v1/seeds returns 200 with empty seeds when KV missing", async () => {
  const kv = new FakeKV();
  const env = { DISCOVERY_KV: kv as unknown as KVNamespace };
  const res = await handleGetSeeds(env);
  assert.equal(res.status, 200);
  const body = await res.json() as { seeds: unknown[] };
  assert.equal(body.seeds.length, 0);
});
```

- [ ] **Step 2：跑测试，确认失败**

```bash
cd cf-worker && npm test
```

Expected: FAIL — `Cannot find module './seeds.js'`。

- [ ] **Step 3：实现 handler**

```typescript
// cf-worker/src/handlers/seeds.ts
import type { Env } from "../index.js";
import { KV_KEY_SEEDS, type SeedsDocument } from "../types.js";

const FALLBACK: SeedsDocument = {
  version: 1,
  updated_at: new Date(0).toISOString(),
  seeds: [],
};

export async function handleGetSeeds(env: Env): Promise<Response> {
  const raw = await env.DISCOVERY_KV.get(KV_KEY_SEEDS);
  const body: SeedsDocument = raw ? (JSON.parse(raw) as SeedsDocument) : FALLBACK;
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "public, max-age=3600",
    },
  });
}
```

- [ ] **Step 4：在 index.ts 路由进来**

修改 `cf-worker/src/index.ts`，替换 fetch handler：

```typescript
import { handleGetSeeds } from "./handlers/seeds.js";

export interface Env {
  DISCOVERY_KV: KVNamespace;
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (request.method === "GET" && url.pathname === "/v1/seeds") {
      return handleGetSeeds(env);
    }
    return new Response("not found", { status: 404 });
  },
  async scheduled(_event: ScheduledEvent, _env: Env, _ctx: ExecutionContext): Promise<void> {},
};
```

- [ ] **Step 5：跑测试，确认通过**

```bash
cd cf-worker && npm test
```

Expected: PASS — 2 tests passing。

- [ ] **Step 6：commit**

```bash
git add cf-worker/src/
git commit -m "feat(p0): implement GET /v1/seeds with kv read"
```

---

### Task P0.5：实现 GET /v1/servers（含测试）

**Files:**
- Create: `cf-worker/src/handlers/servers.ts`
- Create: `cf-worker/src/handlers/servers.test.ts`
- Modify: `cf-worker/src/index.ts`

- [ ] **Step 1：写失败测试**

```typescript
// cf-worker/src/handlers/servers.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import { handleGetServers } from "./servers.js";

class FakeKV {
  private store = new Map<string, string>();
  async get(key: string): Promise<string | null> {
    return this.store.get(key) ?? null;
  }
  set(key: string, value: string): void {
    this.store.set(key, value);
  }
}

test("GET /v1/servers returns active list from kv", async () => {
  const kv = new FakeKV();
  kv.set("peers:active", JSON.stringify({
    version: 1,
    updated_at: "2026-05-08T00:00:00Z",
    servers: [{ address: "https://a.example", lastSeen: "2026-05-08T00:00:00Z" }],
  }));
  const env = { DISCOVERY_KV: kv as unknown as KVNamespace };
  const res = await handleGetServers(env);
  assert.equal(res.status, 200);
  const body = await res.json() as { servers: unknown[] };
  assert.equal(body.servers.length, 1);
});

test("GET /v1/servers returns empty list if KV missing", async () => {
  const kv = new FakeKV();
  const env = { DISCOVERY_KV: kv as unknown as KVNamespace };
  const res = await handleGetServers(env);
  assert.equal(res.status, 200);
  const body = await res.json() as { servers: unknown[] };
  assert.equal(body.servers.length, 0);
});
```

- [ ] **Step 2：跑测试，确认失败**

```bash
cd cf-worker && npm test
```

Expected: FAIL — module not found。

- [ ] **Step 3：实现 handler**

```typescript
// cf-worker/src/handlers/servers.ts
import type { Env } from "../index.js";
import { KV_KEY_ACTIVE, type ActiveServersDocument } from "../types.js";

const FALLBACK: ActiveServersDocument = {
  version: 1,
  updated_at: new Date(0).toISOString(),
  servers: [],
};

export async function handleGetServers(env: Env): Promise<Response> {
  const raw = await env.DISCOVERY_KV.get(KV_KEY_ACTIVE);
  const body: ActiveServersDocument = raw ? (JSON.parse(raw) as ActiveServersDocument) : FALLBACK;
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "public, max-age=60",
    },
  });
}
```

- [ ] **Step 4：路由接入**

`cf-worker/src/index.ts` fetch 内追加：

```typescript
if (request.method === "GET" && url.pathname === "/v1/servers") {
  return handleGetServers(env);
}
```

并 `import { handleGetServers } from "./handlers/servers.js";`。

- [ ] **Step 5：跑测试**

```bash
cd cf-worker && npm test
```

Expected: PASS。

- [ ] **Step 6：commit**

```bash
git add cf-worker/src/
git commit -m "feat(p0): implement GET /v1/servers with kv read"
```

---

### Task P0.6：实现 GET /v1/announcements（含测试）

**Files:**
- Create: `cf-worker/src/handlers/announcements.ts`
- Create: `cf-worker/src/handlers/announcements.test.ts`
- Modify: `cf-worker/src/index.ts`

- [ ] **Step 1：写失败测试**

```typescript
// cf-worker/src/handlers/announcements.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import { handleGetAnnouncements } from "./announcements.js";

class FakeKV {
  private store = new Map<string, string>();
  async get(k: string): Promise<string | null> { return this.store.get(k) ?? null; }
  set(k: string, v: string): void { this.store.set(k, v); }
}

test("GET /v1/announcements returns items", async () => {
  const kv = new FakeKV();
  kv.set("announcements", JSON.stringify({
    version: 1,
    updated_at: "2026-05-08T00:00:00Z",
    items: [{ id: "1", title: "t", body: "b", publishedAt: "2026-05-08T00:00:00Z" }],
  }));
  const env = { DISCOVERY_KV: kv as unknown as KVNamespace };
  const res = await handleGetAnnouncements(env);
  assert.equal(res.status, 200);
  const body = await res.json() as { items: unknown[] };
  assert.equal(body.items.length, 1);
});
```

- [ ] **Step 2：失败验证**

```bash
cd cf-worker && npm test
```

Expected: FAIL。

- [ ] **Step 3：实现 handler**

```typescript
// cf-worker/src/handlers/announcements.ts
import type { Env } from "../index.js";
import { KV_KEY_ANNOUNCEMENTS, type AnnouncementsDocument } from "../types.js";

const FALLBACK: AnnouncementsDocument = {
  version: 1,
  updated_at: new Date(0).toISOString(),
  items: [],
};

export async function handleGetAnnouncements(env: Env): Promise<Response> {
  const raw = await env.DISCOVERY_KV.get(KV_KEY_ANNOUNCEMENTS);
  const body: AnnouncementsDocument = raw ? (JSON.parse(raw) as AnnouncementsDocument) : FALLBACK;
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "public, max-age=300",
    },
  });
}
```

- [ ] **Step 4：路由接入**

`cf-worker/src/index.ts`：

```typescript
import { handleGetAnnouncements } from "./handlers/announcements.js";
// 在 fetch 内：
if (request.method === "GET" && url.pathname === "/v1/announcements") {
  return handleGetAnnouncements(env);
}
```

- [ ] **Step 5：通过验证**

```bash
cd cf-worker && npm test
```

Expected: PASS。

- [ ] **Step 6：commit**

```bash
git add cf-worker/src/
git commit -m "feat(p0): implement GET /v1/announcements with kv read"
```

---

### Task P0.7：Cron 聚合器（每 10 分钟）

**Files:**
- Create: `cf-worker/src/cron/aggregate.ts`
- Create: `cf-worker/src/cron/aggregate.test.ts`
- Modify: `cf-worker/src/index.ts`

- [ ] **Step 1：写失败测试（使用 fetch mock 模拟 peer）**

```typescript
// cf-worker/src/cron/aggregate.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import { aggregateActivePeers } from "./aggregate.js";

class FakeKV {
  private store = new Map<string, string>();
  async get(k: string): Promise<string | null> { return this.store.get(k) ?? null; }
  async put(k: string, v: string): Promise<void> { this.store.set(k, v); }
  read(k: string): string | undefined { return this.store.get(k); }
}

test("aggregate writes merged peers from seed list", async () => {
  const kv = new FakeKV();
  await kv.put("peers:seeds", JSON.stringify({
    version: 1, updated_at: "2026-05-08T00:00:00Z",
    seeds: [{ address: "https://a.example" }, { address: "https://b.example" }],
  }));

  const fetchMock = async (input: RequestInfo): Promise<Response> => {
    const url = typeof input === "string" ? input : input.url;
    if (url.startsWith("https://a.example/peers")) {
      return new Response(JSON.stringify({
        peers: [
          { address: "https://a.example", publicKey: "pa", lastSeen: "2026-05-08T00:00:00Z" },
          { address: "https://c.example", publicKey: "pc", lastSeen: "2026-05-08T00:00:00Z" },
        ],
      }), { status: 200 });
    }
    if (url.startsWith("https://b.example/peers")) {
      return new Response(JSON.stringify({
        peers: [{ address: "https://b.example", publicKey: "pb", lastSeen: "2026-05-08T00:00:00Z" }],
      }), { status: 200 });
    }
    return new Response("not found", { status: 404 });
  };

  await aggregateActivePeers({ DISCOVERY_KV: kv as unknown as KVNamespace }, fetchMock);
  const written = kv.read("peers:active");
  assert.ok(written, "active key should be written");
  const parsed = JSON.parse(written!);
  const addrs = parsed.servers.map((s: { address: string }) => s.address).sort();
  assert.deepEqual(addrs, ["https://a.example", "https://b.example", "https://c.example"]);
});

test("aggregate keeps previous active when all peers fail", async () => {
  const kv = new FakeKV();
  await kv.put("peers:active", JSON.stringify({
    version: 1, updated_at: "2026-05-08T00:00:00Z",
    servers: [{ address: "https://prev.example", lastSeen: "2026-05-08T00:00:00Z" }],
  }));
  await kv.put("peers:seeds", JSON.stringify({
    version: 1, updated_at: "2026-05-08T00:00:00Z",
    seeds: [{ address: "https://dead.example" }],
  }));

  const fetchMock = async (): Promise<Response> => { throw new Error("boom"); };

  await aggregateActivePeers({ DISCOVERY_KV: kv as unknown as KVNamespace }, fetchMock);
  const written = JSON.parse(kv.read("peers:active")!);
  assert.equal(written.servers[0].address, "https://prev.example");
});
```

- [ ] **Step 2：失败验证**

```bash
cd cf-worker && npm test
```

Expected: FAIL。

- [ ] **Step 3：实现聚合器**

```typescript
// cf-worker/src/cron/aggregate.ts
import type { Env } from "../index.js";
import {
  KV_KEY_ACTIVE,
  KV_KEY_SEEDS,
  type ActiveServersDocument,
  type PeerEntry,
  type SeedsDocument,
} from "../types.js";

type FetchLike = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

const SAMPLER_PER_SOURCE = 5;
const PEER_FETCH_TIMEOUT_MS = 5_000;

interface PeersResponse {
  peers: PeerEntry[];
}

export async function aggregateActivePeers(env: Env, fetchImpl: FetchLike = fetch): Promise<void> {
  const seeds = await loadSeeds(env);
  const previous = await loadActive(env);

  const samplerSet = new Set<string>();
  for (const s of seeds.slice(0, SAMPLER_PER_SOURCE)) samplerSet.add(s.address);
  for (const p of previous.slice(0, SAMPLER_PER_SOURCE)) samplerSet.add(p.address);

  const fetched = await Promise.allSettled(
    [...samplerSet].map((addr) => fetchPeers(addr, fetchImpl)),
  );

  const merged = new Map<string, PeerEntry>();
  let anySuccess = false;

  for (const r of fetched) {
    if (r.status !== "fulfilled") continue;
    anySuccess = true;
    for (const peer of r.value) {
      merged.set(peer.address, peer);
    }
  }

  if (!anySuccess && previous.length > 0) {
    return;
  }

  const document: ActiveServersDocument = {
    version: 1,
    updated_at: new Date().toISOString(),
    servers: [...merged.values()],
  };
  await env.DISCOVERY_KV.put(KV_KEY_ACTIVE, JSON.stringify(document));
}

async function loadSeeds(env: Env): Promise<Array<{ address: string }>> {
  const raw = await env.DISCOVERY_KV.get(KV_KEY_SEEDS);
  if (!raw) return [];
  return (JSON.parse(raw) as SeedsDocument).seeds;
}

async function loadActive(env: Env): Promise<PeerEntry[]> {
  const raw = await env.DISCOVERY_KV.get(KV_KEY_ACTIVE);
  if (!raw) return [];
  return (JSON.parse(raw) as ActiveServersDocument).servers;
}

async function fetchPeers(address: string, fetchImpl: FetchLike): Promise<PeerEntry[]> {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), PEER_FETCH_TIMEOUT_MS);
  try {
    const res = await fetchImpl(`${address.replace(/\/+$/, "")}/peers`, { signal: ctrl.signal });
    if (!res.ok) throw new Error(`http_${res.status}`);
    const body = (await res.json()) as PeersResponse;
    return body.peers ?? [];
  } finally {
    clearTimeout(timer);
  }
}
```

- [ ] **Step 4：scheduled handler 接入**

`cf-worker/src/index.ts`：

```typescript
import { aggregateActivePeers } from "./cron/aggregate.js";
// scheduled 替换为：
async scheduled(_event: ScheduledEvent, env: Env, ctx: ExecutionContext): Promise<void> {
  ctx.waitUntil(aggregateActivePeers(env));
},
```

- [ ] **Step 5：通过验证**

```bash
cd cf-worker && npm test
```

Expected: PASS — 2 tests passing。

- [ ] **Step 6：commit**

```bash
git add cf-worker/src/
git commit -m "feat(p0): implement cron aggregator pulling /peers from seed+previous samplers"
```

---

### Task P0.8：本地 dev 烟雾测试 + 部署

**Files:**
- Create: `cf-worker/scripts/seed-kv.sh`

- [ ] **Step 1：写 KV 初始化辅助脚本**

```bash
#!/usr/bin/env bash
# cf-worker/scripts/seed-kv.sh
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

SEEDS_PATH="${1:-../data/seeds.json}"
if [[ ! -f "$SEEDS_PATH" ]]; then
  echo "seeds file not found: $SEEDS_PATH" >&2
  exit 1
fi

npx wrangler kv key put --binding DISCOVERY_KV "peers:seeds" "$(cat "$SEEDS_PATH")"
npx wrangler kv key put --binding DISCOVERY_KV "announcements" '{"version":1,"updated_at":"2026-05-08T00:00:00Z","items":[]}'
echo "seeded peers:seeds and announcements"
```

```bash
chmod +x cf-worker/scripts/seed-kv.sh
```

- [ ] **Step 2：本地 dev 烟测**

```bash
cd cf-worker && npm run dev
# 另开一个终端：
curl http://127.0.0.1:8787/v1/seeds
curl http://127.0.0.1:8787/v1/servers
curl http://127.0.0.1:8787/v1/announcements
```

Expected: 三个端点均返回 200，body 是合法 JSON（KV 未填则 seeds=[]）。

- [ ] **Step 3：部署到 CF（需先执行手工 prerequisite，记入 spec §6.1 P0）**

部署前手工事项（一次性）：
1. CF 账号登录，`npx wrangler login`
2. 创建 KV namespace：`npx wrangler kv namespace create DISCOVERY_KV` → 把返回的 id 填回 `wrangler.toml`
3. 自有海外域名在 CF 上挂 DNS（CNAME 到 workers 域），把 worker route 配到该域

部署：

```bash
cd cf-worker && npx wrangler deploy
./scripts/seed-kv.sh ../data/seeds.json
```

- [ ] **Step 4：生产烟测**

```bash
DOMAIN="https://<your-domain>"
curl "$DOMAIN/v1/seeds"
curl "$DOMAIN/v1/servers"
curl "$DOMAIN/v1/announcements"
```

Expected: 三端点均 200；`/v1/seeds` 返回种子；其它两端点至少返回空骨架。

- [ ] **Step 5：commit**

```bash
git add cf-worker/scripts/seed-kv.sh
git commit -m "chore(p0): add kv seed script and document deploy prerequisites"
```

---

## 第 P1 章 · lobby-service v0.3 协议核心

### Task P1.1：节点身份模块（ed25519）

**Files:**
- Create: `lobby-service/src/peer/identity.ts`
- Create: `lobby-service/src/peer/identity.test.ts`

- [ ] **Step 1：写失败测试**

```typescript
// lobby-service/src/peer/identity.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { loadOrCreateIdentity, signChallenge, verifySignature } from "./identity.js";

function tmpDir(): string { return mkdtempSync(join(tmpdir(), "peer-id-")); }

test("loadOrCreateIdentity creates and persists keypair", async () => {
  const dir = tmpDir();
  try {
    const id1 = await loadOrCreateIdentity(dir);
    const id2 = await loadOrCreateIdentity(dir);
    assert.equal(id1.publicKey, id2.publicKey);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("signChallenge produces verifiable signature", async () => {
  const dir = tmpDir();
  try {
    const id = await loadOrCreateIdentity(dir);
    const sig = signChallenge(id, "hello");
    assert.ok(verifySignature(id.publicKey, "hello", sig));
    assert.ok(!verifySignature(id.publicKey, "tampered", sig));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
```

- [ ] **Step 2：失败验证**

```bash
cd lobby-service && npm test
```

Expected: FAIL — 模块不存在。

- [ ] **Step 3：实现身份模块**

```typescript
// lobby-service/src/peer/identity.ts
import { generateKeyPairSync, sign, verify, createPrivateKey, createPublicKey, type KeyObject } from "node:crypto";
import { mkdir, readFile, writeFile, chmod } from "node:fs/promises";
import { existsSync } from "node:fs";
import { dirname, join } from "node:path";

export interface NodeIdentity {
  privateKey: KeyObject;
  publicKey: string;
}

interface IdentityFile {
  version: 1;
  privateKeyPem: string;
  publicKeyPem: string;
}

const FILENAME = "peer-identity.key";

export async function loadOrCreateIdentity(stateDir: string): Promise<NodeIdentity> {
  const path = join(stateDir, FILENAME);
  if (!existsSync(path)) {
    await mkdir(dirname(path), { recursive: true });
    const { privateKey, publicKey } = generateKeyPairSync("ed25519");
    const file: IdentityFile = {
      version: 1,
      privateKeyPem: privateKey.export({ type: "pkcs8", format: "pem" }) as string,
      publicKeyPem: publicKey.export({ type: "spki", format: "pem" }) as string,
    };
    await writeFile(path, JSON.stringify(file));
    await chmod(path, 0o600);
  }
  const raw = await readFile(path, "utf8");
  const file = JSON.parse(raw) as IdentityFile;
  const privateKey = createPrivateKey(file.privateKeyPem);
  const publicKey = base64UrlSpki(file.publicKeyPem);
  return { privateKey, publicKey };
}

export function signChallenge(identity: NodeIdentity, challenge: string): string {
  const sig = sign(null, Buffer.from(challenge, "utf8"), identity.privateKey);
  return sig.toString("base64url");
}

export function verifySignature(publicKeyB64u: string, challenge: string, signatureB64u: string): boolean {
  try {
    const pemDer = Buffer.from(publicKeyB64u, "base64url");
    const pem = `-----BEGIN PUBLIC KEY-----\n${pemDer.toString("base64").match(/.{1,64}/g)!.join("\n")}\n-----END PUBLIC KEY-----\n`;
    const key = createPublicKey(pem);
    return verify(null, Buffer.from(challenge, "utf8"), key, Buffer.from(signatureB64u, "base64url"));
  } catch {
    return false;
  }
}

function base64UrlSpki(pem: string): string {
  const der = pem.replace(/-----BEGIN PUBLIC KEY-----/, "")
    .replace(/-----END PUBLIC KEY-----/, "")
    .replace(/\s+/g, "");
  return Buffer.from(der, "base64").toString("base64url");
}
```

- [ ] **Step 4：通过验证**

```bash
cd lobby-service && npm test
```

Expected: PASS。

- [ ] **Step 5：commit**

```bash
git add lobby-service/src/peer/
git commit -m "feat(p1): add ed25519 node identity module with challenge signing"
```

---

### Task P1.2：Peer DTO 类型定义（含 C# 镜像同步）

**Files:**
- Create: `lobby-service/src/peer/types.ts`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs`（追加 DTO）

- [ ] **Step 1：写 TypeScript 端类型**

```typescript
// lobby-service/src/peer/types.ts
export interface PeerRecord {
  address: string;
  publicKey: string;
  displayName?: string;
  firstSeen: string;
  lastSeen: string;
  consecutiveProbeFailures: number;
  status: "active" | "offline";
  source: "self" | "seed" | "announce" | "gossip";
}

export interface PeersListResponse {
  version: 1;
  generatedAt: string;
  peers: Array<{
    address: string;
    publicKey: string;
    displayName?: string;
    lastSeen: string;
    status: "active" | "offline";
  }>;
}

export interface AnnounceRequestBody {
  address: string;
  publicKey: string;
  displayName?: string;
}

export interface HeartbeatRequestBody {
  address: string;
  publicKey: string;
}

export interface HealthResponse {
  address: string;
  publicKey: string;
  challenge: string;
  signature: string;
  serverTime: string;
}
```

- [ ] **Step 2：在 C# 端镜像 DTO（保持 AGENTS.md 跨运行时同步要求）**

打开 `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs`，在文件末尾追加：

```csharp
// === Peer Network DTOs (mirror lobby-service/src/peer/types.ts) ===

internal sealed class PeerListEntry
{
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("lastSeen")]
    public string LastSeen { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";
}

internal sealed class PeersListResponse
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = string.Empty;

    [JsonPropertyName("peers")]
    public List<PeerListEntry> Peers { get; set; } = new();
}

internal sealed class PeerHealthResponse
{
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("serverTime")]
    public string ServerTime { get; set; } = string.Empty;
}
```

- [ ] **Step 3：TS 编译通过**

```bash
cd lobby-service && npm run check
```

Expected: 无错误。

- [ ] **Step 4：commit**

```bash
git add lobby-service/src/peer/types.ts sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs
git commit -m "feat(p1): define peer protocol DTOs in ts and mirror to C#"
```

---

### Task P1.3：Peer 持久化存储（JSON + 原子写）

**Files:**
- Create: `lobby-service/src/peer/store.ts`
- Create: `lobby-service/src/peer/store.test.ts`

- [ ] **Step 1：写失败测试**

```typescript
// lobby-service/src/peer/store.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PeerStore } from "./store.js";

function tmpDir(): string { return mkdtempSync(join(tmpdir(), "peer-store-")); }

test("upsert + list + persistence roundtrip", async () => {
  const dir = tmpDir();
  try {
    const s1 = new PeerStore(join(dir, "peers.json"));
    await s1.load();
    await s1.upsert({
      address: "https://a.example",
      publicKey: "pa",
      firstSeen: "2026-05-08T00:00:00Z",
      lastSeen: "2026-05-08T00:00:00Z",
      consecutiveProbeFailures: 0,
      status: "active",
      source: "seed",
    });
    await s1.flush();

    const s2 = new PeerStore(join(dir, "peers.json"));
    await s2.load();
    const list = s2.list();
    assert.equal(list.length, 1);
    assert.equal(list[0].address, "https://a.example");
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("recordProbeFailure marks offline at threshold and TTL deletes", async () => {
  const dir = tmpDir();
  try {
    const s = new PeerStore(join(dir, "peers.json"));
    await s.load();
    await s.upsert({
      address: "https://a.example",
      publicKey: "pa",
      firstSeen: "2026-05-08T00:00:00Z",
      lastSeen: "2026-05-08T00:00:00Z",
      consecutiveProbeFailures: 0,
      status: "active",
      source: "seed",
    });
    s.recordProbeFailure("https://a.example");
    s.recordProbeFailure("https://a.example");
    s.recordProbeFailure("https://a.example");
    assert.equal(s.list()[0].status, "offline");

    const cutoff8d = new Date(Date.now() - 8 * 86400_000);
    s.list()[0].lastSeen = cutoff8d.toISOString();
    s.runTtlCleanup(new Date());
    assert.equal(s.list().length, 0);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
```

- [ ] **Step 2：失败验证**

```bash
cd lobby-service && npm test
```

Expected: FAIL。

- [ ] **Step 3：实现存储**

```typescript
// lobby-service/src/peer/store.ts
import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import { dirname } from "node:path";
import type { PeerRecord } from "./types.js";

const PROBE_FAILURE_THRESHOLD = 3;
const TTL_HARD_DELETE_DAYS = 7;

interface PersistedFile {
  version: 1;
  peers: PeerRecord[];
}

export class PeerStore {
  private peers: Map<string, PeerRecord> = new Map();
  private flushPending = false;

  constructor(private readonly path: string) {}

  async load(): Promise<void> {
    if (!existsSync(this.path)) {
      this.peers = new Map();
      return;
    }
    const raw = await readFile(this.path, "utf8");
    const file = JSON.parse(raw) as PersistedFile;
    this.peers = new Map(file.peers.map((p) => [p.address, p]));
  }

  async flush(): Promise<void> {
    await mkdir(dirname(this.path), { recursive: true });
    const file: PersistedFile = { version: 1, peers: [...this.peers.values()] };
    const tmp = `${this.path}.tmp`;
    await writeFile(tmp, JSON.stringify(file, null, 2));
    await rename(tmp, this.path);
    this.flushPending = false;
  }

  scheduleFlush(): void {
    if (this.flushPending) return;
    this.flushPending = true;
    setTimeout(() => { this.flush().catch(() => { this.flushPending = false; }); }, 100);
  }

  list(): PeerRecord[] { return [...this.peers.values()]; }

  get(address: string): PeerRecord | undefined { return this.peers.get(address); }

  async upsert(record: PeerRecord): Promise<void> {
    this.peers.set(record.address, record);
    this.scheduleFlush();
  }

  async heartbeat(address: string, publicKey: string, now = new Date()): Promise<boolean> {
    const existing = this.peers.get(address);
    if (!existing || existing.publicKey !== publicKey) return false;
    existing.lastSeen = now.toISOString();
    existing.status = "active";
    existing.consecutiveProbeFailures = 0;
    this.scheduleFlush();
    return true;
  }

  recordProbeFailure(address: string): void {
    const p = this.peers.get(address);
    if (!p) return;
    p.consecutiveProbeFailures += 1;
    if (p.consecutiveProbeFailures >= PROBE_FAILURE_THRESHOLD) p.status = "offline";
    this.scheduleFlush();
  }

  recordProbeSuccess(address: string, now = new Date()): void {
    const p = this.peers.get(address);
    if (!p) return;
    p.consecutiveProbeFailures = 0;
    p.lastSeen = now.toISOString();
    p.status = "active";
    this.scheduleFlush();
  }

  runTtlCleanup(now = new Date()): number {
    const hardCutoff = now.getTime() - TTL_HARD_DELETE_DAYS * 86400_000;
    let removed = 0;
    for (const [addr, p] of this.peers) {
      if (new Date(p.lastSeen).getTime() < hardCutoff) {
        this.peers.delete(addr);
        removed += 1;
      }
    }
    if (removed > 0) this.scheduleFlush();
    return removed;
  }
}
```

- [ ] **Step 4：通过验证**

```bash
cd lobby-service && npm test
```

Expected: PASS。

- [ ] **Step 5：commit**

```bash
git add lobby-service/src/peer/
git commit -m "feat(p1): add peer persistent store (json + atomic write + ttl cleanup)"
```

---

### Task P1.4：实现 GET /peers/health（含 challenge 签名）

**Files:**
- Create: `lobby-service/src/peer/handlers/health.ts`
- Create: `lobby-service/src/peer/handlers/health.test.ts`

- [ ] **Step 1：写失败测试**

```typescript
// lobby-service/src/peer/handlers/health.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { loadOrCreateIdentity, verifySignature } from "../identity.js";
import { mountHealth } from "./health.js";

test("/peers/health returns signed challenge", async () => {
  const dir = mkdtempSync(join(tmpdir(), "health-"));
  try {
    const identity = await loadOrCreateIdentity(dir);
    const app = express();
    mountHealth(app, { identity, address: "https://self.example" });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/health?challenge=hi`);
    server.close();
    assert.equal(res.status, 200);
    const body = await res.json() as { challenge: string; signature: string; publicKey: string };
    assert.equal(body.challenge, "hi");
    assert.ok(verifySignature(body.publicKey, body.challenge, body.signature));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("/peers/health 400 if challenge missing", async () => {
  const dir = mkdtempSync(join(tmpdir(), "health-"));
  try {
    const identity = await loadOrCreateIdentity(dir);
    const app = express();
    mountHealth(app, { identity, address: "https://self.example" });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/health`);
    server.close();
    assert.equal(res.status, 400);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
```

- [ ] **Step 2：失败验证**

```bash
cd lobby-service && npm test
```

Expected: FAIL。

- [ ] **Step 3：实现 handler**

```typescript
// lobby-service/src/peer/handlers/health.ts
import type { Express, Request, Response } from "express";
import type { NodeIdentity } from "../identity.js";
import { signChallenge } from "../identity.js";
import type { HealthResponse } from "../types.js";

interface Deps {
  identity: NodeIdentity;
  address: string;
}

export function mountHealth(app: Express, deps: Deps): void {
  app.get("/peers/health", (req: Request, res: Response) => {
    const challenge = typeof req.query.challenge === "string" ? req.query.challenge : "";
    if (!challenge || challenge.length > 256) {
      res.status(400).json({ error: "challenge_required" });
      return;
    }
    const body: HealthResponse = {
      address: deps.address,
      publicKey: deps.identity.publicKey,
      challenge,
      signature: signChallenge(deps.identity, challenge),
      serverTime: new Date().toISOString(),
    };
    res.status(200).json(body);
  });
}
```

- [ ] **Step 4：通过验证**

```bash
cd lobby-service && npm test
```

Expected: PASS。

- [ ] **Step 5：commit**

```bash
git add lobby-service/src/peer/handlers/
git commit -m "feat(p1): implement /peers/health with ed25519 challenge signing"
```

---

### Task P1.5：实现 GET /peers

**Files:**
- Create: `lobby-service/src/peer/handlers/list.ts`
- Create: `lobby-service/src/peer/handlers/list.test.ts`

- [ ] **Step 1：写失败测试**

```typescript
// lobby-service/src/peer/handlers/list.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PeerStore } from "../store.js";
import { mountList } from "./list.js";

test("/peers returns active peers only by default", async () => {
  const dir = mkdtempSync(join(tmpdir(), "list-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    await store.upsert({
      address: "https://a", publicKey: "pa", firstSeen: "2026-05-08T00:00:00Z",
      lastSeen: "2026-05-08T00:00:00Z", consecutiveProbeFailures: 0, status: "active", source: "seed",
    });
    await store.upsert({
      address: "https://b", publicKey: "pb", firstSeen: "2026-05-08T00:00:00Z",
      lastSeen: "2026-05-08T00:00:00Z", consecutiveProbeFailures: 5, status: "offline", source: "seed",
    });

    const app = express();
    mountList(app, { store });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers`);
    server.close();
    assert.equal(res.status, 200);
    const body = await res.json() as { peers: Array<{ address: string }> };
    assert.equal(body.peers.length, 1);
    assert.equal(body.peers[0].address, "https://a");
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
```

- [ ] **Step 2：失败验证**

```bash
cd lobby-service && npm test
```

Expected: FAIL。

- [ ] **Step 3：实现**

```typescript
// lobby-service/src/peer/handlers/list.ts
import type { Express, Request, Response } from "express";
import type { PeerStore } from "../store.js";
import type { PeersListResponse } from "../types.js";

export function mountList(app: Express, deps: { store: PeerStore }): void {
  app.get("/peers", (_req: Request, res: Response) => {
    const peers = deps.store.list().filter((p) => p.status === "active");
    const body: PeersListResponse = {
      version: 1,
      generatedAt: new Date().toISOString(),
      peers: peers.map((p) => ({
        address: p.address,
        publicKey: p.publicKey,
        displayName: p.displayName,
        lastSeen: p.lastSeen,
        status: p.status,
      })),
    };
    res.status(200).json(body);
  });
}
```

- [ ] **Step 4：通过验证**

```bash
cd lobby-service && npm test
```

Expected: PASS。

- [ ] **Step 5：commit**

```bash
git add lobby-service/src/peer/handlers/
git commit -m "feat(p1): implement GET /peers (active only)"
```

---

### Task P1.6：实现 POST /peers/announce + Liveness Prober

**Files:**
- Create: `lobby-service/src/peer/prober.ts`
- Create: `lobby-service/src/peer/prober.test.ts`
- Create: `lobby-service/src/peer/handlers/announce.ts`
- Create: `lobby-service/src/peer/handlers/announce.test.ts`

- [ ] **Step 1：写 prober 测试**

```typescript
// lobby-service/src/peer/prober.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { loadOrCreateIdentity } from "./identity.js";
import { mountHealth } from "./handlers/health.js";
import { probeAndVerify } from "./prober.js";

test("probeAndVerify returns publicKey when target signs correctly", async () => {
  const dir = mkdtempSync(join(tmpdir(), "prober-"));
  try {
    const identity = await loadOrCreateIdentity(dir);
    const app = express();
    mountHealth(app, { identity, address: "https://self" });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const result = await probeAndVerify(`http://127.0.0.1:${port}`);
    server.close();
    assert.ok(result.ok);
    if (result.ok) assert.equal(result.publicKey, identity.publicKey);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("probeAndVerify fails when target unreachable", async () => {
  const result = await probeAndVerify("http://127.0.0.1:1");
  assert.ok(!result.ok);
});
```

- [ ] **Step 2：实现 prober**

```typescript
// lobby-service/src/peer/prober.ts
import { randomBytes } from "node:crypto";
import { verifySignature } from "./identity.js";
import type { HealthResponse } from "./types.js";

const PROBE_TIMEOUT_MS = 5_000;

export type ProbeResult = { ok: true; publicKey: string } | { ok: false; reason: string };

export async function probeAndVerify(address: string, expectedKey?: string): Promise<ProbeResult> {
  const challenge = randomBytes(16).toString("base64url");
  const ctrl = new AbortController();
  const t = setTimeout(() => ctrl.abort(), PROBE_TIMEOUT_MS);
  try {
    const url = `${address.replace(/\/+$/, "")}/peers/health?challenge=${encodeURIComponent(challenge)}`;
    const res = await fetch(url, { signal: ctrl.signal });
    if (!res.ok) return { ok: false, reason: `http_${res.status}` };
    const body = (await res.json()) as HealthResponse;
    if (body.challenge !== challenge) return { ok: false, reason: "challenge_mismatch" };
    if (!verifySignature(body.publicKey, challenge, body.signature)) return { ok: false, reason: "signature_invalid" };
    if (expectedKey && expectedKey !== body.publicKey) return { ok: false, reason: "publickey_mismatch" };
    return { ok: true, publicKey: body.publicKey };
  } catch (err) {
    return { ok: false, reason: err instanceof Error ? err.message : "unknown" };
  } finally { clearTimeout(t); }
}
```

- [ ] **Step 3：写 announce 测试**

```typescript
// lobby-service/src/peer/handlers/announce.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { loadOrCreateIdentity } from "../identity.js";
import { mountHealth } from "./health.js";
import { PeerStore } from "../store.js";
import { mountAnnounce } from "./announce.js";

test("/peers/announce accepts new peer after liveness probe succeeds", async () => {
  const dir = mkdtempSync(join(tmpdir(), "announce-"));
  try {
    const identity = await loadOrCreateIdentity(join(dir, "remote"));
    const remote = express();
    mountHealth(remote, { identity, address: "" });
    const remoteServer = remote.listen(0);
    const remotePort = (remoteServer.address() as { port: number }).port;

    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    const local = express();
    local.use(express.json());
    mountAnnounce(local, { store });
    const localServer = local.listen(0);
    const localPort = (localServer.address() as { port: number }).port;

    const res = await fetch(`http://127.0.0.1:${localPort}/peers/announce`, {
      method: "POST", headers: { "content-type": "application/json" },
      body: JSON.stringify({ address: `http://127.0.0.1:${remotePort}`, publicKey: identity.publicKey }),
    });
    remoteServer.close();
    localServer.close();
    assert.equal(res.status, 202);
    assert.equal(store.list().length, 1);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("/peers/announce rejects 400 if address missing", async () => {
  const dir = mkdtempSync(join(tmpdir(), "announce-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    const app = express();
    app.use(express.json());
    mountAnnounce(app, { store });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/announce`, {
      method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({}),
    });
    server.close();
    assert.equal(res.status, 400);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
```

- [ ] **Step 4：实现 announce handler**

```typescript
// lobby-service/src/peer/handlers/announce.ts
import type { Express, Request, Response } from "express";
import { probeAndVerify } from "../prober.js";
import type { PeerStore } from "../store.js";
import type { AnnounceRequestBody } from "../types.js";

const ANNOUNCE_RATE_LIMIT_PER_HOUR = 5;
const recentByIp: Map<string, number[]> = new Map();

export function mountAnnounce(app: Express, deps: { store: PeerStore }): void {
  app.post("/peers/announce", async (req: Request, res: Response) => {
    const body = req.body as Partial<AnnounceRequestBody>;
    if (!body || typeof body.address !== "string" || typeof body.publicKey !== "string") {
      res.status(400).json({ error: "address_and_publicKey_required" });
      return;
    }

    const ip = (req.ip ?? "0.0.0.0").toString();
    if (rateLimited(ip)) { res.status(429).json({ error: "rate_limited" }); return; }

    const probe = await probeAndVerify(body.address, body.publicKey);
    if (!probe.ok) {
      res.status(422).json({ error: "probe_failed", reason: probe.reason });
      return;
    }

    const now = new Date();
    await deps.store.upsert({
      address: body.address,
      publicKey: probe.publicKey,
      displayName: body.displayName,
      firstSeen: deps.store.get(body.address)?.firstSeen ?? now.toISOString(),
      lastSeen: now.toISOString(),
      consecutiveProbeFailures: 0,
      status: "active",
      source: "announce",
    });
    res.status(202).json({ accepted: true });
  });
}

function rateLimited(ip: string): boolean {
  const now = Date.now();
  const cutoff = now - 3600_000;
  const list = (recentByIp.get(ip) ?? []).filter((t) => t > cutoff);
  if (list.length >= ANNOUNCE_RATE_LIMIT_PER_HOUR) { recentByIp.set(ip, list); return true; }
  list.push(now); recentByIp.set(ip, list); return false;
}
```

- [ ] **Step 5：通过验证**

```bash
cd lobby-service && npm test
```

Expected: PASS — 4 tests passing。

- [ ] **Step 6：commit**

```bash
git add lobby-service/src/peer/
git commit -m "feat(p1): implement liveness prober and POST /peers/announce with rate limit"
```

---

### Task P1.7：实现 POST /peers/heartbeat

**Files:**
- Create: `lobby-service/src/peer/handlers/heartbeat.ts`
- Create: `lobby-service/src/peer/handlers/heartbeat.test.ts`

- [ ] **Step 1：写失败测试**

```typescript
// lobby-service/src/peer/handlers/heartbeat.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PeerStore } from "../store.js";
import { mountHeartbeat } from "./heartbeat.js";

test("heartbeat updates lastSeen for known peer", async () => {
  const dir = mkdtempSync(join(tmpdir(), "hb-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    await store.upsert({
      address: "https://a", publicKey: "pa",
      firstSeen: "2025-01-01T00:00:00Z", lastSeen: "2025-01-01T00:00:00Z",
      consecutiveProbeFailures: 0, status: "active", source: "seed",
    });
    const app = express();
    app.use(express.json());
    mountHeartbeat(app, { store });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/heartbeat`, {
      method: "POST", headers: { "content-type": "application/json" },
      body: JSON.stringify({ address: "https://a", publicKey: "pa" }),
    });
    server.close();
    assert.equal(res.status, 200);
    const updated = store.get("https://a");
    assert.notEqual(updated?.lastSeen, "2025-01-01T00:00:00Z");
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("heartbeat 404 for unknown peer", async () => {
  const dir = mkdtempSync(join(tmpdir(), "hb-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    const app = express();
    app.use(express.json());
    mountHeartbeat(app, { store });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/heartbeat`, {
      method: "POST", headers: { "content-type": "application/json" },
      body: JSON.stringify({ address: "https://nope", publicKey: "p" }),
    });
    server.close();
    assert.equal(res.status, 404);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
```

- [ ] **Step 2：失败验证**

```bash
cd lobby-service && npm test
```

Expected: FAIL。

- [ ] **Step 3：实现**

```typescript
// lobby-service/src/peer/handlers/heartbeat.ts
import type { Express, Request, Response } from "express";
import type { PeerStore } from "../store.js";
import type { HeartbeatRequestBody } from "../types.js";

export function mountHeartbeat(app: Express, deps: { store: PeerStore }): void {
  app.post("/peers/heartbeat", async (req: Request, res: Response) => {
    const body = req.body as Partial<HeartbeatRequestBody>;
    if (!body || typeof body.address !== "string" || typeof body.publicKey !== "string") {
      res.status(400).json({ error: "address_and_publicKey_required" });
      return;
    }
    const ok = await deps.store.heartbeat(body.address, body.publicKey);
    if (!ok) { res.status(404).json({ error: "unknown_peer_or_key_mismatch" }); return; }
    res.status(200).json({ accepted: true });
  });
}
```

- [ ] **Step 4：通过验证**

```bash
cd lobby-service && npm test
```

Expected: PASS。

- [ ] **Step 5：commit**

```bash
git add lobby-service/src/peer/handlers/
git commit -m "feat(p1): implement POST /peers/heartbeat"
```

---

### Task P1.8：Gossip 调度器（pull 5min / push 30min / TTL 24h+7d）

**Files:**
- Create: `lobby-service/src/peer/gossip.ts`
- Create: `lobby-service/src/peer/gossip.test.ts`

- [ ] **Step 1：写失败测试**

```typescript
// lobby-service/src/peer/gossip.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PeerStore } from "./store.js";
import { GossipScheduler } from "./gossip.js";
import type { ProbeResult } from "./prober.js";

test("pull cycle merges peers from sampled known peers", async () => {
  const dir = mkdtempSync(join(tmpdir(), "gossip-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    await store.upsert({
      address: "https://a", publicKey: "pa",
      firstSeen: "2026-05-08T00:00:00Z", lastSeen: "2026-05-08T00:00:00Z",
      consecutiveProbeFailures: 0, status: "active", source: "seed",
    });

    const fakeFetchPeers = async (addr: string) => {
      if (addr === "https://a") return [{ address: "https://b", publicKey: "pb", lastSeen: "2026-05-08T00:00:00Z" }];
      return [];
    };
    const fakeProbe = async (_addr: string, _expectedKey?: string): Promise<ProbeResult> => ({ ok: true, publicKey: "pb" });

    const sched = new GossipScheduler({
      store, selfAddress: "https://self",
      fetchPeers: fakeFetchPeers, probeAndVerify: fakeProbe,
      seedAddresses: [],
    });
    await sched.runPullCycleOnce();

    const list = store.list();
    assert.ok(list.find((p) => p.address === "https://b"));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("ttl marks offline peers older than 24h, deletes those older than 7d", async () => {
  const dir = mkdtempSync(join(tmpdir(), "gossip-ttl-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    const now = new Date("2026-06-01T00:00:00Z");
    const day3Ago = new Date(now.getTime() - 3 * 86400_000);
    const day9Ago = new Date(now.getTime() - 9 * 86400_000);
    await store.upsert({
      address: "https://stale-3d", publicKey: "p1",
      firstSeen: day3Ago.toISOString(), lastSeen: day3Ago.toISOString(),
      consecutiveProbeFailures: 0, status: "active", source: "seed",
    });
    await store.upsert({
      address: "https://stale-9d", publicKey: "p2",
      firstSeen: day9Ago.toISOString(), lastSeen: day9Ago.toISOString(),
      consecutiveProbeFailures: 0, status: "active", source: "seed",
    });

    const sched = new GossipScheduler({
      store, selfAddress: "https://self",
      fetchPeers: async () => [], probeAndVerify: async () => ({ ok: false, reason: "" }),
      seedAddresses: [],
    });
    sched.runTtlCycleOnce(now);

    const after = store.list();
    assert.equal(after.find((p) => p.address === "https://stale-3d")?.status, "offline");
    assert.ok(!after.find((p) => p.address === "https://stale-9d"));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
```

- [ ] **Step 2：失败验证**

```bash
cd lobby-service && npm test
```

Expected: FAIL。

- [ ] **Step 3：实现 gossip 调度器**

```typescript
// lobby-service/src/peer/gossip.ts
import type { PeerStore } from "./store.js";
import type { ProbeResult } from "./prober.js";
import { probeAndVerify as defaultProbe } from "./prober.js";

const PULL_INTERVAL_MS = 5 * 60_000;
const PUSH_INTERVAL_MS = 30 * 60_000;
const TTL_INTERVAL_MS = 60 * 60_000;
const PULL_FANOUT = 3;
const PUSH_FANOUT = 3;
const OFFLINE_AFTER_MS = 24 * 3600_000;

export interface GossipDeps {
  store: PeerStore;
  selfAddress: string;
  selfPublicKey?: string;
  seedAddresses: string[];
  fetchPeers?: (address: string) => Promise<Array<{ address: string; publicKey: string; lastSeen: string }>>;
  probeAndVerify?: (address: string, expectedKey?: string) => Promise<ProbeResult>;
  postHeartbeat?: (address: string, body: { address: string; publicKey: string }) => Promise<void>;
}

export class GossipScheduler {
  private timers: NodeJS.Timeout[] = [];
  constructor(private readonly deps: GossipDeps) {}

  start(): void {
    this.timers.push(setInterval(() => { void this.runPullCycleOnce(); }, PULL_INTERVAL_MS));
    this.timers.push(setInterval(() => { void this.runPushCycleOnce(); }, PUSH_INTERVAL_MS));
    this.timers.push(setInterval(() => { this.runTtlCycleOnce(); }, TTL_INTERVAL_MS));
  }

  stop(): void { for (const t of this.timers) clearInterval(t); this.timers = []; }

  async runPullCycleOnce(): Promise<void> {
    const peers = this.deps.store.list().filter((p) => p.status === "active" && p.address !== this.deps.selfAddress);
    const sample = pickRandom(peers.map((p) => p.address), PULL_FANOUT);
    if (sample.length === 0) return;
    const fetcher = this.deps.fetchPeers ?? defaultFetchPeers;
    const probe = this.deps.probeAndVerify ?? defaultProbe;
    const merged = new Map<string, { address: string; publicKey: string; lastSeen: string }>();
    for (const addr of sample) {
      try {
        for (const p of await fetcher(addr)) merged.set(p.address, p);
      } catch { /* ignore peer-level failure */ }
    }
    for (const p of merged.values()) {
      if (p.address === this.deps.selfAddress) continue;
      if (this.deps.store.get(p.address)) continue;
      const probed = await probe(p.address, p.publicKey);
      if (!probed.ok) continue;
      await this.deps.store.upsert({
        address: p.address, publicKey: probed.publicKey,
        firstSeen: new Date().toISOString(), lastSeen: p.lastSeen,
        consecutiveProbeFailures: 0, status: "active", source: "gossip",
      });
    }
  }

  async runPushCycleOnce(): Promise<void> {
    if (!this.deps.selfPublicKey || !this.deps.postHeartbeat) return;
    const peers = this.deps.store.list().filter((p) => p.status === "active" && p.address !== this.deps.selfAddress);
    const sample = pickRandom(peers.map((p) => p.address), PUSH_FANOUT);
    for (const addr of sample) {
      try {
        await this.deps.postHeartbeat(addr, { address: this.deps.selfAddress, publicKey: this.deps.selfPublicKey });
      } catch { /* ignore */ }
    }
  }

  runTtlCycleOnce(now = new Date()): void {
    const offlineCutoff = now.getTime() - OFFLINE_AFTER_MS;
    for (const p of this.deps.store.list()) {
      if (p.status === "active" && new Date(p.lastSeen).getTime() < offlineCutoff) {
        p.status = "offline";
        this.deps.store.scheduleFlush();
      }
    }
    this.deps.store.runTtlCleanup(now);
  }
}

function pickRandom<T>(arr: T[], n: number): T[] {
  const copy = arr.slice();
  for (let i = copy.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [copy[i], copy[j]] = [copy[j], copy[i]];
  }
  return copy.slice(0, n);
}

async function defaultFetchPeers(address: string): Promise<Array<{ address: string; publicKey: string; lastSeen: string }>> {
  const ctrl = new AbortController();
  const t = setTimeout(() => ctrl.abort(), 5_000);
  try {
    const res = await fetch(`${address.replace(/\/+$/, "")}/peers`, { signal: ctrl.signal });
    if (!res.ok) return [];
    const body = (await res.json()) as { peers?: Array<{ address: string; publicKey: string; lastSeen: string }> };
    return body.peers ?? [];
  } catch { return []; } finally { clearTimeout(t); }
}
```

- [ ] **Step 4：注意 PeerStore 内部 `scheduleFlush` 调用**

`runTtlCycleOnce` 直接调用 `this.deps.store.scheduleFlush()`——该方法已在 P1.3 实现为 public，无需追加。

- [ ] **Step 5：通过验证**

```bash
cd lobby-service && npm test
```

Expected: PASS — 6 tests passing。

- [ ] **Step 6：commit**

```bash
git add lobby-service/src/peer/
git commit -m "feat(p1): implement gossip scheduler (pull 5min / push 30min / ttl 24h+7d)"
```

---

### Task P1.9：CF 种子加载器

**Files:**
- Create: `lobby-service/src/peer/seeds-loader.ts`
- Create: `lobby-service/src/peer/seeds-loader.test.ts`

- [ ] **Step 1：写失败测试**

```typescript
// lobby-service/src/peer/seeds-loader.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { loadSeedsFromCf } from "./seeds-loader.js";

test("loadSeedsFromCf returns empty list when CF unreachable", async () => {
  const result = await loadSeedsFromCf("http://127.0.0.1:1");
  assert.deepEqual(result, []);
});

test("loadSeedsFromCf parses CF response correctly", async () => {
  const app = express();
  app.get("/v1/seeds", (_req, res) => res.json({
    version: 1, updated_at: "2026-05-08T00:00:00Z",
    seeds: [{ address: "https://a" }, { address: "https://b" }],
  }));
  const server = app.listen(0);
  const port = (server.address() as { port: number }).port;
  const result = await loadSeedsFromCf(`http://127.0.0.1:${port}`);
  server.close();
  assert.deepEqual(result.map((s) => s.address).sort(), ["https://a", "https://b"]);
});
```

- [ ] **Step 2：失败验证**

```bash
cd lobby-service && npm test
```

Expected: FAIL。

- [ ] **Step 3：实现**

```typescript
// lobby-service/src/peer/seeds-loader.ts
const TIMEOUT_MS = 5_000;

export interface SeedAddress { address: string; note?: string; }

export async function loadSeedsFromCf(cfBaseUrl: string): Promise<SeedAddress[]> {
  const ctrl = new AbortController();
  const t = setTimeout(() => ctrl.abort(), TIMEOUT_MS);
  try {
    const url = `${cfBaseUrl.replace(/\/+$/, "")}/v1/seeds`;
    const res = await fetch(url, { signal: ctrl.signal });
    if (!res.ok) return [];
    const body = (await res.json()) as { seeds?: SeedAddress[] };
    return body.seeds ?? [];
  } catch { return []; } finally { clearTimeout(t); }
}
```

- [ ] **Step 4：通过验证**

```bash
cd lobby-service && npm test
```

Expected: PASS。

- [ ] **Step 5：commit**

```bash
git add lobby-service/src/peer/
git commit -m "feat(p1): implement cf seeds loader"
```

---

### Task P1.10：Bootstrap 模块（启动时拉种子并入库）

**Files:**
- Create: `lobby-service/src/peer/bootstrap.ts`
- Create: `lobby-service/src/peer/bootstrap.test.ts`

- [ ] **Step 1：写失败测试**

```typescript
// lobby-service/src/peer/bootstrap.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PeerStore } from "./store.js";
import { bootstrapPeers } from "./bootstrap.js";
import type { ProbeResult } from "./prober.js";

test("bootstrap adds seeds that pass probe", async () => {
  const dir = mkdtempSync(join(tmpdir(), "boot-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    const probes: Record<string, ProbeResult> = {
      "https://good": { ok: true, publicKey: "pg" },
      "https://bad": { ok: false, reason: "timeout" },
    };
    await bootstrapPeers({
      store,
      selfAddress: "https://self",
      seeds: [{ address: "https://good" }, { address: "https://bad" }],
      probeAndVerify: async (addr) => probes[addr] ?? { ok: false, reason: "unknown" },
    });
    const list = store.list();
    assert.equal(list.length, 1);
    assert.equal(list[0].address, "https://good");
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("bootstrap skips self", async () => {
  const dir = mkdtempSync(join(tmpdir(), "boot-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    await bootstrapPeers({
      store,
      selfAddress: "https://self",
      seeds: [{ address: "https://self" }],
      probeAndVerify: async () => ({ ok: true, publicKey: "ps" }),
    });
    assert.equal(store.list().length, 0);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
```

- [ ] **Step 2：失败验证**

```bash
cd lobby-service && npm test
```

Expected: FAIL。

- [ ] **Step 3：实现**

```typescript
// lobby-service/src/peer/bootstrap.ts
import type { PeerStore } from "./store.js";
import type { ProbeResult } from "./prober.js";
import { probeAndVerify as defaultProbe } from "./prober.js";
import type { SeedAddress } from "./seeds-loader.js";

export interface BootstrapDeps {
  store: PeerStore;
  selfAddress: string;
  seeds: SeedAddress[];
  probeAndVerify?: (address: string, expectedKey?: string) => Promise<ProbeResult>;
}

export async function bootstrapPeers(deps: BootstrapDeps): Promise<void> {
  const probe = deps.probeAndVerify ?? defaultProbe;
  for (const seed of deps.seeds) {
    if (seed.address === deps.selfAddress) continue;
    if (deps.store.get(seed.address)) continue;
    const r = await probe(seed.address);
    if (!r.ok) continue;
    const now = new Date().toISOString();
    await deps.store.upsert({
      address: seed.address, publicKey: r.publicKey,
      firstSeen: now, lastSeen: now, consecutiveProbeFailures: 0,
      status: "active", source: "seed",
    });
  }
}
```

- [ ] **Step 4：通过验证**

```bash
cd lobby-service && npm test
```

Expected: PASS。

- [ ] **Step 5：commit**

```bash
git add lobby-service/src/peer/
git commit -m "feat(p1): implement bootstrap module loading seeds into peer store"
```

---

### Task P1.11：把 peer 子系统装进 server.ts

**Files:**
- Modify: `lobby-service/src/server.ts`

- [ ] **Step 1：识别现有 server.ts 启动结构**

阅读 `lobby-service/src/server.ts` 的 `function main()` / 启动段（搜索 `app.listen` 与现有 `console.log` 入口附近）；确认 `env` 配置块的结构（参考 line 70–76 的 `serverRegistry*` 配置区）。

- [ ] **Step 2：在 env 配置块新增 peer 相关变量**

`server.ts` 顶部 env 解析处追加（接在 `serverRegistry*` 之后）：

```typescript
const peerEnv = {
  enabled: process.env.PEER_NETWORK_ENABLED !== "false",
  selfAddress: process.env.PEER_SELF_ADDRESS ?? "",
  cfDiscoveryBaseUrl: process.env.PEER_CF_DISCOVERY_BASE_URL ?? "",
  stateDir: process.env.PEER_STATE_DIR ?? "./data/peer",
};
```

- [ ] **Step 3：在 main() 启动时挂载 peer 子系统**

在 `app.listen` 之前插入：

```typescript
import { loadOrCreateIdentity } from "./peer/identity.js";
import { PeerStore } from "./peer/store.js";
import { mountHealth } from "./peer/handlers/health.js";
import { mountList } from "./peer/handlers/list.js";
import { mountAnnounce } from "./peer/handlers/announce.js";
import { mountHeartbeat } from "./peer/handlers/heartbeat.js";
import { GossipScheduler } from "./peer/gossip.js";
import { loadSeedsFromCf } from "./peer/seeds-loader.js";
import { bootstrapPeers } from "./peer/bootstrap.js";
import { join } from "node:path";

// ...在 main() 内、app.listen 之前：
if (peerEnv.enabled && peerEnv.selfAddress) {
  const identity = await loadOrCreateIdentity(peerEnv.stateDir);
  const peerStore = new PeerStore(join(peerEnv.stateDir, "peers.json"));
  await peerStore.load();
  mountHealth(app, { identity, address: peerEnv.selfAddress });
  mountList(app, { store: peerStore });
  mountAnnounce(app, { store: peerStore });
  mountHeartbeat(app, { store: peerStore });

  if (peerEnv.cfDiscoveryBaseUrl) {
    const seeds = await loadSeedsFromCf(peerEnv.cfDiscoveryBaseUrl);
    await bootstrapPeers({ store: peerStore, selfAddress: peerEnv.selfAddress, seeds });
  }

  const scheduler = new GossipScheduler({
    store: peerStore,
    selfAddress: peerEnv.selfAddress,
    selfPublicKey: identity.publicKey,
    seedAddresses: [],
    postHeartbeat: async (addr, body) => {
      await fetch(`${addr.replace(/\/+$/, "")}/peers/heartbeat`, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
    },
  });
  scheduler.start();
  console.log(`[peer] mounted; self=${peerEnv.selfAddress} cf=${peerEnv.cfDiscoveryBaseUrl || "(none)"}`);
} else {
  console.log("[peer] disabled (set PEER_SELF_ADDRESS to enable)");
}
```

- [ ] **Step 4：跳过不可达 server-registry 的旧上报循环**

定位现有 server-admin-sync 的启动调用（参考 `server.ts:734-737`），改为：

```typescript
if (env.serverRegistryBaseUrl) {
  // existing sync setup...
} else {
  console.log("[server-admin] registry sync disabled (legacy SERVER_REGISTRY_BASE_URL not set)");
}
```

如果该判断已存在则保持原样，不重复修改。

- [ ] **Step 5：编译 + 测试**

```bash
cd lobby-service && npm run check && npm test
```

Expected: 编译无错误；现有所有测试 + 新增 peer 测试都 PASS。

- [ ] **Step 6：手工启动烟测**

```bash
cd lobby-service
PEER_SELF_ADDRESS="http://127.0.0.1:8787" PEER_STATE_DIR="./data-test/peer" \
PEER_CF_DISCOVERY_BASE_URL="https://<your-cf-domain>" \
npm start &
sleep 2
curl -s "http://127.0.0.1:8787/peers/health?challenge=hello" | head -c 500
curl -s "http://127.0.0.1:8787/peers"
kill %1
```

Expected: `/peers/health` 返回带签名响应；`/peers` 返回 peers 列表（启动时从 CF seeds 加载）。

- [ ] **Step 7：commit**

```bash
git add lobby-service/src/server.ts
git commit -m "feat(p1): wire peer subsystem into lobby-service main entry"
```

---

### Task P1.12：bump lobby-service 版本到 0.3.0

**Files:**
- Modify: `lobby-service/package.json`
- Modify: `README.md`（顶部 badges）

- [ ] **Step 1：编辑 version**

```json
{
  "version": "0.3.0"
}
```

- [ ] **Step 2：更新 README.md badge**

将 `service-v0.2.2-green` 改为 `service-v0.3.0-green`。

- [ ] **Step 3：commit**

```bash
git add lobby-service/package.json README.md
git commit -m "chore(p1): bump lobby-service to v0.3.0"
```

---

## 第 P2 章 · Sidecar

### Task P2.1：Sidecar 项目脚手架

**Files:**
- Create: `sts2-peer-sidecar/package.json`
- Create: `sts2-peer-sidecar/tsconfig.json`
- Create: `sts2-peer-sidecar/src/index.ts`（占位）
- Create: `sts2-peer-sidecar/.gitignore`

- [ ] **Step 1：建立 npm 工程**

`sts2-peer-sidecar/package.json`：

```json
{
  "name": "sts2-peer-sidecar",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "engines": { "node": ">=20.11.0" },
  "scripts": {
    "build": "tsc -p tsconfig.json",
    "check": "tsc -p tsconfig.json --noEmit",
    "start": "node --enable-source-maps dist/index.js",
    "test": "npm run build && node --test dist/**/*.test.js"
  },
  "dependencies": {
    "express": "^5.1.0"
  },
  "devDependencies": {
    "@types/express": "^5.0.3",
    "@types/node": "^22.15.31",
    "typescript": "^5.8.3"
  }
}
```

`sts2-peer-sidecar/tsconfig.json`：

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ES2022",
    "moduleResolution": "node16",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "outDir": "dist",
    "rootDir": "src"
  },
  "include": ["src/**/*"]
}
```

`sts2-peer-sidecar/src/index.ts`：

```typescript
import express from "express";

async function main(): Promise<void> {
  const port = Number.parseInt(process.env.PEER_LISTEN_PORT ?? "18800", 10);
  const app = express();
  app.use(express.json());
  app.get("/healthz", (_req, res) => res.status(200).json({ ok: true }));
  app.listen(port, () => console.log(`[sidecar] listening on ${port}`));
}

main().catch((err) => { console.error(err); process.exit(1); });
```

`sts2-peer-sidecar/.gitignore`：

```
node_modules/
dist/
data/
```

- [ ] **Step 2：安装依赖**

```bash
cd sts2-peer-sidecar && npm install
```

- [ ] **Step 3：commit**

```bash
git add sts2-peer-sidecar/
git commit -m "feat(p2): scaffold sts2-peer-sidecar npm project"
```

---

### Task P2.2：复用 lobby-service peer 模块（symlink 或 copy）

**Files:**
- Modify: `sts2-peer-sidecar/package.json`（增加 file 依赖）
- Create: `sts2-peer-sidecar/src/use-peer-modules.ts`

**关键决策**：避免代码复制，sidecar 直接以 file 依赖方式引用 lobby-service 编译产物中的 peer 模块。

- [ ] **Step 1：在 sidecar 的 package.json 加 file 依赖**

```json
{
  "dependencies": {
    "express": "^5.1.0",
    "sts2-lobby-service": "file:../lobby-service"
  }
}
```

```bash
cd sts2-peer-sidecar && npm install
```

注意：`sts2-lobby-service` 的 `package.json.name` 当前是 `sts2-lobby-service`，import path 为 `sts2-lobby-service/dist/peer/...`。需要在 lobby-service 的 `package.json` 增加 `"main": "dist/server.js"` 与 `"exports"` 字段以便导出 peer 子模块。

- [ ] **Step 2：在 lobby-service/package.json 增加 exports**

```json
{
  "main": "dist/server.js",
  "exports": {
    "./peer/*": "./dist/peer/*.js"
  }
}
```

- [ ] **Step 3：sidecar 引用 peer 模块**

`sts2-peer-sidecar/src/use-peer-modules.ts`：

```typescript
export { loadOrCreateIdentity } from "sts2-lobby-service/peer/identity";
export { PeerStore } from "sts2-lobby-service/peer/store";
export { mountHealth } from "sts2-lobby-service/peer/handlers/health";
export { mountList } from "sts2-lobby-service/peer/handlers/list";
export { mountAnnounce } from "sts2-lobby-service/peer/handlers/announce";
export { mountHeartbeat } from "sts2-lobby-service/peer/handlers/heartbeat";
export { GossipScheduler } from "sts2-lobby-service/peer/gossip";
export { loadSeedsFromCf } from "sts2-lobby-service/peer/seeds-loader";
export { bootstrapPeers } from "sts2-lobby-service/peer/bootstrap";
```

- [ ] **Step 4：编译 lobby-service（sidecar 依赖其 dist/）**

```bash
cd lobby-service && npm run build
```

- [ ] **Step 5：编译 sidecar 验证 import 通**

```bash
cd sts2-peer-sidecar && npm run check
```

Expected: 无错误。

- [ ] **Step 6：commit**

```bash
git add lobby-service/package.json sts2-peer-sidecar/
git commit -m "feat(p2): expose lobby-service peer modules via package exports for sidecar reuse"
```

---

### Task P2.3：Sidecar main 装配

**Files:**
- Modify: `sts2-peer-sidecar/src/index.ts`

- [ ] **Step 1：替换占位 main**

```typescript
import express from "express";
import { join } from "node:path";
import {
  loadOrCreateIdentity, PeerStore,
  mountHealth, mountList, mountAnnounce, mountHeartbeat,
  GossipScheduler, loadSeedsFromCf, bootstrapPeers,
} from "./use-peer-modules.js";

interface Config {
  listenPort: number;
  lobbyPublicBaseUrl: string;
  cfDiscoveryBaseUrl: string;
  stateDir: string;
}

function loadConfig(): Config {
  const lobbyUrl = process.env.LOBBY_PUBLIC_BASE_URL;
  if (!lobbyUrl) throw new Error("LOBBY_PUBLIC_BASE_URL is required");
  return {
    listenPort: Number.parseInt(process.env.PEER_LISTEN_PORT ?? "18800", 10),
    lobbyPublicBaseUrl: lobbyUrl,
    cfDiscoveryBaseUrl: process.env.PEER_CF_DISCOVERY_BASE_URL ?? "",
    stateDir: process.env.PEER_STATE_DIR ?? "./data",
  };
}

async function main(): Promise<void> {
  const cfg = loadConfig();
  const identity = await loadOrCreateIdentity(cfg.stateDir);
  const store = new PeerStore(join(cfg.stateDir, "peers.json"));
  await store.load();

  const app = express();
  app.use(express.json());
  mountHealth(app, { identity, address: cfg.lobbyPublicBaseUrl });
  mountList(app, { store });
  mountAnnounce(app, { store });
  mountHeartbeat(app, { store });

  if (cfg.cfDiscoveryBaseUrl) {
    const seeds = await loadSeedsFromCf(cfg.cfDiscoveryBaseUrl);
    await bootstrapPeers({ store, selfAddress: cfg.lobbyPublicBaseUrl, seeds });
  }

  const scheduler = new GossipScheduler({
    store, selfAddress: cfg.lobbyPublicBaseUrl, selfPublicKey: identity.publicKey,
    seedAddresses: [],
    postHeartbeat: async (addr, body) => {
      await fetch(`${addr.replace(/\/+$/, "")}/peers/heartbeat`, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
    },
  });
  scheduler.start();

  app.listen(cfg.listenPort, () => {
    console.log(`[sidecar] listening on ${cfg.listenPort}; representing ${cfg.lobbyPublicBaseUrl}`);
  });
}

main().catch((err) => { console.error(err); process.exit(1); });
```

- [ ] **Step 2：编译**

```bash
cd sts2-peer-sidecar && npm run build
```

- [ ] **Step 3：手工烟测**

```bash
cd sts2-peer-sidecar
LOBBY_PUBLIC_BASE_URL="http://127.0.0.1:8787" \
PEER_LISTEN_PORT=18800 PEER_STATE_DIR="./data-test" \
node dist/index.js &
sleep 1
curl -s "http://127.0.0.1:18800/peers/health?challenge=hi"
curl -s "http://127.0.0.1:18800/peers"
kill %1
```

Expected: 两端点正常响应。

- [ ] **Step 4：commit**

```bash
git add sts2-peer-sidecar/src/
git commit -m "feat(p2): assemble sidecar main with identity+store+gossip"
```

---

### Task P2.4：Sidecar systemd unit + 安装脚本 + tarball

**Files:**
- Create: `sts2-peer-sidecar/deploy/sts2-peer-sidecar.service`
- Create: `sts2-peer-sidecar/deploy/install.sh`
- Create: `scripts/package-sts2-peer-sidecar.sh`

- [ ] **Step 1：systemd unit 模板**

```ini
# sts2-peer-sidecar/deploy/sts2-peer-sidecar.service
[Unit]
Description=STS2 Lan Connect Peer Sidecar
After=network.target

[Service]
Type=simple
User=sts2sidecar
EnvironmentFile=/etc/sts2-peer-sidecar/sidecar.env
WorkingDirectory=/opt/sts2-peer-sidecar
ExecStart=/usr/bin/node --enable-source-maps /opt/sts2-peer-sidecar/dist/index.js
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

- [ ] **Step 2：安装脚本**

```bash
#!/usr/bin/env bash
# sts2-peer-sidecar/deploy/install.sh
set -euo pipefail

TARBALL="${1:-sts2-peer-sidecar.tar.gz}"
INSTALL_DIR="/opt/sts2-peer-sidecar"
ENV_DIR="/etc/sts2-peer-sidecar"
USER_NAME="sts2sidecar"

if [[ "$EUID" -ne 0 ]]; then
  echo "must run as root" >&2; exit 1
fi

if ! id "$USER_NAME" &>/dev/null; then
  useradd --system --no-create-home --shell /usr/sbin/nologin "$USER_NAME"
fi

mkdir -p "$INSTALL_DIR" "$ENV_DIR"
tar -xzf "$TARBALL" -C "$INSTALL_DIR" --strip-components=1
chown -R "$USER_NAME":"$USER_NAME" "$INSTALL_DIR"

if [[ ! -f "$ENV_DIR/sidecar.env" ]]; then
  cat >"$ENV_DIR/sidecar.env" <<'EOF'
LOBBY_PUBLIC_BASE_URL=https://your-lobby.example.com
PEER_LISTEN_PORT=18800
PEER_CF_DISCOVERY_BASE_URL=https://your-cf-domain.example.com
PEER_STATE_DIR=/var/lib/sts2-peer-sidecar
EOF
  echo "wrote default env to $ENV_DIR/sidecar.env — edit before starting"
fi

mkdir -p /var/lib/sts2-peer-sidecar
chown -R "$USER_NAME":"$USER_NAME" /var/lib/sts2-peer-sidecar

cp "$INSTALL_DIR/deploy/sts2-peer-sidecar.service" /etc/systemd/system/
systemctl daemon-reload
echo "installed; enable with: sudo systemctl enable --now sts2-peer-sidecar"
```

```bash
chmod +x sts2-peer-sidecar/deploy/install.sh
```

- [ ] **Step 3：打包脚本**

```bash
#!/usr/bin/env bash
# scripts/package-sts2-peer-sidecar.sh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SIDECAR_DIR="$ROOT_DIR/sts2-peer-sidecar"
OUT_DIR="$ROOT_DIR/releases/sts2_peer_sidecar"
TARBALL="$OUT_DIR/sts2-peer-sidecar.tar.gz"

cd "$ROOT_DIR/lobby-service" && npm run build
cd "$SIDECAR_DIR" && npm install && npm run build

mkdir -p "$OUT_DIR"
rm -f "$TARBALL"

tar -czf "$TARBALL" \
  -C "$ROOT_DIR" \
  --transform 's|^sts2-peer-sidecar/|sts2-peer-sidecar/|' \
  sts2-peer-sidecar/dist \
  sts2-peer-sidecar/node_modules \
  sts2-peer-sidecar/package.json \
  sts2-peer-sidecar/deploy

echo "wrote $TARBALL"
```

```bash
chmod +x scripts/package-sts2-peer-sidecar.sh
```

- [ ] **Step 4：本地打包验证**

```bash
./scripts/package-sts2-peer-sidecar.sh
ls -lh releases/sts2_peer_sidecar/sts2-peer-sidecar.tar.gz
```

Expected: tarball 生成；约 5–15 MB。

- [ ] **Step 5：commit**

```bash
git add sts2-peer-sidecar/deploy/ scripts/package-sts2-peer-sidecar.sh
git commit -m "feat(p2): add sidecar systemd unit, install script, packaging"
```

---

## 第 P3 章 · 客户端 v0.3

### Task P3.1：扩展 LanConnectBundledLobbyDefaults DTO

**Files:**
- Modify: `sts2-lan-connect/Scripts/LanConnectLobbyEndpointDefaults.cs`

- [ ] **Step 1：在 LanConnectBundledLobbyDefaults 新增字段**

定位 `LanConnectBundledLobbyDefaults` 类（文件起始处），追加：

```csharp
[JsonPropertyName("seedPeers")]
public List<string> SeedPeers { get; set; } = new();

[JsonPropertyName("cfDiscoveryBaseUrl")]
public string CfDiscoveryBaseUrl { get; set; } = string.Empty;
```

需在 `using` 区追加 `using System.Collections.Generic;`。

- [ ] **Step 2：在 LanConnectLobbyEndpointDefaults 静态类添加访问器**

类内追加（参考已有 `_registryBaseUrl` 模式）：

```csharp
private static List<string> _seedPeers = new();
private static string _cfDiscoveryBaseUrl = string.Empty;

public static IReadOnlyList<string> GetSeedPeers()
{
    EnsureLoaded();
    return _seedPeers;
}

public static string GetCfDiscoveryBaseUrl()
{
    EnsureLoaded();
    return _cfDiscoveryBaseUrl;
}
```

- [ ] **Step 3：在 LoadUnsafe() 中读取这两个字段**

`LoadUnsafe` 内、其他赋值之后追加：

```csharp
_seedPeers = defaults?.SeedPeers ?? new List<string>();
_cfDiscoveryBaseUrl = (defaults?.CfDiscoveryBaseUrl ?? string.Empty).Trim().TrimEnd('/');
```

且在 catch 块的 reset 区域同步追加：

```csharp
_seedPeers = new List<string>();
_cfDiscoveryBaseUrl = string.Empty;
```

- [ ] **Step 4：commit**

```bash
git add sts2-lan-connect/Scripts/LanConnectLobbyEndpointDefaults.cs
git commit -m "feat(p3): extend LanConnectBundledLobbyDefaults with seedPeers and cfDiscoveryBaseUrl"
```

---

### Task P3.2：打包脚本注入 seeds 与 CF URL

**Files:**
- Modify: `scripts/package-sts2-lan-connect.sh`

- [ ] **Step 1：在 env 文档区追加新变量名**

`scripts/package-sts2-lan-connect.sh` 顶部 usage 内：

```
  STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL
  STS2_LOBBY_SEEDS_FILE   (defaults to <repo>/data/seeds.json)
```

- [ ] **Step 2：在生成 lobby-defaults.json 的逻辑里追加注入**

定位脚本内构造 `lobby-defaults.json` 的段落（搜索 `lobby-defaults.json` 或 `LOCAL_DEFAULTS_FILE`），在写文件前追加生成 seeds 数组的 jq 片段：

```bash
SEEDS_FILE="${STS2_LOBBY_SEEDS_FILE:-$ROOT_DIR/data/seeds.json}"
SEEDS_JSON_ARRAY="[]"
if [[ -f "$SEEDS_FILE" ]]; then
  SEEDS_JSON_ARRAY="$(jq -c '[.seeds[].address]' "$SEEDS_FILE")"
fi
CF_DISCOVERY_URL="${STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL:-}"
```

将这两个变量插入到现有 `lobby-defaults.json` 的 jq 构造里（参照现有 `baseUrl` `registryBaseUrl` 等字段的拼接模式）。例如若现有为：

```bash
jq -n --arg base "$BASE" --arg reg "$REG" \
  '{baseUrl:$base, registryBaseUrl:$reg}' > "$LOCAL_DEFAULTS_FILE"
```

改为：

```bash
jq -n --arg base "$BASE" --arg reg "$REG" --arg cf "$CF_DISCOVERY_URL" --argjson seeds "$SEEDS_JSON_ARRAY" \
  '{baseUrl:$base, registryBaseUrl:$reg, cfDiscoveryBaseUrl:$cf, seedPeers:$seeds}' > "$LOCAL_DEFAULTS_FILE"
```

（实际改动取决于现有脚本——以保持现有字段为前提，追加 cf 与 seeds。）

- [ ] **Step 3：本地打包验证**

```bash
STS2_LOBBY_DEFAULT_BASE_URL="http://test.example:8787" \
STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL="https://discovery.example" \
./scripts/package-sts2-lan-connect.sh --skip-build
cat sts2-lan-connect/release/sts2_lan_connect/lobby-defaults.json
```

Expected: JSON 含 `seedPeers` 数组（从 `data/seeds.json` 拷贝的 6 个地址）和 `cfDiscoveryBaseUrl`。

- [ ] **Step 4：commit**

```bash
git add scripts/package-sts2-lan-connect.sh
git commit -m "feat(p3): inject seedPeers and cfDiscoveryBaseUrl into client lobby-defaults.json"
```

---

### Task P3.3：本地 Peer 缓存 C# 类

**Files:**
- Create: `sts2-lan-connect/Scripts/Lobby/LanConnectKnownPeersCache.cs`

- [ ] **Step 1：写完整文件**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal sealed class KnownPeerEntry
{
    [JsonPropertyName("address")] public string Address { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("lastSeenInListing")] public string? LastSeenInListing { get; set; }
    [JsonPropertyName("lastSuccessConnect")] public string? LastSuccessConnect { get; set; }
    [JsonPropertyName("consecutiveFailures")] public int ConsecutiveFailures { get; set; }
    [JsonPropertyName("discoveredVia")] public string DiscoveredVia { get; set; } = "unknown";
    [JsonPropertyName("isFavorite")] public bool IsFavorite { get; set; }
}

internal sealed class KnownPeersFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = string.Empty;
    [JsonPropertyName("entries")] public List<KnownPeerEntry> Entries { get; set; } = new();
}

internal static class LanConnectKnownPeersCache
{
    private const int MaxEntries = 200;
    private const int StaleDays = 14;
    private const int FailureThreshold = 5;
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string PathFile => Path.Combine(LanConnectPaths.ResolveWritableDataDirectory(), "known_peers.json");

    public static List<KnownPeerEntry> Load()
    {
        lock (Sync)
        {
            if (!File.Exists(PathFile)) return new List<KnownPeerEntry>();
            try
            {
                string json = File.ReadAllText(PathFile);
                var file = JsonSerializer.Deserialize<KnownPeersFile>(json, LanConnectJson.Options);
                return file?.Entries ?? new List<KnownPeerEntry>();
            }
            catch (Exception ex)
            {
                Log.Warn($"sts2_lan_connect failed to load known_peers.json: {ex.Message}");
                return new List<KnownPeerEntry>();
            }
        }
    }

    public static void Save(IEnumerable<KnownPeerEntry> entries)
    {
        lock (Sync)
        {
            var file = new KnownPeersFile
            {
                Version = 1,
                UpdatedAt = DateTime.UtcNow.ToString("o"),
                Entries = entries.Take(MaxEntries).ToList(),
            };
            string tmp = PathFile + ".tmp";
            string dir = Path.GetDirectoryName(PathFile)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(tmp, JsonSerializer.Serialize(file, JsonOpts));
            if (File.Exists(PathFile)) File.Delete(PathFile);
            File.Move(tmp, PathFile);
        }
    }

    public static List<KnownPeerEntry> Cleanup(IEnumerable<KnownPeerEntry> entries, DateTime now)
    {
        var staleCutoff = now - TimeSpan.FromDays(StaleDays);
        var keep = new List<KnownPeerEntry>();
        foreach (var e in entries)
        {
            if (e.IsFavorite) { keep.Add(e); continue; }
            if (DateTime.TryParse(e.LastSeenInListing ?? "", out var seen)
                && seen < staleCutoff
                && e.ConsecutiveFailures >= FailureThreshold)
            {
                continue;
            }
            keep.Add(e);
        }
        return keep
            .OrderByDescending(e => DateTime.TryParse(e.LastSuccessConnect ?? "", out var t) ? t : DateTime.MinValue)
            .Take(MaxEntries)
            .ToList();
    }
}
```

注意：`LanConnectPaths.ResolveWritableDataDirectory()` 与 `LanConnectJson.Options` 在仓库已有；如不存在则在该任务步骤中追加占位。

- [ ] **Step 2：编译验证（在 Godot 之外用 dotnet build 跑）**

```bash
cd sts2-lan-connect && dotnet build
```

Expected: 编译通过。

- [ ] **Step 3：commit**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectKnownPeersCache.cs
git commit -m "feat(p3): add local known peers cache (json file with atomic write + cleanup)"
```

---

### Task P3.4：CF Discovery 客户端

**Files:**
- Create: `sts2-lan-connect/Scripts/Lobby/LanConnectCfDiscoveryClient.cs`

- [ ] **Step 1：写文件**

```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal sealed class CfServerEntry
{
    public string Address { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string LastSeen { get; set; } = string.Empty;
}

internal static class LanConnectCfDiscoveryClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static async Task<List<CfServerEntry>> GetServersAsync(string cfBaseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cfBaseUrl)) return new List<CfServerEntry>();
        try
        {
            using var client = new HttpClient { Timeout = Timeout };
            using var resp = await client.GetAsync($"{cfBaseUrl.TrimEnd('/')}/v1/servers", ct);
            if (!resp.IsSuccessStatusCode) return new List<CfServerEntry>();
            string text = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("servers", out var servers)) return new List<CfServerEntry>();
            var result = new List<CfServerEntry>();
            foreach (var s in servers.EnumerateArray())
            {
                result.Add(new CfServerEntry
                {
                    Address = s.GetProperty("address").GetString() ?? "",
                    PublicKey = s.TryGetProperty("publicKey", out var pk) ? (pk.GetString() ?? "") : "",
                    DisplayName = s.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
                    LastSeen = s.TryGetProperty("lastSeen", out var ls) ? (ls.GetString() ?? "") : "",
                });
            }
            return result;
        }
        catch
        {
            return new List<CfServerEntry>();
        }
    }
}
```

- [ ] **Step 2：编译**

```bash
cd sts2-lan-connect && dotnet build
```

Expected: 通过。

- [ ] **Step 3：commit**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectCfDiscoveryClient.cs
git commit -m "feat(p3): add cf discovery http client (GET /v1/servers)"
```

---

### Task P3.5：HTTP HEAD Ping 工具

**Files:**
- Create: `sts2-lan-connect/Scripts/Lobby/LanConnectPeerPing.cs`

- [ ] **Step 1：写文件**

```csharp
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal enum PingBucket { Low, Mid, High, Unreachable }

internal static class LanConnectPeerPing
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private const int LowMs = 500;
    private const int MidMs = 2000;

    public static async Task<(int Ms, PingBucket Bucket)> PingAsync(string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return (-1, PingBucket.Unreachable);
        var url = $"{baseUrl.TrimEnd('/')}/peers/health?ping=1";
        try
        {
            using var client = new HttpClient { Timeout = Timeout };
            var sw = Stopwatch.StartNew();
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await client.SendAsync(req, ct);
            sw.Stop();
            if (!resp.IsSuccessStatusCode) return ((int)sw.ElapsedMilliseconds, PingBucket.Unreachable);
            int ms = (int)sw.ElapsedMilliseconds;
            return (ms, ms <= LowMs ? PingBucket.Low : ms <= MidMs ? PingBucket.Mid : PingBucket.High);
        }
        catch
        {
            return (-1, PingBucket.Unreachable);
        }
    }
}
```

- [ ] **Step 2：编译**

```bash
cd sts2-lan-connect && dotnet build
```

Expected: 通过。

- [ ] **Step 3：commit**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectPeerPing.cs
git commit -m "feat(p3): add HTTP HEAD ping helper with 3-bucket categorization"
```

---

### Task P3.6：三源 Bootstrap 协调器

**Files:**
- Create: `sts2-lan-connect/Scripts/Lobby/LanConnectServerListBootstrap.cs`

- [ ] **Step 1：写文件**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal sealed class ServerListEntry
{
    public string Address { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Source { get; set; } = "unknown"; // cache | cf | seed
    public bool IsFavorite { get; set; }
    public DateTime? LastSuccessConnect { get; set; }
    public PingBucket Bucket { get; set; } = PingBucket.Unreachable;
    public int? PingMs { get; set; }
}

internal static class LanConnectServerListBootstrap
{
    public static async Task<List<ServerListEntry>> GatherAsync(CancellationToken ct = default)
    {
        var cache = LanConnectKnownPeersCache.Load();
        var cleaned = LanConnectKnownPeersCache.Cleanup(cache, DateTime.UtcNow);
        LanConnectKnownPeersCache.Save(cleaned);

        var cfTask = LanConnectCfDiscoveryClient.GetServersAsync(LanConnectLobbyEndpointDefaults.GetCfDiscoveryBaseUrl(), ct);
        var seeds = LanConnectLobbyEndpointDefaults.GetSeedPeers();

        List<CfServerEntry> cfList;
        try { cfList = await cfTask.WaitAsync(TimeSpan.FromSeconds(5), ct); }
        catch { cfList = new List<CfServerEntry>(); }

        var byAddr = new Dictionary<string, ServerListEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cleaned)
        {
            byAddr[c.Address] = new ServerListEntry
            {
                Address = c.Address, DisplayName = c.DisplayName, Source = "cache",
                IsFavorite = c.IsFavorite,
                LastSuccessConnect = DateTime.TryParse(c.LastSuccessConnect ?? "", out var t) ? t : null,
            };
        }
        foreach (var c in cfList)
        {
            if (!byAddr.TryGetValue(c.Address, out var e))
            {
                byAddr[c.Address] = new ServerListEntry { Address = c.Address, DisplayName = c.DisplayName, Source = "cf" };
            }
        }
        foreach (var s in seeds)
        {
            if (!byAddr.ContainsKey(s))
            {
                byAddr[s] = new ServerListEntry { Address = s, Source = "seed" };
            }
        }
        return byAddr.Values.ToList();
    }

    public static async Task PingAllAsync(List<ServerListEntry> entries, CancellationToken ct = default)
    {
        var tasks = entries.Select(async e =>
        {
            var (ms, bucket) = await LanConnectPeerPing.PingAsync(e.Address, ct);
            e.PingMs = ms >= 0 ? ms : null;
            e.Bucket = bucket;
        }).ToList();
        await Task.WhenAll(tasks);
    }
}
```

- [ ] **Step 2：编译**

```bash
cd sts2-lan-connect && dotnet build
```

Expected: 通过。

- [ ] **Step 3：commit**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectServerListBootstrap.cs
git commit -m "feat(p3): add three-source bootstrap coordinator (cache+cf+seeds) with ping enrichment"
```

---

### Task P3.7：Godot 选服弹窗 UI

**Files:**
- Create: `sts2-lan-connect/Scripts/Lobby/LanConnectServerSelectionDialog.cs`
- Create: `sts2-lan-connect/Scenes/Lobby/ServerSelectionDialog.tscn`（手动在 Godot 编辑器创建并保存）

> **关键说明**：本步骤涉及 Godot 编辑器手动操作（创建 .tscn 场景并绑定 C# 脚本）。无法纯命令行 TDD，改为"实现 + 手工烟测"的非 TDD 步骤。

- [ ] **Step 1：写 C# 脚本**

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Sts2LanConnect.Scripts;

public partial class LanConnectServerSelectionDialog : Window
{
    private VBoxContainer? _list;
    private Button? _refreshButton;
    private Button? _manualButton;
    private LineEdit? _manualInput;
    private CheckBox? _autoConnectCheck;
    private Label? _statusLabel;

    private System.Collections.Generic.List<ServerListEntry> _entries = new();

    public event Action<string>? ServerChosen;

    public override void _Ready()
    {
        Title = "选择服务器";
        _list = GetNode<VBoxContainer>("%ServerList");
        _refreshButton = GetNode<Button>("%RefreshButton");
        _manualButton = GetNode<Button>("%ManualButton");
        _manualInput = GetNode<LineEdit>("%ManualInput");
        _autoConnectCheck = GetNode<CheckBox>("%AutoConnectCheck");
        _statusLabel = GetNode<Label>("%StatusLabel");

        _refreshButton.Pressed += () => _ = RefreshAsync();
        _manualButton.Pressed += OnManualConnect;
        CloseRequested += () => QueueFree();

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_statusLabel != null) _statusLabel.Text = "刷新中...";
        _entries = await LanConnectServerListBootstrap.GatherAsync();
        await LanConnectServerListBootstrap.PingAllAsync(_entries);
        Render();
        if (_statusLabel != null) _statusLabel.Text = $"共 {_entries.Count} 个，刷新完成";
    }

    private void Render()
    {
        if (_list == null) return;
        foreach (var child in _list.GetChildren()) child.QueueFree();

        var ordered = _entries
            .OrderByDescending(e => e.LastSuccessConnect ?? DateTime.MinValue)
            .ThenBy(e => e.Bucket)
            .ThenBy(e => e.Address);
        foreach (var e in ordered)
        {
            var btn = new Button
            {
                Text = $"[{(e.PingMs.HasValue ? e.PingMs.Value.ToString() + "ms" : "—")}] {e.DisplayName ?? e.Address}",
                CustomMinimumSize = new Vector2(560, 32),
                ToolTipText = $"address: {e.Address}\nsource: {e.Source}",
            };
            string addr = e.Address;
            btn.Pressed += () => { ServerChosen?.Invoke(addr); QueueFree(); };
            _list.AddChild(btn);
        }
    }

    private void OnManualConnect()
    {
        string addr = (_manualInput?.Text ?? "").Trim();
        if (!string.IsNullOrEmpty(addr)) { ServerChosen?.Invoke(addr); QueueFree(); }
    }
}
```

- [ ] **Step 2：在 Godot 编辑器创建 .tscn 场景**

打开 Godot 编辑器，新建 `Scenes/Lobby/ServerSelectionDialog.tscn`，根节点 `Window`，挂上述脚本。子树：

```
Window
└─ MarginContainer
   └─ VBoxContainer
      ├─ Label "选择服务器"
      ├─ ScrollContainer
      │  └─ VBoxContainer (unique name: ServerList)
      ├─ HBoxContainer
      │  ├─ LineEdit  (unique name: ManualInput, placeholder "手动输入 https://...")
      │  └─ Button (unique name: ManualButton, text "连接")
      ├─ HBoxContainer
      │  ├─ CheckBox (unique name: AutoConnectCheck, text "自动连接上次使用")
      │  └─ Button (unique name: RefreshButton, text "刷新")
      └─ Label (unique name: StatusLabel)
```

将场景保存。

- [ ] **Step 3：编译 + Godot 编辑器中手工烟测**

```bash
./scripts/build-sts2-lan-connect.sh
```

在 Godot 内手工实例化弹窗，确认能弹出、列表能填充、刷新可用、点击一项触发 `ServerChosen` 事件。

- [ ] **Step 4：commit**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectServerSelectionDialog.cs sts2-lan-connect/Scenes/Lobby/ServerSelectionDialog.tscn
git commit -m "feat(p3): add server selection dialog scene + script"
```

---

### Task P3.8：替换启动入口 — 弹窗优先

**Files:**
- Modify: `sts2-lan-connect/Scripts/Entry.cs`（或现行启动协调点）
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs`（如其在 mod 启动时被自动展开）

- [ ] **Step 1：阅读现有启动流程**

打开 `sts2-lan-connect/Scripts/Entry.cs`，搜索"自动加载默认大厅"或类似入口（已知会基于 `LanConnectConfig.LobbyBaseUrl` 直接创建 lobby overlay）。识别需要在该处插入"弹窗 → 选完服务器再加载 lobby"的拦截点。

- [ ] **Step 2：在启动入口插入弹窗**

修改 `Entry.cs` 的相关初始化方法（伪代码示意，实际需匹配现有结构）：

```csharp
// 替代原本"直接加载 lobby with default base url"的位置：
private void StartLobbyFlow()
{
    var autoConnectAddr = LanConnectConfig.AutoConnectLastServer ? LanConnectConfig.LastUsedServerAddress : null;
    if (!string.IsNullOrEmpty(autoConnectAddr))
    {
        // 3 秒倒计时弹窗，带"取消"
        ShowAutoConnectCountdown(autoConnectAddr, () => OpenLobbyWith(autoConnectAddr), () => OpenServerSelection());
        return;
    }
    OpenServerSelection();
}

private void OpenServerSelection()
{
    var scene = GD.Load<PackedScene>("res://Scenes/Lobby/ServerSelectionDialog.tscn");
    var dlg = (LanConnectServerSelectionDialog)scene.Instantiate();
    dlg.ServerChosen += addr => OpenLobbyWith(addr);
    GetTree().Root.AddChild(dlg);
    dlg.PopupCentered();
}

private void OpenLobbyWith(string baseUrl)
{
    LanConnectConfig.LobbyBaseUrl = baseUrl;
    LanConnectConfig.LastUsedServerAddress = baseUrl;
    LanConnectConfig.Save();
    // ...原有 lobby 初始化流程...
}
```

`AutoConnectLastServer` `LastUsedServerAddress` 在 `LanConnectConfig` 中追加为持久化字段。

- [ ] **Step 3：编译**

```bash
./scripts/build-sts2-lan-connect.sh
```

Expected: 通过。

- [ ] **Step 4：手工烟测（启动游戏 + mod）**

游戏启动后应弹出"选择服务器"对话框；选定后进入 lobby；下次启动若已勾选"自动连接"则倒计时 3 秒后默认连上次。

- [ ] **Step 5：commit**

```bash
git add sts2-lan-connect/Scripts/Entry.cs sts2-lan-connect/Scripts/LanConnectConfig.cs
git commit -m "feat(p3): replace auto-load with server selection dialog at startup"
```

---

### Task P3.9：连接成功后二次发现

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyDirectoryClient.cs`（追加 GetPeersAsync）
- Modify: 调用 lobby 成功连接的现行入口（见 `LanConnectLobbyApiClient` 或其包装层）

- [ ] **Step 1：在 directory client 加 GetPeersAsync**

```csharp
public static async Task<List<CfServerEntry>> GetPeersAsync(string lobbyBaseUrl, CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(lobbyBaseUrl)) return new List<CfServerEntry>();
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var resp = await client.GetAsync($"{lobbyBaseUrl.TrimEnd('/')}/peers", ct);
        if (!resp.IsSuccessStatusCode) return new List<CfServerEntry>();
        string text = await resp.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("peers", out var peers)) return new List<CfServerEntry>();
        var result = new List<CfServerEntry>();
        foreach (var p in peers.EnumerateArray())
        {
            result.Add(new CfServerEntry
            {
                Address = p.GetProperty("address").GetString() ?? "",
                PublicKey = p.TryGetProperty("publicKey", out var pk) ? (pk.GetString() ?? "") : "",
                DisplayName = p.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
                LastSeen = p.TryGetProperty("lastSeen", out var ls) ? (ls.GetString() ?? "") : "",
            });
        }
        return result;
    }
    catch
    {
        return new List<CfServerEntry>();
    }
}
```

- [ ] **Step 2：在连接成功后调用并合并入本地缓存**

定位 lobby 连接成功的回调（搜索 `LobbyBaseUrl` 被设置之后的成功逻辑），追加：

```csharp
async Task ExpandPeerCacheAsync(string lobbyBaseUrl)
{
    var peers = await LanConnectLobbyDirectoryClient.GetPeersAsync(lobbyBaseUrl);
    var cache = LanConnectKnownPeersCache.Load();
    var byAddr = cache.ToDictionary(c => c.Address, StringComparer.OrdinalIgnoreCase);
    foreach (var p in peers)
    {
        if (byAddr.ContainsKey(p.Address)) continue;
        byAddr[p.Address] = new KnownPeerEntry
        {
            Address = p.Address,
            DisplayName = p.DisplayName,
            LastSeenInListing = p.LastSeen,
            DiscoveredVia = $"peer:{lobbyBaseUrl}",
        };
    }
    LanConnectKnownPeersCache.Save(byAddr.Values);
}
```

并在连接成功后 `_ = ExpandPeerCacheAsync(lobbyBaseUrl);`（fire-and-forget，参考 `AGENTS.md` 中 `TaskHelper.RunSafely(...)` 风格）。

- [ ] **Step 3：编译**

```bash
./scripts/build-sts2-lan-connect.sh
```

Expected: 通过。

- [ ] **Step 4：commit**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectLobbyDirectoryClient.cs sts2-lan-connect/Scripts/Lobby/
git commit -m "feat(p3): expand local peer cache via /peers after lobby connect (second discovery)"
```

---

### Task P3.10："重置本地服务器列表"应急按钮

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectServerSelectionDialog.cs`（在弹窗增按钮）
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectKnownPeersCache.cs`（增 `Reset()` 方法）

- [ ] **Step 1：缓存类增 Reset**

在 `LanConnectKnownPeersCache` 内追加：

```csharp
public static void Reset()
{
    lock (Sync) { if (File.Exists(PathFile)) File.Delete(PathFile); }
}
```

- [ ] **Step 2：弹窗 UI 加按钮**

在 .tscn 的 HBoxContainer 内追加 `Button (unique name: ResetButton, text "重置本地列表")`。在脚本 `_Ready` 内绑：

```csharp
GetNode<Button>("%ResetButton").Pressed += () =>
{
    LanConnectKnownPeersCache.Reset();
    _ = RefreshAsync();
};
```

- [ ] **Step 3：编译**

```bash
./scripts/build-sts2-lan-connect.sh
```

Expected: 通过。

- [ ] **Step 4：commit**

```bash
git add sts2-lan-connect/Scripts/Lobby/
git commit -m "feat(p3): add 'reset local server list' recovery button to selection dialog"
```

---

### Task P3.11：bump 客户端版本到 0.3.0

**Files:**
- Modify: `sts2-lan-connect/sts2_lan_connect.json`（mod manifest）
- Modify: `sts2-lan-connect/Scripts/LanConnectConstants.cs`（如 ClientVersion 写在此）
- Modify: `README.md`（顶部 client badge）

- [ ] **Step 1：修改各处版本号**

在 manifest / constants / README 把 `0.2.3` 替换为 `0.3.0`。具体路径以仓库现有 0.2.3 出现位置为准（`grep -rn "0\.2\.3" .` 定位）。

- [ ] **Step 2：commit**

```bash
git add -A
git commit -m "chore(p3): bump client mod to v0.3.0"
```

---

## 第 P4 章 · 集成验证（不做 TDD 单测，做 e2e 烟测）

### Task P4.1：本地 e2e harness

**Files:**
- Create: `scripts/dev-e2e-harness.sh`

- [ ] **Step 1：写脚本**

```bash
#!/usr/bin/env bash
# scripts/dev-e2e-harness.sh
# 起 1 个 Worker (wrangler dev) + 3 个 lobby v0.3 + 1 个 sidecar，输出端点。
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cleanup() { kill $(jobs -p) 2>/dev/null || true; }
trap cleanup EXIT

cd "$ROOT_DIR/cf-worker" && npm run dev &
sleep 2

CF_URL="http://127.0.0.1:8787"
echo "CF Worker: $CF_URL"

cd "$ROOT_DIR/lobby-service" && npm run build

PORT_A=18001 PORT_B=18002 PORT_C=18003

PEER_SELF_ADDRESS="http://127.0.0.1:$PORT_A" PEER_STATE_DIR="/tmp/peer-A" \
PEER_CF_DISCOVERY_BASE_URL="$CF_URL" PORT="$PORT_A" \
node "$ROOT_DIR/lobby-service/dist/server.js" &
PEER_SELF_ADDRESS="http://127.0.0.1:$PORT_B" PEER_STATE_DIR="/tmp/peer-B" \
PEER_CF_DISCOVERY_BASE_URL="$CF_URL" PORT="$PORT_B" \
node "$ROOT_DIR/lobby-service/dist/server.js" &
PEER_SELF_ADDRESS="http://127.0.0.1:$PORT_C" PEER_STATE_DIR="/tmp/peer-C" \
PEER_CF_DISCOVERY_BASE_URL="$CF_URL" PORT="$PORT_C" \
node "$ROOT_DIR/lobby-service/dist/server.js" &

cd "$ROOT_DIR/sts2-peer-sidecar" && npm run build
LOBBY_PUBLIC_BASE_URL="http://127.0.0.1:$PORT_A" PEER_LISTEN_PORT=18800 \
PEER_STATE_DIR="/tmp/sidecar" PEER_CF_DISCOVERY_BASE_URL="$CF_URL" \
node "$ROOT_DIR/sts2-peer-sidecar/dist/index.js" &

echo ""
echo "Endpoints:"
echo "  CF Worker  : $CF_URL"
echo "  Lobby A    : http://127.0.0.1:$PORT_A"
echo "  Lobby B    : http://127.0.0.1:$PORT_B"
echo "  Lobby C    : http://127.0.0.1:$PORT_C"
echo "  Sidecar    : http://127.0.0.1:18800"
echo ""
wait
```

```bash
chmod +x scripts/dev-e2e-harness.sh
```

- [ ] **Step 2：手工跑 + 验证场景**

```bash
./scripts/dev-e2e-harness.sh
```

打开第二个终端，手工跑 spec §7 acceptance 列出的 6 个场景：

```bash
# 1. 客户端 boot 看到 CF 列表
curl -s http://127.0.0.1:8787/v1/servers

# 2. 任意一台 lobby /peers 能看到其它两台
curl -s http://127.0.0.1:18001/peers

# 3. sidecar 能与 lobby A 共存（不同端口）
curl -s "http://127.0.0.1:18800/peers/health?challenge=hi"

# 4. kill 一台 lobby，等 24h（或手工修改它的 lastSeen 至 24h 前），观察其它 peer 标记 offline
kill <lobby-A-pid>
# 在另一台 lobby 的 peers.json 里手工把 lobby-A 的 lastSeen 改到 25h 前，重启该 peer
```

每个场景手工 ✅。

- [ ] **Step 3：commit**

```bash
git add scripts/dev-e2e-harness.sh
git commit -m "chore(p4): add local e2e dev harness for cf+lobby+sidecar"
```

---

### Task P4.2：客户端 e2e（手工）

- [ ] **Step 1：跑 e2e harness 起后端**

```bash
./scripts/dev-e2e-harness.sh
```

- [ ] **Step 2：打包客户端指向本地 e2e**

```bash
STS2_LOBBY_DEFAULT_BASE_URL="http://127.0.0.1:18001" \
STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL="http://127.0.0.1:8787" \
STS2_LOBBY_SEEDS_FILE="$PWD/data/seeds.json" \
./scripts/package-sts2-lan-connect.sh
```

安装到本机游戏后启动，验证：

- [ ] 弹窗在 5 秒内出现
- [ ] 列表至少有 3 个 lobby + 1 个 sidecar
- [ ] 选定一个连接成功；连接后再次启动看到 known_peers.json 被扩充
- [ ] kill CF Worker 进程后，再次启动客户端，弹窗仍能从本地缓存 + hardcoded 种子展示列表
- [ ] "重置本地列表"按钮可清空本地缓存

如全部 ✅，进入 P5。

---

## 第 P5 章 · 协调与发布

> 以下任务为运营动作，不强制 TDD。每个 step 完成手工 ✅ 即可。

### Task P5.1：更新部署文档

- [ ] **Step 1：补充 lobby-service v0.3 升级章节到 `docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`**

需写明：
- 必须设置环境变量 `PEER_SELF_ADDRESS=https://<lobby-public-url>`
- 推荐设置 `PEER_CF_DISCOVERY_BASE_URL=https://<cf-domain>`
- 升级步骤：拉新版 → `npm ci && npm run build` → `systemctl restart lobby-service`

- [ ] **Step 2：新增 sidecar 部署文档 `docs/STS2_PEER_SIDECAR_GUIDE_ZH.md`**

照抄 spec §5.3 的 3 行安装命令 + env 文件示例。

- [ ] **Step 3：commit**

```bash
git add docs/
git commit -m "docs(p5): document v0.3 lobby upgrade and peer sidecar install"
```

---

### Task P5.2：发布 release 产物

- [ ] **Step 1：跑全套打包**

```bash
./scripts/package-lobby-service.sh
./scripts/package-sts2-peer-sidecar.sh
./scripts/package-sts2-lan-connect.sh
```

- [ ] **Step 2：检查 `releases/` 三处产物均生成**

```bash
ls -la releases/sts2_lobby_service/
ls -la releases/sts2_peer_sidecar/
ls -la releases/sts2_lan_connect/
```

- [ ] **Step 3：sync 到 public release repo（若适用）**

```bash
./scripts/sync-release-repo.sh --repo-dir <public-repo-clone>
```

- [ ] **Step 4：commit + tag**

```bash
git commit -am "release: v0.3.0 — decentralized lobby discovery"
git tag v0.3.0
```

---

### Task P5.3：通知服主升级 + 公告横幅

- [ ] **Step 1：在即将下线的 server-registry 公告系统挂"将下线"横幅**

通过现有 `/server-admin` 面板配置一条公告：

```
【重要】当前中心服务器将于 2026-05-18 关闭。请尽快升级到客户端 v0.3，
   下载地址：https://<your-domain>/release。
   服主请升级 lobby-service 到 v0.3.0，或安装 peer sidecar 接入新发现网络。
```

- [ ] **Step 2：私下联系已知 6 位服主并发送升级指南链接**

逐一 IM/邮件，附 deployment 文档链接与 sidecar tarball 下载位置。

- [ ] **Step 3：观察 24h 后 CF `/v1/servers` 看到的活跃数**

```bash
curl -s "https://<your-domain>/v1/servers" | jq '.servers | length'
```

期望：D-1 时已有 ≥3 台 lobby 可见。

---

## 第 P6 章 · D 日

### Task P6.1：D 日 pre-flight

- [ ] **Step 1：30 分钟前过一遍 spec §7 验收清单**
- [ ] **Step 2：监控 CF Worker 健康**

```bash
watch -n 30 'curl -s "https://<your-domain>/v1/servers" | jq ".servers | length"'
```

- [ ] **Step 3：旧服务器停机后，确认客户端流量切到 peer 网络**

如发现客户端连接率显著下滑，立即在 CF KV 直写补充 `peers:seeds`，触发客户端使用 hardcoded 之外的兜底。

---

## 第 P7 章 · 善后（D+1 → D+7）

### Task P7.1：bug triage

- [ ] **Step 1：开 GitHub issue label `v0.3-rollout`**
- [ ] **Step 2：每天扫一次 CF Worker logs（`wrangler tail`）观察异常**

### Task P7.2：CF /v1/submit 表单（如 P0 时被减法砍掉）

- [ ] **Step 1：实现 Worker 路由 `POST /v1/submit`** 接受 Turnstile token + 服主表单字段
- [ ] **Step 2：Worker 验证 Turnstile API**：参考 `https://challenges.cloudflare.com/turnstile/v0/siteverify`
- [ ] **Step 3：Worker 转发到 `peers:seeds` 中随机 2 台 peer 的 `/peers/announce`**
- [ ] **Step 4：CF Pages 上托管 HTML 表单**（直接写在 Worker 内或独立 Pages 项目）

具体代码与测试可在 D+7 之后另起一份 spec/plan。

---

## 自审

**Spec 覆盖检查（spec §1–§9 vs plan 任务）：**

| spec 章节 | 实现任务 |
|---|---|
| §4 总体架构 | 由 P0+P1+P2+P3 全套覆盖 |
| §5.1 客户端 v0.3 | P3.1–P3.11 |
| §5.1.5 降级表现 | P3.6 (5s 超时合并) + P3.10 (重置按钮) |
| §5.2 lobby-service v0.3 | P1.1–P1.12 |
| §5.2.2 gossip 节奏 | P1.8 |
| §5.2.4 老 SERVER_REGISTRY 跳过 | P1.11 step 4 |
| §5.3 sidecar | P2.1–P2.4 |
| §5.4 CF Worker + KV | P0.1–P0.8 |
| §5.5 信任反垃圾 | P1.6 (probe + rate limit) + P0.7 (cron 不写空表) |
| §6 迁移计划 | P5+P6+P7 章节对齐 P0–P7 阶段 |
| §7 验收清单 | P4.2 客户端 e2e + P5.3 24h 观察 |
| §8.1 hardcoded 种子刷新 | P0.1 + P3.2 |
| §8.2 ed25519 + TOFU | P1.1 (identity) + P1.6 (probe with expectedKey) + §5.2.2 TTL |
| §8.3 JSON 持久化 | P1.3 + P3.3 |
| §8.4 HTTP HEAD ping | P3.5 + P3.6 |

无 spec 章节遗漏。

**Placeholder 扫描：** plan 内出现的 `<your-domain>` `<your-cf-domain>` 是部署期占位，明示需在 P0 部署时填入实际值——不算 plan 失败。`<lobby-A-pid>` 在 P4.1 step 2 是手工流程占位（操作者自行查 PID）。其余无 TBD/TODO。

**类型一致性：** `PeerRecord` (P1.2) 与 `PeerStore` (P1.3) 字段命名一致；C# `KnownPeerEntry` (P3.3) 字段命名一致（与 TS 端是不同对象——客户端缓存的本地概念，不是协议 DTO）；`PeersListResponse` (P1.2) 与 P1.5 处使用一致。

**已知执行注意：**
- P2.2 `package.json` exports 路径对 ES2022 module 要求严格，若 sidecar 编译报 `Cannot find module`，把 `exports` 改为 `"./peer/*": { "import": "./dist/peer/*.js", "types": "./dist/peer/*.d.ts" }`。
- P3.2 jq 拼接需匹配现有脚本结构——若现有脚本不是用 jq 而是 heredoc 拼字符串，按原模式追加字段而非替换为 jq。
- Godot 场景文件（P3.7）无法纯命令行创建；该步骤明示需 Godot 编辑器介入。

---

**Plan 终。** 实现可以开始，建议执行节奏照 `docs/superpowers/specs/2026-05-08-decentralized-lobby-discovery-design.md` §6.1 的 P0–P7 时间表。
