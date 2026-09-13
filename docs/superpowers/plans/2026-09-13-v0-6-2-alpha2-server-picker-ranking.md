# v0.6.2-alpha.2 选服列表排序重做（版本优先 + 真实延迟）

- 日期：2026-09-13
- 目标版本：`0.6.2-alpha.2`（客户端 MOD 与 lobby-service 同步）
- 状态：**第 4 版，定稿**。Codex（gpt-6-astra, high）第 3、4 轮均 `APPROVE`，Claude 复核同意，交付 Opencode 实施 §9。
- 流程：Claude 起草 → Codex 评审 → 双方无异议 → Opencode 实施（§9）→ Codex 做 E2E（§11）

---

## 1. 背景与已核实事实

以下事实均于 2026-09-12/13 在代码或线上核实（Codex 第 1 轮评审复核通过）。实施者若发现与代码不符，必须停下报告。

### 1.1 现在的排序完全在客户端

`sts2-lan-connect/Scripts/Lobby/LanConnectServerListBootstrap.cs` 的 `OrderForDisplay`：

```csharp
entries
    .OrderByDescending(entry => entry.IsPinned)
    .ThenByDescending(entry => entry.LastSuccessConnect ?? DateTime.MinValue)
    .ThenBy(entry => entry.Bucket)          // Low(<=500ms) Mid(<=2000ms) High Unreachable
    .ThenBy(entry => entry.Address, StringComparer.OrdinalIgnoreCase);
```

- Cloudflare Worker `/v1/servers` 返回的顺序不参与排序。**本次不改 Worker。**
- `LastSuccessConnect` 是死条件：生产代码从不写入 `KnownPeerEntry.LastSuccessConnect`，只可能从旧文件读入。
- 同一延迟档内按地址字符串排，不是按毫秒。
- `ServerListEntry.Bucket` 默认值是 `Unreachable`，探测完成前无法区分“还没测”与“测不通”。
- `LanConnectServerSelectionDialog.RefreshAsync` 每收到一批结果都调用 `Render()` 全量重排，列表会跳动。
- `RefreshAsync` 顺序 `await cfDiscoveryTask; await pingTask;`；若 CF 任务抛异常会直接进入异常分支，初始探测可能仍在运行。
- 延迟来自 `LanConnectPeerPing.ProbeAsync`：每次 `new HttpClient`，只发一次 `/peers/health`（失败才回退 `/probe`），耗时包含建连。
- 选服框的“手动输入”直接 `ChooseServer` 并关闭对话框，**不会**把地址加入候选列表（`LanConnectServerSelectionDialog.OnManualConnect`）。
- 候选来源只有 `known_peers.json` 缓存、内置种子、置顶服，以及 CF 列表；`GatherInitialCandidates` 读取时会清理并**整体重写** `known_peers.json`。
- 选择服务器会在连接成功前持久化 `config.json` 中的 `LobbyServerBaseUrl` 与 `LastUsedServerAddress`。
- 本机客户端数据目录：`~/Library/Application Support/SlayTheSpire2/sts2_lan_connect/`（`config.json`、`known_peers.json` 均已存在）。

### 1.2 0.6.x 客户端无法使用 0.6.0 以前的 lobby-service

- `LanConnectHostFlow.cs`（建房、续局发布）与 `LanConnectLobbyJoinFlow.cs`（加入）在响应缺少 `protocolSelection` 时直接抛 `lan_protocol_version_mismatch`。
- `protocolSelection` 由 0.6.0 起的服务端提供。对 0.6.2 客户端而言，0.4.x/0.5.x 服务器**不是“排后面”而是“用不了”**。

### 1.3 服务端版本现在怎么拿

- 所有公开 JSON 接口（`/probe`、`/health`、`/peers/health`、`/peers/metrics`）都没有服务端版本号字段。
- 免登录 `/server-admin` HTML 里有 `const serviceVersionLabel = "Lobby Service vX.Y.Z";`，但页面 39KB～173KB，**不采用**。
- `/probe` 在所有版本都存在且很小（11～587 字节），其 capability 字段可推断大版本（首次出现的 tag 已核实）：

| `/probe` 中出现的字段 | 首次出现的版本 | 推断 |
|---|---|---|
| `capabilities.dualProtocolApiVersion` | v0.6.0-alpha.1 | ≥ 0.6.0 |
| `capabilities.modSyncProtocolVersion` | v0.5.1-rc.1 | ≥ 0.5.1 |
| `capabilities.serverChatVersion` | v0.5.0 | ≥ 0.5.0 |
| 只有 `{"ok":true}` | — | ≤ 0.4.x |

- `/peers/metrics` 只在节点网络开启且配置了 `PEER_SELF_ADDRESS` 时挂载（`app.ts:1675`），不能作为唯一版本来源。
- `{"ok":true}` 按当前模型与 `LanConnectJson.Options` 反序列化得到 `Ok=true`、`Capabilities` 为默认非空对象、整数字段为 0（静态核实，实施时需用测试确认）。

### 1.4 线上 `/probe` 真实响应（测试夹具，逐字复制）

`https://sts2.gia.dmit.icu.ng`（v0.4.0）：

```json
{"ok":true}
```

`http://103.39.66.147:8787`（v0.5.1）：

```json
{"ok":true,"capabilities":{"serverChatVersion":1,"roomChatProtocolVersion":1,"richContentVersion":1,"emojiSetVersion":1,"itemRefVersion":1,"combatRefVersion":1,"maxMessageChars":300,"maxSegments":32,"maxEntities":12,"historyLimit":50,"modSyncProtocolVersion":1,"modSyncEnabled":true,"modSyncMinimumClientVersion":"0.5.1"}}
```

`http://47.97.126.98:8787`（v0.6.1）：

```json
{"ok":true,"capabilities":{"serverChatVersion":1,"roomChatProtocolVersion":1,"richContentVersion":1,"emojiSetVersion":1,"itemRefVersion":1,"combatRefVersion":1,"maxMessageChars":300,"maxSegments":32,"maxEntities":12,"historyLimit":50,"modSyncProtocolVersion":1,"modSyncEnabled":true,"modSyncMinimumClientVersion":"0.5.1","wireCacheSignatureV1Enforced":true,"dualProtocolApiVersion":1,"supportedProtocolProfiles":["compat_4_5_v1","tail_v1"],"lanProtocolMin":1,"lanProtocolMax":1,"minimumClientVersion":"0.3.0","tailV1MinimumClientVersion":"0.6.1-alpha.1","tailV1Carrier":"native_bus_v1"}}
```

`http://101.35.217.99:8788`（魔仙堡，v0.6.2-alpha.1）：

```json
{"ok":true,"capabilities":{"serverChatVersion":1,"roomChatProtocolVersion":1,"richContentVersion":1,"emojiSetVersion":1,"itemRefVersion":1,"combatRefVersion":1,"maxMessageChars":300,"maxSegments":32,"maxEntities":12,"historyLimit":50,"modSyncProtocolVersion":1,"modSyncEnabled":true,"modSyncMinimumClientVersion":"0.5.1","wireCacheSignatureV1Enforced":true,"dualProtocolApiVersion":1,"supportedProtocolProfiles":["compat_4_5_v1","tail_v1"],"lanProtocolMin":1,"lanProtocolMax":1,"minimumClientVersion":"0.3.0","tailV1MinimumClientVersion":"0.6.2-alpha.1","tailV1Carrier":"native_bus_v1"}}
```

线上版本分布（2026-09-13）：0.6.2-alpha.1 ×1（魔仙堡）、0.6.1 ×2、0.5.4 ×1、0.5.1 ×5、0.4.0 ×3、读不到 ×2（推断 ≤0.4）。

---

## 2. 目标与非目标

### 目标

1. 魔仙堡（`LanConnectServerListBootstrap.FeaturedServerAddress`）继续置顶，行为不变。
2. 服务端版本越新越靠前，按**大版本档**（major.minor）比较。
3. 同一版本档内按**真实延迟**（精确毫秒）从低到高排。
4. 不可达的服务器永远在可达服务器之后。
5. 0.6.0 以前的服务器在行上明确标注“服务端版本过旧”。
6. 列表在一次刷新过程中不来回跳动：中途只更新行内数据，全部结果到齐后只重排一次。
7. 服务端新增公开 `serviceVersion` 字段，未升级的服务器靠 `/probe` 推断继续参与排序。

### 非目标

- 不改 Cloudflare Worker、不改 KV、不改节点网络/gossip。
- 不禁止点击过旧服务器（只标注）。
- 不改 `known_peers.json` 文件格式；`KnownPeerEntry.LastSuccessConnect` 字段与 `Cleanup` 保留。
- 不改任何 wire 协议、`protocolSelection`、`minimumClientVersion`、`tailV1MinimumClientVersion`。
- 不改用户指南、README、`releases/` 目录、`lobby-defaults.json`。
- 不发布、不打 tag、不推送、不更新创意工坊、不部署任何远程服务器。实施阶段不打包；E2E 阶段 `verify-release.sh --artifacts-only` 在其临时目录内打包校验，属于明确的临时例外（§11）。

---

## 3. 排序规则（规范）

`OrderForDisplay` 改为以下全序，自上而下逐级比较：

1. **置顶**：`IsPinned == true` 排最前。
2. **可达性**：`ProbeState == Reachable` 在前；`Unreachable` 与 `Pending` 在后（二者同组）。
3. **版本档**（降序）：`(Major, Minor)` 越大越前。`Version.Source == Unknown` 视为最低，排在所有已知档之后。
   - 预发布与补丁号不参与档位比较：`0.6.2-alpha.1`、`0.6.1`、`0.6.0` 同属 `0.6` 档。
4. **延迟**（升序，仅可达组）：精确 `PingMs`。
5. **地址**：`Address`，`StringComparer.OrdinalIgnoreCase`，保证确定性。

不可达组内部只使用第 3、5 级。

不设延迟容差档：在“先比档位、再比精确毫秒”的组合下档位不产生任何效果（Codex 评审指出），抖动问题由 §6 的“每轮只重排一次”解决。

删除 `ServerListEntry.LastSuccessConnect` 属性及 `GatherInitialCandidates` 中的赋值，排序不再引用它。`PingBucket` 枚举与 `Bucket` 属性保留但不参与排序。

---

## 4. 服务端版本识别

### 4.1 服务端（lobby-service）新增字段

- `/probe` 的 `capabilities` 新增 `serviceVersion: string`，值为 `app.ts` 已有的 `lobbyServiceVersion`（读 `package.json`，失败为 `"unknown"`）。
- `/peers/metrics` 响应新增 `serviceVersion: string`：
  - `peer/types.ts` 的 `MetricsResponse` 增加 `serviceVersion: string`；
  - `peer/handlers/metrics.ts` 的 `Deps` 增加 `getServiceVersion?: () => string`，缺省输出 `"unknown"`；
  - `app.ts` 调用 `mountMetrics` 时传入 `getServiceVersion: () => lobbyServiceVersion`。
- 版本号本来就在免登录管理页公开，新增字段不扩大信息暴露面。

### 4.2 客户端数据结构

新增 `Scripts/Lobby/LanConnectServerVersionInfo.cs`：

```csharp
internal enum ServerVersionSource { Unknown, Inferred, Reported }
internal enum ServerProbeState { Pending, Reachable, Unreachable }

internal sealed record ServerVersionInfo(
    ServerVersionSource Source,
    int Major,
    int Minor,
    string Display)   // "0.6.1"、"0.5.x（推断）"、"0.4.x 或更早（推断）"、"未知"
{
    public static readonly ServerVersionInfo Unknown = new(ServerVersionSource.Unknown, 0, 0, "未知");
    public bool IsKnown => Source != ServerVersionSource.Unknown;
    // 仅在版本已知且 < 0.6 时为 true；Unknown 一律 false
    public bool IsTooOldForThisClient => IsKnown && (Major, Minor).CompareTo((0, 6)) < 0;
}
```

`ServerListEntry` 新增：

```csharp
public ServerVersionInfo Version { get; set; } = ServerVersionInfo.Unknown;
public ServerProbeState ProbeState { get; set; } = ServerProbeState.Pending;
```

### 4.3 解析规则（纯函数，可单测）

新增 `internal static class LanConnectServerVersionResolver`：

1. `ServerVersionInfo? FromReported(string? serviceVersion)`：
   - 去首尾空白后长度 1～64，且匹配 `^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$`；
   - `Major`、`Minor` 用 `int.TryParse` 解析，溢出视为不匹配；
   - 成功 → `Reported`，`Display` 为去空白后的原字符串；否则（含 `"unknown"`、空、null、`"v0.6.1"`）返回 `null`。
   - 不复用 `LanConnectClientVersion.TryParseSupported`（拒绝 `<0.3`、限 32 字节，语义是客户端版本）。
2. `ServerVersionInfo FromProbe(LobbyProbeResponse? probe)`，严格按顺序：
   1. `probe == null` 或 `probe.Ok != true` → `Unknown`；
   2. `probe.Capabilities == null`（JSON 显式 `"capabilities": null`）→ `Unknown`；
   3. `FromReported(probe.Capabilities.ServiceVersion)` 非 null → 用它；
   4. `DualProtocolApiVersion >= 1` → `Inferred(0, 6, "0.6.x（推断）")`；
   5. `ModSyncProtocolVersion >= 1 || ServerChatVersion >= 1` → `Inferred(0, 5, "0.5.x（推断）")`；
   6. 其余（`Ok == true` 且 capabilities 为缺省对象，即历史 `{"ok":true}`）→ `Inferred(0, 4, "0.4.x 或更早（推断）")`。
3. `/probe` 请求失败、非 2xx、非 JSON → `Unknown`。
4. **只用 `/probe` 做推断**。`/peers/metrics` 的 `modSyncProtocolVersion` 等字段不用于推断（0.6 服务器也有，推断会冤枉成 0.5）。

### 4.4 请求编排（快照链路，不含延迟探测）

`LanConnectPeerMetricsClient` 新增：

```csharp
internal sealed record PeerDiscoverySnapshot(PeerMetricsResponse? Metrics, ServerVersionInfo Version);
public static Task<PeerDiscoverySnapshot> FetchSnapshotAsync(string baseUrl, CancellationToken ct = default);
internal static Task<PeerDiscoverySnapshot> FetchSnapshotAsync(string baseUrl, HttpMessageHandler handler, CancellationToken ct = default);
```

编排：

1. `GET /peers/metrics`；失败则 `metrics = null`，继续。
2. `reported = FromReported(metrics?.ServiceVersion)`。
3. 需要 `/probe` 的条件：`reported == null`，**或** `metrics != null && metrics.ModSyncProtocolVersion <= 0`（与现有回退条件 `> 0 才直接返回` 完全一致）。
4. 需要时 `GET /probe` **一次**；同一响应同时用于：
   - `probeVersion = FromProbe(probe)`；
   - 现有 mod-sync 能力回填（仅当 `metrics != null && metrics.ModSyncProtocolVersion <= 0 && probe?.Ok == true && probe.Capabilities != null`；显式 `capabilities: null` 时不回填，保留原 metrics 与 Reported）。
5. **最终版本**：`reported ?? probeVersion ?? ServerVersionInfo.Unknown`。`/probe` 失败或给出较旧推断，**永远不得覆盖** `reported`。
6. 快照链路每台服务器每轮最多 1 次 `/peers/metrics`、最多 1 次 `/probe`（§5 延迟探测的请求另计）。

现有 `FetchAsync(...)` 保留签名，内部改为 `FetchSnapshotAsync(...).Metrics`，现有测试不应失效。

模型改动：

- `PeerMetricsResponse` 新增 `[JsonPropertyName("serviceVersion")] public string? ServiceVersion { get; set; }`。
- `LobbyProbeCapabilities` 新增 `public string? ServiceVersion { get; set; }`。
- `LobbyProbeResponse.Capabilities` 保留默认值 `= new()`（JSON 缺该字段时非 null，对应历史 `{"ok":true}`）；JSON 显式 `null` 时为 null，解析器与回填均须判空。可把属性类型标为可空，但不得去掉默认值，以保证“缺字段”与“显式 null”处理不同。

---

## 5. 延迟测量

`LanConnectPeerPing.ProbeAsync`：

- 新增 `internal static Task<PeerProbeResult> ProbeAsync(string baseUrl, HttpMessageHandler handler, CancellationToken ct)` 以便测试。
- 每台服务器本次探测使用**同一个** `HttpClient`。
- Tier 1（`/peers/health`）：第一次成功后再发第二次；`PingMs = min(成功样本)`。第二次失败则用第一次。第一次失败（异常或非 2xx）→ 不发第二次，走 Tier 2。
- Tier 2（`/probe`）：同样规则，最多两次，取最小值。
- 两级都失败 → `Ms = -1`，`Bucket = Unreachable`。
- 单请求超时仍为 5 秒。
- `displayName` 取第一次成功响应中的值。
- **取消**：调用方 `ct` 已取消时，不再发送任何后续样本或回退请求，立即返回 `(-1, Unreachable, null)`（保持现有“不抛出”的契约）。`HttpClient` 自身超时（`ct` 未取消）按普通失败处理，照常走回退规则。

`LanConnectServerListBootstrap.PingAllAsync`：延迟探测与 `FetchSnapshotAsync` 并行；完成后设置 `PingMs`、`ProbeState = Reachable/Unreachable`、`entry.Version = snapshot.Version`；`snapshot.Metrics` 非 null 时照旧 `ApplyMetrics`。

---

## 6. 列表稳定性（不跳动）

新增 `internal static class LanConnectServerListDisplayOrder`：

```csharp
// resort=true：按 §3 全量排序。
// resort=false：previousOrder 中仍存在的地址保持原相对顺序；新出现的条目按 §3 排序后追加在末尾；已消失的地址剔除。
internal static List<ServerListEntry> Apply(
    IReadOnlyList<string> previousOrder, IEnumerable<ServerListEntry> entries, bool resort);
```

地址比较一律 `Trim().TrimEnd('/')` + `OrdinalIgnoreCase`（与 `NormalizeAddress` 一致，可改为 `internal` 复用）。

对话框行为：

- 对话框持有 `List<string> _displayOrder`。
- `Render(bool resort)`：调用 `Apply(_displayOrder, _entries, resort)`，**每次渲染后都把返回列表的规范化地址写回 `_displayOrder`**（无论 `resort` 真假）。
- `RefreshAsync`：
  1. 初始候选渲染：`Render(resort: true)`（此时全部 `Pending`）。
  2. CF 合并后、每批探测完成后：`Render(resort: false)`。
  3. `await Task.WhenAll(cfDiscoveryTask, pingTask)` 汇合（两个任务都结束后才继续，含其中一个抛异常的情况），通过 `CanApplyRefresh` 后 `Render(resort: true)` **只重排一次**。
  4. `OperationCanceledException` 与异常分支保持现有语义；异常分支同样必须在两个任务都结束后才 `Render(resort: true)`。
- 日志（供 E2E 自动核对）。所有行都带窗口与轮次标识；字符串由纯函数生成（放在 `LanConnectServerListDisplayOrder` 或同级类中），并有 xUnit 覆盖：
  - **窗口标识**：每个 `LanConnectServerSelectionDialog` 实例在构造时从进程内静态自增计数器取 `dialogId`（从 1 开始，`Interlocked.Increment`）。`generation` 沿用现有 `_refreshGeneration`（每个窗口从 1 开始）。
  - **轮次事件**（每轮每种状态最多一行）：

    ```
    sts2_lan_connect server picker refresh: dialog=<D> generation=<N> state=<started|completed|canceled|failed>
    ```

    - `started`：`BeginRefresh` 之后立即记录；
    - `completed`：正常路径最终 `Render(resort: true)` 与 `order:` 快照之后记录；
    - `canceled`：捕获 `OperationCanceledException`，或任一 `CanApplyRefresh` 为 false 的提前返回点（同一轮只记录一次）；
    - `failed`：异常分支渲染之后记录。
  - **每次渲染**：

    ```
    sts2_lan_connect server picker render: dialog=<D> generation=<N> resort=<true|false> count=<K> order=<addr1>,<addr2>,...
    ```

  - **最终排序快照**（仅正常完成的轮次，紧接最终 `render:` 之后、`completed` 之前；**全量输出，不截断**）：

    ```
    sts2_lan_connect server picker order: dialog=<D> generation=<N> count=<K> items=1=http://101.35.217.99:8788|pinned|0.6.2-alpha.1|reachable|38ms; 2=...
    ```

    每项格式：`序号=地址|pinned 或 -|Version.Display|reachable 或 unreachable 或 pending|NNms 或 -`，项间以 `; ` 分隔。地址序列必须与同轮最终 `render:` 的 `order=` 完全一致。
  - **窗口关闭**：对话框 `_ExitTree` 时记录一行 `sts2_lan_connect server picker closed: dialog=<D>`。
- 实施提示（Codex 第 3 轮核实）：
  - `canceled` 去重必须按**任务捕获的** `refreshGeneration` 记录（如 `HashSet<int>` 或按轮次的状态对象）；不得使用当前 `_refreshGeneration` 标记旧任务，也不得用随新轮重置的单个布尔值。父刷新、CF 分支、探测分支的提前返回点共享这份记录。
  - `_ExitTree` 已存在（`LanConnectServerSelectionDialog.cs` 约 72 行，调用 `CancelRefresh()`），不要新增第二个覆写；在其中 `CancelRefresh()` 之后记录 `closed:`，保留基类调用。
  - 异步分支可能在 `closed:` 之后补记同轮 `canceled`，属预期；§11.3 只禁止关闭后再出现 `render:`。

---

## 7. UI 标注

`BuildServerRow` 中：

- `entry.Version.IsTooOldForThisClient == true` 时，在现有徽章同一行渲染 `Label`，`Name = "ServiceTooOldBadge"`，文本 `服务端版本过旧`，颜色 `DangerColor`，风格参照 `ModSyncSupportBadge`。
- 行 tooltip 追加 `服务端版本：{Version.Display}`；过旧时再追加 `该服务器的 lobby-service 低于 0.6.0，当前客户端无法在此创建或加入房间。`
- `Unknown` 与 `≥0.6` 不渲染该徽章。
- 不改变既有布局断言（`CustomMinimumSize.Y >= 88`）。

---

## 8. 版本号 0.6.2-alpha.2

### 8.1 改为 `0.6.2-alpha.2`（发布版本号）

| 文件 | 位置 |
|---|---|
| `lobby-service/package.json` | `version` |
| `lobby-service/package-lock.json` | 顶层 `version` 与 `packages[""].version` |
| `sts2-lan-connect/sts2_lan_connect.json` | `version` |
| `sts2-lan-connect/sts2_lan_connect.csproj` | `<Version>`、`<InformationalVersion>`（`<AssemblyVersion>0.6.2.0` 不变） |
| `sts2-lan-connect/Scripts/Lobby/ModSync/LanConnectModPreflightCoordinator.cs` | `ProtocolOffer` 默认初始化值中的客户端版本字符串 |
| `sts2-lan-connect.Tests/Protocol/LanConnectBuildInfoTests.cs` | 期望 canonical 版本 |
| `sts2-lan-connect.Tests/Packaging/LanConnectPackageContentTests.cs` | 约 50、54 行 manifest / 程序集版本断言 |
| `lobby-service/src/package-content.test.ts` | 约 266-268 行与 342-357 行（含测试标题） |

### 8.2 不改（协议最低版本或测试夹具）

- `lobby-service/src/protocol-capabilities.ts` 的 `minimumClientVersion = "0.6.2-alpha.1"`
- `lobby-service/src/app.ts` 的 `tailV1MinimumClientVersion: "0.6.2-alpha.1"`（及测试中 `CURRENT_PROBE_CAPABILITIES` 里的同名值）
- `store.test.ts`、`app.integration.test.ts`、`protocol-capabilities.test.ts`、`LanConnectCapabilityDigestTests.cs` 中作为客户端版本夹具的 `0.6.2-alpha.1`
- `docs/RELEASE_NOTES_V0.6.2_ALPHA1_ZH.md`；`LanConnectPackageContentTests.Client_v062_alpha1_documents_peer_addressed_type_id_candidate` 除 §8.4 指定的一条过期断言外不改

理由：alpha.2 不改 wire 协议，alpha.1 客户端仍应能进入 tail 房间。

### 8.3 文档

- `CHANGELOG.md`：在 `## [Unreleased]` 与 `## [0.6.2-alpha.1]` 之间新增 `## [0.6.2-alpha.2] - 2026-09-13`（Added / Changed / Compatibility），注明 pre-release、正式版仍为 `0.6.1`。
- 新增 `docs/RELEASE_NOTES_V0.6.2_ALPHA2_ZH.md`，结构参照 alpha1，必须包含：`0.6.2-alpha.2`、`pre-release`、`<待打包后填写>`、`serviceVersion`、`服务端版本过旧`、`魔仙堡`。
- `LanConnectPackageContentTests` 新增 `Client_v062_alpha2_documents_server_picker_ranking`，断言上述文本存在于 release notes，且 changelog 含 `## [0.6.2-alpha.2] - `。

### 8.4 基线既有失败（实施前已存在，必须一并修正）

2026-09-13 在未改动的 `main`（`42cd5c3`）上运行基线：

| 检查 | 结果 |
|---|---|
| `cd lobby-service && npm test` | 611 通过，0 失败 |
| `dotnet build sts2-lan-connect/sts2_lan_connect.csproj -c Release` | 成功 |
| `dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj` | 1171 通过，**1 失败**，1 跳过 |

失败用例：`LanConnectPackageContentTests.Client_v062_alpha1_documents_peer_addressed_type_id_candidate`，断言 `releaseNotes` 包含 `<待打包后填写>`。原因：提交 `279549b` 已在 `docs/RELEASE_NOTES_V0.6.2_ALPHA1_ZH.md` 的 `## 校验和` 段填入客户端与服务端 ZIP 的 SHA-256，但未同步测试。

修正方式（只改这一条断言，其余断言保持不变；不改 alpha1 发布说明）：

- 删除 `Assert.Contains("<待打包后填写>", releaseNotes, ...)`；
- 改为断言 `releaseNotes` **不含** `<待打包后填写>`、包含 `## 校验和`，并用正则各匹配一次：
  - `客户端 ZIP SHA-256：` 后接反引号包裹的 64 位小写十六进制；
  - `服务端 ZIP SHA-256：` 后接反引号包裹的 64 位小写十六进制。

约定：alpha2 发布说明按惯例先写 `<待打包后填写>`（§8.3），将来打包填入校验和时必须在同一提交里把 alpha2 测试改成与本节相同的形式。

---

## 9. 实施范围（交给 Opencode）

### 9.1 允许修改的文件

- `lobby-service/src/app.ts`（仅 `/probe` capabilities 与 `mountMetrics` 调用）
- `lobby-service/src/peer/types.ts`、`lobby-service/src/peer/handlers/metrics.ts`、`metrics.test.ts`
- `lobby-service/src/app.integration.test.ts`：允许给 `CURRENT_PROBE_CAPABILITIES` 增加 `serviceVersion`（其中协议最低版本保持不变）并补断言
- 其他对 `/probe` 响应做整体 `deepEqual` 的现有测试（如 `mod-sync/preflight.integration.test.ts`）：仅允许同步增加 `serviceVersion` 字段
- `lobby-service/src/package-content.test.ts`、`package.json`、`package-lock.json`（仅版本号）
- `sts2-lan-connect/Scripts/Lobby/` 下：`LanConnectServerListBootstrap.cs`、`LanConnectServerSelectionDialog.cs`、`LanConnectPeerMetricsClient.cs`、`LanConnectPeerPing.cs`、`LanConnectLobbyModels.cs`、新增 `LanConnectServerVersionInfo.cs`、新增 `LanConnectServerListDisplayOrder.cs`（名称可调整，须在汇报中说明）
- `sts2-lan-connect/Scripts/Lobby/ModSync/LanConnectModPreflightCoordinator.cs`（仅版本字符串）
- `sts2-lan-connect/sts2_lan_connect.json`、`sts2_lan_connect.csproj`（仅版本号）
- `sts2-lan-connect.Tests/` 与 `sts2-lan-connect.GdUnitTests/` 中相关测试
- `CHANGELOG.md`、新增 `docs/RELEASE_NOTES_V0.6.2_ALPHA2_ZH.md`

新增 `.cs` 文件若所在目录使用 Godot `.uid` 伴随文件，照现有惯例处理。

### 9.2 禁止

- 不 `git commit` / `git push` / `git tag`。
- 不改 `cf-worker/**`、`releases/**`、`lobby-defaults*.json`、用户指南与 README。
- 不运行打包、部署、`wrangler`、`steamcmd` 命令；不改游戏安装目录、`mods/` 与 `~/Library/Application Support/SlayTheSpire2/**`。
- 不改 §8.2 列出的协议最低版本与夹具。

### 9.3 测试要求

**xUnit（`sts2-lan-connect.Tests`）**

1. 排序：置顶即使不可达/过旧仍第一；可达全部在不可达之前；`0.6 > 0.5 > 0.4 > Unknown`；`0.6.2-alpha.1` 与 `0.6.1` 同档由延迟决定；同档按精确毫秒（如 41ms 先于 42ms）；毫秒相同按地址；`Pending` 与 `Unreachable` 同组；不可达组内按版本档再按地址。更新现有 `Featured_test_server_sorts_before_recent_and_low_latency_servers`（去掉 `LastSuccessConnect`）。
2. 版本解析：§1.4 四个夹具分别得到 `0.4 Inferred`、`0.5 Inferred`、`0.6 Inferred`、`0.6 Inferred`；`serviceVersion: "0.7.0"` 覆盖推断得 `0.7 Reported`；`"unknown"`、空串、`"v0.6.1"`、超长串、`"99999999999.1.0"`（int 溢出）不算 Reported；`{"ok":false,"capabilities":{"serverChatVersion":1}}` → Unknown；`{"ok":true,"capabilities":null}` → Unknown；非 JSON → Unknown。
3. 请求编排（`HttpMessageHandler` 计数）：metrics 带合法 `serviceVersion` 且 modSync>0 → 0 次 `/probe`；metrics 带合法 `serviceVersion` 且 modSync=0 → 1 次 `/probe`，且 `/probe` 失败或返回 `{"ok":true}` 时最终版本仍为 Reported；metrics 无版本 → 恰好 1 次 `/probe`；metrics 404 → 仍 1 次 `/probe` 并得到版本；metrics 没有合法 Reported 版本时，`/probe` 失败 → Unknown 且 metrics 不丢；`{"ok":true,"capabilities":null}` 时不回填 mod-sync，保留原 metrics 与 Reported；mod-sync 回填与现有测试一致。
4. 延迟：Tier 1 成功时发 2 次 `/peers/health` 且取较小值；第一次失败不发第二次并回退 `/probe`；两级失败 → `-1`/Unreachable；调用方取消后不再发送任何后续请求；单请求超时（`ct` 未取消）照常回退。
5. 稳定顺序：`resort=false` 保持旧顺序、新条目追加、消失条目剔除、地址规范化（尾斜杠、大小写）；**连续调用**：追加 B/C 后把返回地址写回 → 反转 B/C 的延迟、版本、可达性 → 再次 `resort=false`，顺序不变；`resort=true` 等价于 `OrderForDisplay`；日志格式化函数（`refresh:` / `render:` / `order:` / `closed:` 行）输出与 §6 规定逐字一致，`order:` 不截断（构造 50 项验证）。
6. 版本号与 alpha2 文档测试（§8）。

**GdUnit（`sts2-lan-connect.GdUnitTests/Chat/LanConnectServerSelectionDialogTests.cs`）**

7. `0.5.x（推断）` 行渲染 `ServiceTooOldBadge`，文本 `服务端版本过旧`；`0.6.1` 与 `Unknown` 行不渲染；tooltip 含 `服务端版本：`。
8. 若可行：刷新任务中 CF 任务抛异常时，最终 `Render(resort: true)` 发生在探测任务结束之后（可通过可注入的任务源或抽出的纯函数验证；不可行时在汇报中说明，并由 E2E 日志覆盖）。

**node:test（lobby-service）**

9. `/peers/metrics` 含 `serviceVersion`（注入值与缺省 `"unknown"`）；`/probe` 的 `capabilities.serviceVersion` 等于 `package.json` 版本。

### 9.4 实施完成门禁（Opencode 必须全部跑通并贴出末尾输出）

```bash
cd lobby-service && npm test
cd .. && dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj
dotnet build sts2-lan-connect/sts2_lan_connect.csproj -c Release
```

三条均须 0 失败（包含 §8.4 修正后的原既有失败用例）。GdUnit 由 E2E 阶段执行，Opencode 可尝试但不作为其门禁。

---

## 10. 兼容性

| 组合 | 结果 |
|---|---|
| 旧客户端 + 新服务端 | 旧客户端忽略新增 `serviceVersion` 字段，行为不变 |
| 新客户端 + 0.6.2-alpha.2 服务端 | 读 `serviceVersion`，精确版本 |
| 新客户端 + 0.6.0～0.6.2-alpha.1 服务端 | `/probe` 推断为 0.6 档 |
| 新客户端 + 0.5.x / 0.4.x 服务端 | 推断为 0.5 / 0.4 档，标注“服务端版本过旧” |
| 新客户端 + `/probe` 失败但延迟探测通 | 若 metrics 报了版本则用之，否则 Unknown，可达组最末，不标注 |
| Cloudflare Worker | 不变 |

请求量（每台每轮）：延迟探测 `/peers/health` 最多 2 次（失败回退时 `/probe` 最多 2 次）；快照链路 `/peers/metrics` 1 次、`/probe` 0～1 次。均为小 JSON。

---

## 11. E2E 验收（交给 Codex，实施完成后）

总原则：不部署远程服务器、不发布、不提交。允许临时改动本机游戏 `mods/` 与客户端数据目录，但必须先完整备份、结束后按字节恢复。任何一项失败都给出原始输出，**不得修改业务代码修到通过**，缺陷回报 Claude。

### 11.0 前置与备份（游戏未运行时进行）

1. 确认屏幕未锁定（`ioreg -n Root -d1 -a | grep -A1 CGSSessionScreenIsLocked`），`cliclick` 可用，Steam 分支为 0.111.0（`SlayTheSpire2.app/Contents/Resources/release_info.json`）。
2. 备份并记录是否原本存在：
   - `SlayTheSpire2.app/Contents/MacOS/mods/sts2_lan_connect/`（现为 0.6.2-alpha.1）
   - `~/Library/Application Support/SlayTheSpire2/sts2_lan_connect/config.json`
   - `~/Library/Application Support/SlayTheSpire2/sts2_lan_connect/known_peers.json`
3. 备份位置写入报告。

### 11.1 源码与运行时门禁

1. §9.4 三条命令全部通过。
2. GdUnit：

   ```bash
   GODOT_BIN=/Users/mac/.local/bin/godot451-mono \
   dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
     -p:RitsuLibAssembly=/Users/mac/Desktop/STS2-local-cleanup-20260912/tem/STS2-RitsuLib/.godot/mono/temp/bin/Release/STS2-RitsuLib.dll -m:1
   ```

   断言总执行数 > 0，且 §9.3 第 7（及第 8，若实现）条新增用例名出现在结果中并通过。
3. 0.107.1 类型加载：`scripts/verify-game-abi-load.sh --data-dir ~/Desktop/STS2-fixtures/0.107.1-data --dll <新构建 DLL>` 通过。说明：该工具只做类型枚举，不覆盖方法体 JIT；报告中如实注明此边界。
4. 打包校验（§2 的临时例外）：`RITSULIB_ASSEMBLY=<同上 DLL> scripts/verify-release.sh --artifacts-only` 通过；确认未向 `releases/` 写入任何文件（`git status` 佐证）。

### 11.2 本地新服务端

在临时目录 `T=$(mktemp -d)` 下，用仓库构建产物启动 `0.6.2-alpha.2` lobby-service（入口以 `lobby-service/package.json` 的 `start` 脚本为准，使用绝对路径）。以下变量须 `export` 给服务进程；它们已覆盖 `config.ts` 中全部五个默认写入 cwd 的路径（Codex 第 2 轮核实），`SENSITIVE_LEXICON_DIR` 为只读目录无需迁移：

```bash
HOST=127.0.0.1
PORT=<空闲端口>
RELAY_BIND_HOST=127.0.0.1
RELAY_PORT_START=<空闲段起> RELAY_PORT_END=<空闲段止>
SERVER_ADMIN_STATE_FILE=$T/server-admin.json
SERVER_UPDATE_DATA_DIR=$T/service-update
AI_MODERATION_STATE_FILE=$T/ai-moderation-state.json
AI_MODERATION_CACHE_FILE=$T/ai-moderation-cache.json
PEER_NETWORK_ENABLED=true
PEER_SELF_ADDRESS=http://127.0.0.1:<端口>
PEER_STATE_DIR=$T/peer
PEER_CF_DISCOVERY_BASE_URL=
PEER_PUBLIC_LISTING_ENABLED=false
PEER_DISPLAY_NAME=E2E-alpha2-local
SERVER_UPDATE_ENABLED=false
```

`curl` 验证 `/probe` 的 `capabilities.serviceVersion` 与 `/peers/metrics` 的 `serviceVersion` 均为 `0.6.2-alpha.2`。服务保持运行到 11.3 结束。

### 11.3 真机选服界面

1. 游戏未运行时，向 `known_peers.json` 追加本地节点条目（`{"address":"http://127.0.0.1:<端口>","discoveredVia":"e2e"}`，保留原有条目；文件不存在则新建合法结构）。**不使用手动输入**（它会直接选中并关闭对话框）。
2. 安装新构建 DLL 到 `mods/sts2_lan_connect/`，启动游戏，打开选服界面，等待该窗口本轮出现 `refresh: dialog=<D> generation=<N> state=completed`。
3. 证据：
   - 刷新完成后截图至少一张；
   - `godot.log` 中本次测试期间全部 `server picker` 行（按 `dialog` + `generation` 分组保存）；
   - **排序严格核对**（仅 `state=completed` 的轮次）：脚本解析该轮 `order:` 行中每项的 pinned / 版本 / 状态 / 毫秒，按 §3 独立计算顺序，与日志顺序**严格相等**；并断言 `order:` 的 `count` 等于项数、地址序列与同轮最终 `render:` 的 `order=` 一致。另起的公网测速只作参考；
   - **不跳动自动断言**（按 `dialog` + `generation` 分组）：
     - 对 `completed` 轮次：第一条 `render:` 为 `resort=true`；之后恰好再有一条 `resort=true`，且它是该轮最后一条 `render:`；其间所有 `resort=false` 渲染中，上一次渲染已出现的地址相对顺序不变；
     - 对 `canceled` 轮次：不要求最终重排；`canceled` 行之后该轮不得再出现 `render:` 或 `order:`；
     - 对 `failed` 轮次：报告为失败并附原始日志；
     - 每个 `closed: dialog=<D>` 之后，该窗口不得再出现任何 `render:`；
   - 魔仙堡第一；0.5.x/0.4.x 行显示“服务端版本过旧”（截图）；本地节点显示精确版本 `0.6.2-alpha.2`（tooltip 截图或日志 `order:` 行）。
4. 取消与重复刷新：在同一窗口连续快速点击刷新至少 3 次（预期前几轮为 `canceled`、最后一轮 `completed`），再重新打开选服界面并立即关闭一次（预期出现新的 `dialog` 编号与 `closed:` 行）；按上一步的分组规则断言，并确认日志中无未处理异常。

### 11.4 回归

选择任一 0.6 档公网服务器，能正常进入大厅界面（不要求建房或加入对局）。

### 11.5 清理（必须执行）

1. 退出游戏。
2. 停止本地服务，删除 `$T`。
3. 恢复 `mods/sts2_lan_connect/`、`config.json`、`known_peers.json` 为备份的**原始字节**；原本不存在的文件删除。
4. `shasum` 比对恢复结果与备份一致，写入报告。

报告需逐项列出命令、结果与证据路径。

---

## 12. 风险

- **推断误判**：只用 `/probe` 推断，先校验 `Ok`，`Unknown` 不标注。在已核实的官方响应形态下不会误标；本次未发现官方 ≥0.6 服务缺少 `dualProtocolApiVersion` 的反例。第三方魔改服务端不在保证范围内。
- **延迟两次采样增加请求**：每台 +1 个极小请求，可接受。
- **0.107.1 兼容**：只用 BCL 与现有类型；E2E 11.1 第 3 步覆盖类型加载，但不覆盖方法体 JIT。
- **GdUnit 不在发布门禁里**：E2E 手动执行并核对新增用例确实执行。
- **E2E 改动本机数据**：11.0 备份、11.5 按字节恢复。
- **回滚**：全部改动未提交，`git checkout -- <files>` 并删除新增文件即可；线上无变更。

---

## 修订记录

### 第 2 版（吸收 Codex 第 1 轮评审）

| 评审项 | 处理 |
|---|---|
| MAJOR 1：`_displayOrder` 只在 resort 时更新，新条目仍会跳 | 采纳。§6 规定每次渲染后写回；§9.3-5 增加连续调用测试 |
| MAJOR 2：手动输入不能把本地节点加入候选 | 采纳。§11.3 改为游戏未运行时写入 `known_peers.json`，禁用手动输入路径 |
| MAJOR 3：恢复范围漏 `config.json`，且缓存会被整体重写 | 采纳。§11.0 完整备份，§11.5 按字节恢复并 `shasum` 比对 |
| MAJOR 4：独立测速不能作为判定基准，不跳动证据不足 | 采纳。§6 增加逐次渲染日志；§11.3 以日志自身数值严格核对并自动断言稳定性；增加重复刷新与关闭取消场景 |
| MINOR 1：20ms 档 + 精确毫秒等价于精确毫秒 | 采纳。§3 删除延迟档，稳定性交给 §6 |
| MINOR 2：Reported 与 probe 失败冲突；回退条件应为 `<= 0` | 采纳。§4.4 规定 `reported ?? probeVersion`，条件改 `<= 0`，“最多一次 probe”限定快照链路 |
| MINOR 3：应先校验 `Ok`、处理显式 null、数字溢出；风险措辞过满 | 采纳。§4.3 调整顺序与 `int.TryParse`；§9.3-2 增加用例；§12 限定措辞 |
| MINOR 4：异常路径未等所有任务结束；取消与超时需区分 | 采纳。§6 用 `Task.WhenAll`；§5 规定取消后不再发请求 |
| MINOR 5：`/probe` 集成测试是整体 `deepEqual` | 采纳。§9.1 允许更新 `CURRENT_PROBE_CAPABILITIES` 及同类整体比较 |
| MINOR 6：E2E 前置条件、打包例外、GdUnit 用例核对、ABI 工具边界 | 采纳。§11.1、§11.2 与 §2 非目标中明确 |

### 第 3 版（吸收 Codex 第 2 轮评审）

| 评审项 | 处理 |
|---|---|
| MAJOR：日志缺少窗口标识，取消轮次被错误要求最终重排 | 采纳。§6 所有日志带 `dialog` + `generation`，新增 `refresh:` 状态行与 `closed:` 行；§11.3 按状态分别断言，取消轮次只断言“之后不再渲染” |
| MINOR 1：§9.3-3 残留无前提的“probe 失败 → Unknown” | 采纳。限定为“metrics 没有合法 Reported 版本时” |
| MINOR 2：mod-sync 回填应检查 `Capabilities != null` | 采纳。§4.4 回填条件追加判空，模型保留 `= new()` 默认值；§9.3-3 增加用例 |
| MINOR 3：`order:` 前 40 项只能验证前缀 | 采纳。§6 改为全量不截断，并要求与最终 `render:` 地址序列一致；§9.3-5 用 50 项验证 |
| MINOR 4：自动更新开关应写真实变量名 | 采纳。§11.2 使用 `SERVER_UPDATE_ENABLED=false`，注明五个可写路径已全覆盖、须 export、绝对路径启动 |

### 第 4 版（Codex 第 3 轮 APPROVE 之后的增补）

| 事项 | 处理 |
|---|---|
| 基线 `dotnet test` 有 1 条既有失败，会让 §9.4 门禁无法通过 | 新增 §8.4：记录基线结果，只修正那条过期断言为校验和格式检查；§8.2 相应注明例外；§9.4 要求三条门禁 0 失败 |
| Codex 第 3 轮给出的实施细节 | §6 增加实施提示：按任务捕获的 generation 去重 `canceled`、复用现有 `_ExitTree`、允许 `closed` 后补记 `canceled` |
