# STS2 LAN Connect：0.6.0 在游戏 0.107.1（Steam 正式分支）无法加载 — 修复报告

日期：2026-08-27。范围：仅问题 1（正式分支加载失败）。问题 2（续局重开后房间不可见 / 房主聊天）与问题 3（tail 房间 `LobbyJoinTimeout`、旧大厅服务器缺 `protocolSelection`）按决定暂不处理，调查结论保留在会话记录与 `docs/STS2_LAN_CONNECT_GAME_0107_DUAL_LOAD_FIX_PLAN_ZH.md` 第 0 节。

修复策略：**恢复双版本加载**——同一个 `sts2_lan_connect.dll` 同时在 0.107.1（`public`，build 23811903）与 0.111.0（`public-beta`，build 24724944）上加载并可用；0.107.1 上只提供兼容模式 `compat_4_5_v1`，tail_v1 自动关闭。未改版本号、未改协议线格式、未动 `releases/`、未提交。

## 1. 根因

玩家日志 `godot (4).log`（游戏 `release=v0.107.1`）：

```
Exception thrown while loading mod sts2_lan_connect: ReflectionTypeLoadException
  Method 'Connect' in type 'RitsuSidecarENetClientConnectionInitializer' ... does not have an implementation.
  Could not load type 'MegaCrit.Sts2.Core.Entities.Multiplayer.LoadRunLobbyPlayer' from assembly 'sts2'
  Could not load type 'MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer' from assembly 'sts2'
   at System.Reflection.RuntimeModule.GetTypes()
   at MegaCrit.Sts2.Core.Modding.ModManager.TryLoadMod(Mod mod)
```

游戏 ModManager 对 mod 程序集整体 `Assembly.GetTypes()`；任一 TypeDef 在**类型加载层面**（基类 / 实现的接口 / 字段类型，含编译器生成的闭包类与 async 状态机字段）引用了 0.107.1 不存在或签名不同的游戏类型，整个 mod 不加载。

用 System.Reflection.Metadata 审计 v0.6.0 发布 DLL，命中 29 处，全部来自 0.6.0 新增的 tail_v1 运行时与 RitsuLib sidecar 加入流程：

| 位置 | 引用 |
|---|---|
| `LanConnectLobbyJoinFlow/RitsuSidecarENetClientConnectionInitializer` | 实现游戏接口 `IClientConnectionInitializer`（`Connect` 首参 0.107.1 为 `NetClientGameService`，0.111 为 `INetClientGameService`） |
| 其 `<Connect>d__6` 状态机 | 字段 `INetClientGameService` |
| `LanConnectTailMessageRuntime/<>c__DisplayClass47_1/52_0/53_1/56_0/56_1` | 闭包字段 `StartRunLobbyPlayer` / `LoadRunLobbyPlayer` / `IReadOnlyList<StartRunLobbyPlayer>` |
| `LanConnectTailMessageRuntime/<>c` | 19 个 `Func<StartRunLobbyPlayer,…>` / `Func<LoadRunLobbyPlayer,…>` 静态 lambda 缓存字段 |
| `<JoinAsync>d__0`、`<BeginAsync>d__22`、`<ConnectAsync>d__23` | 字段 `IClientConnectionInitializer` |

v0.5.6-rc4 及更早版本没有引用上述任何符号（`git grep` 确认），并以 0.107.1 程序集为编译基线；0.6.0 全部 alpha 只在 0.111.0 上验证，开发机 Steam 安装的也是 0.111.0，因此构建从未见过 0.107.1。`sts2_lan_connect.json` 未声明最低游戏版本，游戏只 WARN 不拦截。

### 1.1 审阅中追加发现的第二层问题（方法体 / JIT 期）

对比 v0.5.6-rc4 发布 DLL 与新构建的全部 `sts2` 成员引用（MemberRef 表）后发现：`docs/RELEASE_NOTES_V0.5.5_CLIENT_ZH.md` 记载 **`PeerVersionInfo` 是游戏 0.110.x 才引入的类型**，0.5.6 用无参构造 `new NetHostGameService()` 并把相关补丁放在可失败的 `TryApplyGroup("PeerVersionInfo", …)` 里；而 0.6.0 在**兼容路径**上直接写了 `new NetHostGameService(PeerVersionInfo.LocalDefault())`（`LanConnectHostFlow.cs:55/:157`、`LanConnectMultiplayerSaveCompatibility.cs:154`）和 `new NetClientGameService(PeerVersionInfo.LocalDefault())`（`LanConnectLobbyManagedJoinFlow.cs:74`）。这类引用不影响 `GetTypes()`，但所在方法一旦被 JIT 就抛 `TypeLoadException`——只修第一层的话，mod 在 0.107.1 上"能加载"，但建房、续局重开、加入房间全部失败。同类隐患还有 `LanConnectLobbyManagedJoinFlow.OnDisconnected` 里对 `NetClientGameService.HostNetId` 的直接访问（0.5.6 未引用过该成员，0.107.1 是否存在无法确认）。

## 2. 改动清单

执行方式：修复计划（`docs/STS2_LAN_CONNECT_GAME_0107_DUAL_LOAD_FIX_PLAN_ZH.md`）由本会话撰写，实现由 OpenCode（`zhipuai-coding-plan/glm-5.3-flash`，variant max）分三轮执行，每轮结束后由本会话独立复验；详细执行记录见计划文档"执行记录"一节。

### 2.1 类型加载层（计划 §2–§4）

- `LanConnectLobbyJoinFlow.RitsuSidecarENetClientConnectionInitializer` 不再实现 `IClientConnectionInitializer`，`Connect` 参数改为具体类 `NetClientGameService`；`LanConnectLobbyManagedJoinFlow.BeginAsync/ConnectAsync` 参数改为 `object`，`Connect` 方法先按初始化器的具体类型反射解析，再回退到游戏接口（已有的 `ResolveCompatibleConnectMethod` 签名与三个既有测试保持不变）。
- `LanConnectTailMessageRuntime` 名册代码泛型化：`ProjectPlayers<TPlayer>`、`RestorePlayers<TPlayer>`、`RestoreJoinedPlayer<TPlayer>`、`RestoreLoadJoinPlayers<TPlayer>`、`BuildLoadJoinCarriers<TPlayer>`；新增 `LanConnectTailPlayerAccessors<TPlayer>` 用表达式树按成员名 `id` / `slotId` 构建访问器。所有涉及 `StartRunLobbyPlayer` / `LoadRunLobbyPlayer` 的 lambda 都位于泛型方法内，编译器生成的闭包类字段类型是 `TPlayer`，不再在加载期解析具体类型。线格式、校验条件、异常文本未变（golden vector 测试全部通过）。

### 2.2 0.107.1 上的行为收敛（计划 §5）

- 新增 `LanConnectTailRuntimeSupport`：启动后首次使用时纯反射探测游戏程序集——tail 需要的 9 个消息类型、`StartRunLobbyPlayer`/`LoadRunLobbyPlayer` 及 `id`/`slotId` 成员、`playersInLobby`/`playersAlreadyConnected`/`lobbyPlayer` 的成员类型、`INetClientGameService`；打一行 `sts2_lan_connect tail_runtime: available=…, gameVersion=…, reason=…`。
- `LanConnectProtocolPatchDispatcher.Apply`：探测不可用时只安装 compat 序列化补丁，跳过全部 tail 补丁并记日志——**不会**进入 `LanConnectDegradedMode`（降级模式会拒绝建房/进房）。
- `LanConnectProtocolOffer.CreateCurrent`：不可用时 offer 为 `0..0`（服务端 `validateOffer` 接受；compat 房间选择协议 0）。
- 建房对话框：tail_v1 选项在 offer 不支持时不可选，说明文案改为「仅支持 0.6+；当前游戏版本不支持该协议，请使用兼容模式」；`LanConnectCreateRoomIntent.Validate` 兜底抛 `lan_protocol_version_mismatch`。
- 加入 tail_v1 房间：`LanConnectLobbyJoinFlow.JoinAsync` 与大厅列表的 `CanJoinRoom` 直接给出「该房间使用 0.6 新协议（tail_v1），当前游戏版本 {版本} 不支持。请切换到 Steam 测试分支（public-beta）或加入兼容模式房间。」，不再显示笼统的「LAN 协议版本不匹配」。

### 2.3 方法体层（计划 §9 与第三轮）

- 新增 `LanConnectNetGameServiceFactory`：纯反射构造 `NetHostGameService` / `NetClientGameService`——存在"单参数且参数类型简单名为 `PeerVersionInfo`"的构造且该类型有 `static LocalDefault()` 时用它（0.111），否则退回无参构造（0.107.1，与 0.5.6 一致）；策略按类型缓存。方法体内不出现 `PeerVersionInfo` 标识符。四个调用点全部替换。
- `LanConnectLobbyManagedJoinFlow.OnDisconnected` 中 `HostNetId` / `TryTakeValidatedRejection` 的访问移入仅在 tail 会话下才调用的独立方法，0.107.1 上该方法永远不会被 JIT。

### 2.4 回归保护与工具

- `sts2-lan-connect.Tests/Packaging/LanConnectGameAbiTypeLoadContractTests.cs`：扫描构建产物元数据，黑名单 `StartRunLobbyPlayer`、`LoadRunLobbyPlayer`、`INetClientGameService`、`IClientConnectionInitializer`、`PeerVersionInfo` 不得出现在任何 TypeDef 的基类 / 接口 / 字段签名上；另有 MemberRef 哨兵：不得出现 `Net{Host,Client}GameService::.ctor(PeerVersionInfo)` 与 `PeerVersionInfo::LocalDefault()`。该测试在改动前对 0.6.0 代码报出 29 处并失败，改动后通过；哨兵 Fact 做过红-绿验证。
- `sts2-lan-connect.Tests/Protocol/LanConnectTailRuntimeSupportTests.cs`（7 个）、`sts2-lan-connect.Tests/LanHost/LanConnectNetGameServiceFactoryTests.cs`（5 个）、`LanConnectLobbyManagedJoinFlowTests` 追加 duck-type 解析测试。
- `sts2-lan-connect/tools/GameAbiLoadCheck/`（独立 net9.0 工具，不进 sln，mod csproj 显式 `Compile Remove`，目录加 `.gdignore`，`bin/obj` 已加入 `.gitignore`）+ `scripts/verify-game-abi-load.sh [--data-dir <dir>] [--dll <path>]`：在自定义 `AssemblyLoadContext` 里按游戏 data 目录解析依赖并对 mod DLL 做真实 `GetTypes()`，退出码 0 / 1 / 2。

改动文件：`.gitignore`、`sts2-lan-connect/sts2_lan_connect.csproj`、`Scripts/LanConnectHostFlow.cs`、`Scripts/LanConnectNetGameServiceFactory.cs`（新）、`Scripts/Lobby/LanConnectLobbyJoinFlow.cs`、`Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs`、`Scripts/Lobby/LanConnectLobbyOverlay.cs`、`Scripts/Lobby/LanConnectMultiplayerSaveCompatibility.cs`、`Scripts/Protocol/LanConnectCreateRoomIntent.cs`、`Scripts/Protocol/LanConnectProtocolOffer.cs`、`Scripts/Protocol/LanConnectTailPlayerAccessors.cs`（新）、`Scripts/Protocol/LanConnectTailRuntimeSupport.cs`（新）、`Scripts/Protocol/Patches/LanConnectProtocolPatchDispatcher.cs`、`Scripts/Protocol/Patches/LanConnectTailMessageRuntime.cs`、`tools/GameAbiLoadCheck/*`（新）、`scripts/verify-game-abi-load.sh`（新）、四个测试文件、两份文档。

## 3. 验证结果（本会话独立复验，非 OpenCode 自报）

| 项目 | 基线（改动前） | 结果 |
|---|---|---|
| 元数据审计（独立工具，黑名单 4 类型，扫描基类/接口/字段） | 29 处命中 | **0 处** |
| `sts2` MemberRef 中 `PeerVersionInfo::LocalDefault` / `Net*GameService::.ctor(PeerVersionInfo)` | 存在 | **不存在**（仅剩 `LanConnectPeerVersionInfoPatches` 内受 `TryApplyGroup` 保护的 `otherMods` / `Nullable<PeerVersionInfo>` 引用，与 0.5.6 相同） |
| `dotnet build sts2-lan-connect/sts2_lan_connect.csproj` | — | 0 错误 |
| `dotnet test sts2-lan-connect.Tests` | 1127 通过 / 1 跳过 | **1143 通过 / 0 失败 / 1 跳过**（三轮全部完成后复验） |
| `./scripts/verify-game-abi-load.sh`（本机 0.111.0） | — | `abi load OK: 1204 types loaded.`，退出码 0 |
| 0.111.0 行为 | — | 测试层面 tail_v1 / compat / Ritsu sidecar 路径未变；未做实机联机回归 |

## 3.1 0.107.1 实机验证（本机 Steam 切到 `public` 分支后）

本机 Steam 安装切换到 `public` 分支（`release_info.json` = `v0.107.1`，build 23811903）。0.107.1 的 `data_sts2_macos_arm64` 已快照到 `~/Desktop/STS2-fixtures/0.107.1-data/`（105 MB），用 0.111 编译出的修复版 DLL 归档在 `~/Desktop/STS2-fixtures/built-0107fix/`。

| 检查 | 结果 |
|---|---|
| `verify-game-abi-load.sh --dll <修复版>`，data 目录为真实 0.107.1 | `abi load OK: 1204 types loaded.`，退出码 0 |
| 同一工具跑 v0.6.0 发布 DLL（阴性对照） | `abi load FAILED: only 1184/1189 types materialized.` + 与玩家日志逐字相同的 5 条 LoaderExceptions，退出码 1 |
| 实机启动（无 RitsuLib，`mods/` 里另有 SayTheSpire2） | `Calling initializer … sts2_lan_connect, Version=0.6.0.0` → `Finished mod initialization for 'STS2 LAN Connect'` → `RUNNING MODDED! --- Loaded 3 mods` |
| 探测器 | `tail_runtime: available=False, gameVersion=v0.107.1, reason=missing MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer` |
| 分发器 | `protocol dispatcher: tail runtime unavailable, skipping tail patches: …`；`multiplayer compatibility ready … publishedProtocolProfile=compat_4_5_v1`；未进入降级模式 |
| 序列化补丁 | `patches applied=6, failed=0. runtimePlayerType=MegaCrit.Sts2.Core.Entities.Multiplayer.LobbyPlayer`（0.107.1 的旧类型名，运行时解析正确） |
| PeerVersionInfo 补丁组 | `[ERROR] gameplay: PeerVersionInfo patches failed: TypeLoadException` —— 与 0.5.6 在 0.107.1 上相同，受 `TryApplyGroup` 保护，其余 8 组补丁全部应用 |
| wire cache | `wire_cache capture failed: … ModelIdSerializationCache … _initialized not found` —— 0.5.6 起就有的 0.107.1 行为（同一 `RequireField("_initialized")`），本地不可用时握手决策 `IsAllowed: true`，不阻断兼容模式 |
| 大厅运行时 | `lobby runtime ready`；大厅列表刷新正常；对列表里的 tail_v1 房间，加入按钮直接显示「该房间使用 0.6 新协议（tail_v1），当前游戏版本 v0.107.1 不支持。请切换到 Steam 测试分支…」 |
| 兼容模式建房（2026-08-28，RitsuLib 在游戏模组菜单中取消勾选后） | `create requested … protocolProfile=compat_4_5_v1` → `POST /rooms -> 201` → `relay host tunnel: starting … relay=101.35.217.99:39005` → `host control channel connected` → 心跳 200。官方大厅 `GET /rooms` 返回该房间：`version=v0.107.1, modVersion=0.6.0, protocolProfileV2=compat_4_5_v1, protocolSelection.profile=compat_4_5_v1, selectedLanProtocolVersion=0, carrier=none, ritsuLibPresent=false, relayState=ready` |
| 0.107.1 + RitsuLib（创意工坊订阅 0.5.16） | 建房对话框两种模式均不可选（兼容模式不允许 RitsuLib，tail 不可用），UI 说明了原因；把创意工坊里的 DLL 改名停用无效——Steam 在下次启动前会重新校验并还原订阅内容，只能在游戏模组菜单取消勾选或取消订阅 |

附带观察：从 0.111 切回 0.107.1 后，游戏自己把 v20 格式的单机存档 `modded/profile1/saves/current_run.save` 改名为 `current_run.<ts>.FUT.corrupt`（`Save file version 20 is newer than current version 16`）。这是游戏的分支降级行为，与 mod 无关；切回 `public-beta` 后把文件改回原名即可恢复。

## 4. 尚未完成 / 需要的下一步

1. 0.107.1 上兼容模式**建房**已实测通过（见 §3.1）；**加入**（第二台 0.107.1 客户端进入该房间并开局）尚未实测，建议发布前用两台正式分支机器跑一次。
2. 方法体层的 0.107.1 风险除已处理的 `PeerVersionInfo` 与 `HostNetId` 外，其余"较 0.5.6 新增"的游戏成员引用（`PacketReader/PacketWriter` 成员、`NetClientData`、`ENetClient`、`SendMessageToAllHandlers` 等）都只出现在 tail 运行时 / tail 补丁 / Ritsu sidecar 路径，0.107.1 上不会被 JIT；实机启动已证明初始化路径干净。
3. 产品策略待定：0.107.1 + RitsuLib 的组合下，兼容模式不允许 RitsuLib、tail 又不可用，玩家将无法建房/进房（UI 会给出原因）。需要决定是否在 0.107.1 上放宽"兼容模式不允许 RitsuLib"。
4. 发布前：`sts2_lan_connect.json` 与 Workshop 说明补上「同时支持正式分支 0.107.1 与测试分支 0.111.0；0.107.1 只提供兼容模式」；`docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md:68` 等仍写着 0.5.x 时代的版本列表，需要更新；建议把 `scripts/verify-game-abi-load.sh` 对 0.107.1 与 0.111.0 两个目录的运行加入 `scripts/verify-release.sh`。
5. 附带发现（未改）：`lobby-defaults.json` 放在 mod 根目录，0.107.1 的 ModManager 会把它当 manifest 解析并打 `[ERROR] missing 'id'`（0.111 会跳过）。不致命，从 0.2.1 起就存在。
