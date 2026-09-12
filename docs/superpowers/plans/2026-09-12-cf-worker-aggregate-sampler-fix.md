# cf-worker aggregate sampler fix (2026-09-12)

## 背景（必须先读）

`cf-worker/src/cron/aggregate.ts` 是 Cloudflare Worker `sts2-discovery` 的定时聚合器（每 10 分钟一次）。它从一组"采样节点"拉 `GET <addr>/peers`，合并成公共服务器列表写入 KV `peers:active`，客户端通过 `/v1/servers` 读取。

线上事故（已定位）：
1. **Cloudflare Workers 的 `fetch()` 无法访问裸 IP 源站**。任何 `http://1.2.3.4:8787/...`、`https://1.2.3.4:8443/...`（任何端口、任何协议）都会被 Cloudflare 自己在 ~2ms 内以 HTTP 403 + 响应体 `error code: 1003` 拒绝，请求永远到不了源站。域名地址（如 `https://sts2.gia.dmit.icu.ng`、`http://lt.syx2023.icu:52000`）正常。
2. 现在的采样集 = `seeds.slice(0,5)` ∪ `previous.slice(0,5)`。当这些地址全是 IP 或已下线时，一个都拉不到，`merged` 为空。
3. 24 小时保留期过后 `previous` 全部被丢弃，`merged.size === 0 && previous.length > 0` 直接 return，KV 永远不再更新（线上列表从 2026-08-30 冻结到 2026-09-12）。它无法自愈。
4. 另外 `fetchWantsPublicListing()` 对 IP 节点也会撞 1003，返回 null 被当作"保留"，白白消耗 subrequest。

## 目标

只修改 `cf-worker/src/cron/aggregate.ts` 和 `cf-worker/src/cron/aggregate.test.ts`。要求：

### A. 采样节点选择（核心）
- 新增辅助函数 `isIpLiteralAddress(address: string): boolean`：用 `new URL(address).hostname` 判断是否为 IPv4 字面量（如 `47.97.126.98`）或 IPv6 字面量（URL 的 hostname 会带方括号 `[...]`）。解析失败视为 `true`（不可采样）。
- 采样候选池 = 全部 seeds ∪ 全部 previous（不再只取前 5 条），**只保留非 IP 字面量的地址**，去重。
- 从候选池中最多选 `SAMPLER_MAX = 8` 个：seeds 中的域名地址优先全部纳入（seeds 通常很少），剩余名额从 previous 的域名地址里随机选（避免每轮总是同几台）。
- IP 字面量地址永远不作为采样节点去 fetch（它们通过域名节点的 `/peers` 列表间接进入公共列表，这一点必须保持——测试要覆盖："域名采样节点返回的列表里包含 IP 地址的 peer，该 peer 仍然出现在最终 `peers:active` 中"）。

### B. 公开开关检查减少无效 subrequest
- `fetchWantsPublicListing` 只对非 IP 字面量地址调用；IP 字面量地址跳过检查、视为保留（行为和现在一样，只是不再发请求）。

### C. 死锁保护
- 保留"merged 为空且 previous 非空时不覆盖旧文档"的保护（不要把列表清空）。
- 但 previous 中的条目在保留期内（`OFFLINE_RETENTION_MS`）继续保留的逻辑不变。
- 现有测试全部必须继续通过；不要改变 `ActiveServersDocument` 的输出格式。

### D. 测试（用 node:test，沿用 `aggregate.test.ts` 里现有的 FakeKV / fetchMock 风格）
至少新增：
1. `previous` 有 7 条，前 5 条都是 IP 字面量、第 6/7 条是域名：断言 fetchMock 收到了对第 6/7 条域名的 `/peers` 请求，并且**没有**对任何 IP 字面量地址发出 `/peers` 请求。
2. 域名采样节点返回的 peers 里含有 `http://10.0.0.5:8787`：断言最终 `peers:active` 包含它，且 fetchMock 没有收到对 `http://10.0.0.5:8787/peers/metrics` 的请求。
3. `isIpLiteralAddress` 单测：`http://47.97.126.98:8787` → true，`https://[2001:db8::1]:8443` → true，`https://sts2.gia.dmit.icu.ng` → false，`http://lt.syx2023.icu:52000` → false，`not a url` → true。

## 完成标准
在 `cf-worker/` 目录下：
```
npm run build   # tsc --noEmit 无错误
npm test        # 全部通过（包括原有测试）
```

## 约束
- 不要 `git commit`，不要 `git push`。
- 不要运行 `wrangler deploy` / `npm run deploy`，不要碰 `wrangler.toml`、`scripts/`、`src/handlers/**`、`src/index.ts`、`src/types.ts`。
- 不要修改 `cf-worker/` 以外的任何文件。
- `node_modules` 已安装，不要重新 `npm install`。
- 用中文写一段简短汇报：改了哪些文件、`npm run build` 和 `npm test` 的结果原文（最后几行）。
