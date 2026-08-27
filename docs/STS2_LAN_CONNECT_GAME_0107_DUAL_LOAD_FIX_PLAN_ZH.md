# STS2 LAN Connect：恢复游戏 0.107.1（Steam 正式分支）双版本加载 — 修复计划

状态：待执行（本文由调查结论直接推导，执行者按本文逐条实现；每一节末尾都有可机械检查的完成标准）。

## 0. 背景（执行者必须先读）

- Steam `public` 分支 = build `23811903` = 游戏 **v0.107.1**；`public-beta` 分支 = build `24724944` = **v0.111.0**。两条分支并存，玩家可任选。
- 客户端 mod v0.6.0 在 0.107.1 上**整体加载失败**。玩家日志（`godot (4).log`）：

```
Exception thrown while loading mod sts2_lan_connect: System.Reflection.ReflectionTypeLoadException
  Method 'Connect' in type 'RitsuSidecarENetClientConnectionInitializer' ... does not have an implementation.
  Could not load type 'MegaCrit.Sts2.Core.Entities.Multiplayer.LoadRunLobbyPlayer' from assembly 'sts2'
  Could not load type 'MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer' from assembly 'sts2'
   at System.Reflection.RuntimeModule.GetTypes()
   at MegaCrit.Sts2.Core.Modding.ModManager.TryLoadMod(Mod mod)
```

- 机制：游戏的 ModManager 对整个 mod 程序集调用 `Assembly.GetTypes()`；只要有**一个 TypeDef 在类型加载层面**（基类 / 实现的接口 / **字段类型**）引用了 0.107.1 不存在或签名不同的游戏类型，整个 mod 就不加载。方法体内引用缺失成员只会在该方法被 JIT 时失败，不影响加载。
- 用 System.Reflection.Metadata 审计 v0.6.0 发布 DLL（`releases/sts2_lan_connect/sts2_lan_connect.dll`），类型加载层面的问题**只有**以下几处（全部在两个文件里）：

| TypeDef | 问题 |
|---|---|
| `LanConnectLobbyJoinFlow/RitsuSidecarENetClientConnectionInitializer` | 实现了游戏接口 `MegaCrit.Sts2.Core.Multiplayer.Connection.IClientConnectionInitializer`；0.107.1 上该接口 `Connect` 的第一个参数是具体类 `NetClientGameService`，0.111 上是 `INetClientGameService`，静态实现必然在其中一个版本上"没有实现" |
| 同上的 `<Connect>d__6` 状态机 | 字段 `netService` 类型为 `INetClientGameService` |
| `LanConnectTailMessageRuntime/<>c__DisplayClass47_1`、`53_1` | 闭包字段 `player : LoadRunLobbyPlayer` |
| `LanConnectTailMessageRuntime/<>c__DisplayClass52_0`、`56_1` | 闭包字段 `restored` / `player : StartRunLobbyPlayer` |
| `LanConnectTailMessageRuntime/<>c__DisplayClass56_0` | 闭包字段 `players : IReadOnlyList<StartRunLobbyPlayer>` |
| `LanConnectTailMessageRuntime/<>c` | 静态 lambda 缓存字段 `Func<StartRunLobbyPlayer,…>` / `Func<LoadRunLobbyPlayer,…>` 共 19 个 |
| `LanConnectLobbyJoinFlow/<JoinAsync>d__0`、`LanConnectLobbyManagedJoinFlow/<BeginAsync>d__22`、`<ConnectAsync>d__23` | 字段类型 `IClientConnectionInitializer`（该接口两版本都存在，仅作为字段不会导致加载失败，但本次一并去掉，避免依赖接口本身继续存在） |

- 0.5.6 及更早版本没有引用上述任何符号，因此能在 0.107.1 上加载；这些引用全部来自 0.6.0 新增的 tail_v1 协议运行时与 RitsuLib sidecar 加入流程。
- 本机安装的是 0.111.0（`~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2`），**没有 0.107.1 程序集**；因此本计划的可验证目标是"元数据契约 + 真实 `GetTypes()` 加载工具"，而不是在 0.107.1 上实机启动（见第 6 节）。

## 1. 目标与非目标

目标（全部必须达成）：

1. 同一个 `sts2_lan_connect.dll` 在 0.107.1 与 0.111.0 上都能通过 `Assembly.GetTypes()` 加载。
2. 在 0.107.1 上，mod 初始化不得因 tail_v1 补丁目标缺失而抛异常进入 `LanConnectDegradedMode`（降级模式会拒绝建房/进房）；应只安装 compat 序列化补丁，tail 相关补丁整体跳过并打一条日志。
3. 在 0.107.1 上，协议 offer 不再声明支持 tail（`LanProtocolMin = LanProtocolMax = 0`）；建房对话框不显示/不可选 tail_v1；加入 tail_v1 房间时给出明确中文提示，而不是笼统的「LAN 协议版本不匹配」。
4. 0.111.0 上行为不变：tail_v1 / compat_4_5_v1 / RitsuLib sidecar 全部照旧；现有测试全部通过。
5. 新增一个**编译期契约测试**：审计构建产物元数据，若任何 TypeDef 的基类 / 接口 / 字段签名引用了黑名单游戏类型，测试失败。该测试在改动前必须失败、改动后必须通过。
6. 新增一个可对任意游戏 data 目录运行的**真实加载检查工具**，并先对本机 0.111.0 跑通。

非目标：

- 不在 0.107.1 上启用 tail_v1；不改 lobby-service；不改协议线格式；不改版本号（发布时另行决定）。
- 不处理调查报告里的问题 2（续局重开后房间不可见 / 聊天）和问题 3（tail 房间 LobbyJoinTimeout、旧大厅服务器缺 protocolSelection）。
- 不改 `sts2_lan_connect.json`（不声明最低游戏版本，因为目标就是两版本都支持）。

## 2. 改动一：契约测试（先写，先看到它失败）

文件：`sts2-lan-connect.Tests/Packaging/LanConnectGameAbiTypeLoadContractTests.cs`

- 用 `System.Reflection.Metadata`（`PEReader` + `MetadataReader`，BCL 内置，无需新包）读取 `typeof(Sts2LanConnect.Scripts.Entry).Assembly.Location`。
- 对每个 `TypeDefinition`（含嵌套类型）收集：`BaseType`、`GetInterfaceImplementations()`、每个字段 `DecodeSignature(...)` 的完整签名字符串（含泛型实参）。实现一个 `ISignatureTypeProvider<string, object?>`，把 `TypeReference` 渲染成 `"<assemblyName>::<Namespace>.<Name>"`，把泛型实例渲染成 `G<A,B>`，这样 `Func<sts2::…StartRunLobbyPlayer,UInt64>` 之类也能被匹配到。
- 黑名单（写成常量数组，附注释说明来源是 0.107.1 实机日志）：
  - `MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer`
  - `MegaCrit.Sts2.Core.Entities.Multiplayer.LoadRunLobbyPlayer`
  - `MegaCrit.Sts2.Core.Multiplayer.Game.INetClientGameService`
  - `MegaCrit.Sts2.Core.Multiplayer.Connection.IClientConnectionInitializer`
- 断言：任何 TypeDef 的基类 / 接口 / 字段签名字符串都不包含黑名单中的任何一个全名。失败信息要列出 `TypeDef 全名 [base|iface|field:<名>] -> 签名`，方便定位。
- 另加一个 `[Fact]`：没有任何 TypeDef 实现 `IClientConnectionInitializer`（与上一条重叠，但错误信息更直接）。
- 完成标准：在未做第 3、4 节改动前运行 `dotnet test sts2-lan-connect.Tests --filter FullyQualifiedName~LanConnectGameAbiTypeLoadContractTests`，必须失败且报出上表中的类型；改动完成后必须通过。

## 3. 改动二：连接初始化器不再实现游戏接口

文件：`sts2-lan-connect/Scripts/Lobby/LanConnectLobbyJoinFlow.cs`、`sts2-lan-connect/Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs`

1. `RitsuSidecarENetClientConnectionInitializer`（JoinFlow 第 404 行附近）：
   - 去掉 `: IClientConnectionInitializer`。
   - `Connect` 签名改为 `public async Task<NetErrorInfo?> Connect(NetClientGameService netService, CancellationToken cancelToken = default)`（具体类两版本都有）。方法体保持不变（`ENetClient client = new(netService); netService.Initialize(client, PlatformType.None); …`），删掉 `netService is not NetClientGameService concrete` 那段类型检查，直接用 `netService`。方法体只在 tail_v1 + RitsuLib 路径被调用，即只在 0.111 上 JIT。
2. JoinFlow 第 126 行 `IClientConnectionInitializer initializer = selection.Carrier == … ? new RitsuSidecar…(…) : new ENetClientConnectionInitializer(…)` 改为 `object initializer = …`。
3. `LanConnectLobbyManagedJoinFlow.BeginAsync(IClientConnectionInitializer initializer, SceneTree sceneTree)` → 参数类型改为 `object`；`ConnectAsync(IClientConnectionInitializer initializer, …)` → `object`。
4. `ConnectAsync` 里的方法解析改为：先 `ResolveCompatibleConnectMethod(initializer.GetType(), netService.GetType())`；若抛 `MissingMethodException`，再回退到 `ResolveCompatibleConnectMethod(typeof(IClientConnectionInitializer), netService.GetType())`（这句只在方法体内引用接口类型，属 JIT 期引用，允许）。保留 `ResolveCompatibleConnectMethod(Type, Type)` 的现有签名与行为——`sts2-lan-connect.Tests/Lobby/LanConnectLobbyManagedJoinFlowTests.cs` 有三个测试依赖它。注意 `GetMethods(Public|Instance)` 找不到显式接口实现，这就是需要回退的原因。
5. `LanConnectDirectJoinFlow.cs` 第 71 行把 `ENetClientConnectionInitializer` 传给 `BeginAsync`，改成 `object` 参数后无需改动。
6. 在 `LanConnectLobbyManagedJoinFlowTests.cs` 追加一个测试：定义一个不实现任何接口、只有 `Task<NetErrorInfo?> Connect(TestNetService, CancellationToken)` 的类，断言 `ResolveCompatibleConnectMethod(typeof(该类), typeof(TestNetService))` 能解析到它（覆盖"具体类型优先"的路径）。

完成标准：契约测试中与 `IClientConnectionInitializer` / `INetClientGameService` 有关的报错全部消失；`LanConnectLobbyManagedJoinFlowTests` 全绿。

## 4. 改动三：tail 名册代码泛型化，消灭具名闭包字段

文件：`sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessageRuntime.cs`（第 824–1100 行区域：`PrepareStartRunRoster`、`PrepareBeginRun`、`RestoreStartRunResponse`、`RestoreBeginRun`、`RestorePlayerJoined`、`RestoreLoadJoin`、`RestoreStartRunPlayers`、`ProjectStartRunPlayers`）。

原则：**`StartRunLobbyPlayer` / `LoadRunLobbyPlayer` 只允许出现在方法体里的局部变量类型、泛型方法的类型实参、`message.playersInLobby` 之类的成员访问中；绝不能出现在任何 lambda 捕获的变量、`static` lambda 的参数/返回类型、async 方法的局部变量上。** 所有涉及这两个类型的 lambda 必须写在以 `TPlayer` 为类型参数的泛型方法里——编译器为泛型方法生成的闭包类 / lambda 缓存类是泛型的，字段类型是 `TPlayer`，加载时不会解析具体类型。

实现方式：

1. 新增一个泛型访问器（可放在同文件或新文件 `sts2-lan-connect/Scripts/Protocol/LanConnectTailPlayerAccessors.cs`）：

```csharp
internal sealed class LanConnectTailPlayerAccessors<TPlayer>
{
    public required Func<TPlayer, ulong> GetId { get; init; }
    public required Func<TPlayer, int> GetSlotId { get; init; }      // LoadRunLobbyPlayer 没有 slotId 时可为 null / 抛 NotSupported
    public required Action<TPlayer, int> SetSlotId { get; init; }

    // 用 System.Linq.Expressions 从成员名构建：Expression.PropertyOrField(param, "id") 同时支持字段和属性。
    public static LanConnectTailPlayerAccessors<TPlayer> FromMembers(string idMember, string? slotMember);
}
```

   在 `LanConnectTailMessageRuntime` 里用 `static readonly ConcurrentDictionary<Type, object>` 缓存实例（字段类型是 `object`，不能是 `LanConnectTailPlayerAccessors<StartRunLobbyPlayer>`）。取用时 `(LanConnectTailPlayerAccessors<TPlayer>)cache.GetOrAdd(typeof(TPlayer), _ => LanConnectTailPlayerAccessors<TPlayer>.FromMembers("id", "slotId"))`——这个 lambda 写在泛型方法里。`StartRunLobbyPlayer` 的成员是 `id`（ulong）、`slotId`（int）；`LoadRunLobbyPlayer` 只用 `id`。

2. 把下列方法改成泛型（`where TPlayer : IPacketSerializable, new()`，与现有 `DeserializeCarrier<T>` 约束一致），原有非泛型调用点只做"取 `message.playersInLobby` 局部变量 → 调用泛型方法 → 写回"三步，不含任何 lambda：
   - `ProjectStartRunPlayers` → `ProjectPlayers<TPlayer>(IReadOnlyList<TPlayer> players)`：内部现有的 `static player => player.id`、`.OrderBy(static player => player.slotId)`、捕获 `players`/`embeddedSlots` 的 lambda 全部改用访问器，留在泛型方法内。
   - `RestoreStartRunPlayers` → `RestorePlayers<TPlayer>(snapshot, IReadOnlyList<TPlayer> projection)`。
   - `RestorePlayerJoined` 中从 `DeserializeCarrier<StartRunLobbyPlayer>(…)` 开始到 `restored.slotId = carrier.RealSlotId` 的校验逻辑 → `RestoreJoinedPlayer<TPlayer>(LanConnectRosterPlayerCarrier carrier, LanConnectRosterSnapshot snapshot, ulong expectedId)`，返回 `TPlayer`；非泛型的 `RestorePlayerJoined` 只保留成员集合计算、调用泛型方法、`SetBoxedField`。注意原代码里 `.Single(value => value.player.PlayerId == restored.id)` 捕获了 `restored`，这是 `<>c__DisplayClass52_0` 的来源。
   - `RestoreLoadJoin` 中 `snapshot.Players.Select(carrier => { LoadRunLobbyPlayer player = …; … message.serializableRun.Players.FindIndex(saved => saved.NetId == player.id) … })` 与 `message.playersAlreadyConnected.Select(static player => player.id)` → `RestoreLoadJoinPlayers<TPlayer>(LanConnectRosterSnapshot snapshot, List<SerializablePlayer> savedPlayers, List<TPlayer> alreadyConnected)` 返回 `List<TPlayer>`；`SerializablePlayer` 两版本都有，可以直接用。
   - `PrepareStartRunRoster`、`PrepareBeginRun`、`RestoreStartRunResponse`、`RestoreBeginRun` 保持非泛型，但体内只允许局部变量 + 调用泛型方法。
3. 不要改变任何线格式、校验条件、异常文本：这些方法有 golden vector 测试（`sts2-lan-connect.Tests/Protocol/LanConnectRosterCodecGoldenVectorTests.cs`、`LanConnectTailCodecGoldenVectorTests.cs`、`LanConnectRosterProjectionTests.cs`），改完必须全部通过。
4. 改完后运行第 2 节的契约测试确认 `LanConnectTailMessageRuntime` 下再无任何报错。如果编译器仍生成带具名类型字段的 `<>c` / `<>c__DisplayClass`，说明还有 lambda 留在非泛型方法里，继续挪。

完成标准：契约测试全绿；`dotnet test sts2-lan-connect.Tests` 全绿。

## 5. 改动四：tail 运行时可用性探测与 0.107.1 行为收敛

新文件：`sts2-lan-connect/Scripts/Protocol/LanConnectTailRuntimeSupport.cs`

```csharp
internal sealed record LanConnectTailRuntimeSupportResult(bool Available, string? UnavailableReason);

internal static class LanConnectTailRuntimeSupport
{
    // 线程安全的懒加载缓存；提供 internal static void ResetForTesting() 与 SetForTesting(result)。
    public static LanConnectTailRuntimeSupportResult Current { get; }
    public static bool IsAvailable => Current.Available;

    // 纯反射，不触碰任何 0.111 专有类型的静态引用：
    internal static LanConnectTailRuntimeSupportResult Probe(Assembly sts2Assembly)
}
```

`Probe` 逐项检查，第一项失败即返回 `Available=false` 与原因（英文短句，含缺失的类型/成员名）：

1. `LanConnectTailMessageTypeMatrix` 的 10 个 kind 对应的 9 个消息类型全部能 `sts2Assembly.GetType("MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.<Name>")` 解析（复用 `LanConnectTailMessagePatchPlan` 里 `ResolveAllMessageKinds` 的逻辑，可把它改成 internal 复用，但不要让它在探测路径上抛异常）。
2. `MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer` 与 `LoadRunLobbyPlayer` 存在，且各自有 `id` 成员（字段或属性，ulong）；`StartRunLobbyPlayer` 有 `slotId`（int，可写）。
3. `ClientLobbyJoinResponseMessage.playersInLobby`、`LobbyBeginRunMessage.playersInLobby` 的成员类型是 `List<StartRunLobbyPlayer>`；`ClientLoadJoinResponseMessage.playersAlreadyConnected` 是 `List<LoadRunLobbyPlayer>`；`PlayerJoinedMessage.lobbyPlayer` 是 `StartRunLobbyPlayer`。
4. `MegaCrit.Sts2.Core.Multiplayer.Game.INetClientGameService` 存在。

`Current` 首次求值时用 `typeof(PacketWriter).Assembly` 探测，并用 `Log.Info` 打一行：`sts2_lan_connect tail_runtime: available=<bool>, gameVersion=<LanConnectBuildInfo.GetGameVersion()>, reason=<reason|none>`。

接入点（每处都要有对应单元测试，测试用 `SetForTesting` 切换）：

1. `LanConnectProtocolPatchDispatcher.Apply()`：在 `LanConnectSerializationPatches.Apply();` 之后，仅当 `LanConnectTailRuntimeSupport.IsAvailable` 才调用 `LanConnectTailMessagePatches.Apply(harmony)`；否则 `Log.Info("sts2_lan_connect protocol dispatcher: tail runtime unavailable, skipping tail patches: <reason>")`。`_applied = true` 照常。
2. `LanConnectProtocolOffer.CreateCurrent()`：不可用时 `LanProtocolMin = LanProtocolMax = 0`（服务端 `validateOffer` 只要求 uint16 且 min ≤ max，0..0 合法；compat 房间选择协议 0）。可用时不变（1..1）。
3. `LanConnectLobbyOverlay.IsCreateProtocolSelectable(...)`：tail 分支两种情况都追加 `&& offer.Supports(LanConnectConstants.TailLanProtocolVersion)`；不改方法签名（`GetDefaultCreateProtocolIdForTests` 等测试依赖）。`GetProtocolProfileDescription` 的 tail 文案在不可用时改为「仅支持 0.6+；当前游戏版本不支持该协议，请使用兼容模式」——可以通过多加一个 `bool tailRuntimeAvailable` 参数的重载实现。
4. `LanConnectLobbyJoinFlow.JoinAsync`：在 `selectionDto.ToValidatedValue(localOffer)` 之前，若 `selectionDto.Profile` 对应 `TailV1` 且 `!LanConnectTailRuntimeSupport.IsAvailable`，直接返回 `LobbyJoinAttemptResult(LobbyJoinAttemptKind.Failed, "该房间使用 0.6 新协议（tail_v1），当前游戏版本 {LanConnectBuildInfo.GetGameVersion()} 不支持。请切换到 Steam 测试分支（public-beta）或加入兼容模式房间。")`。同时在 `LanConnectLobbyOverlay.CanJoinRoom(room, out reason)` 里对 tail 房间给出同样的 reason，让"加入房间"按钮直接显示原因。
5. `LanConnectCreateRoomIntent.Validate()`：`Profile == TailV1 && !Offer.Supports(TailLanProtocolVersion)` 时抛 `lan_protocol_version_mismatch`，详情 `"Tail runtime is unavailable on this game version."`（兜底，UI 正常情况下不会走到）。

完成标准：新增测试至少覆盖——探测器对一个缺少 `StartRunLobbyPlayer` 的伪造程序集（可用 `System.Reflection.Emit` 或直接传入 `typeof(object).Assembly` 让第 1 项失败）返回不可用；`CreateCurrent` 在不可用时为 0..0；`IsCreateProtocolSelectable` 在 offer 为 0..0 时 tail 不可选；`CreateRoomIntent.Validate` 兜底抛错。全部测试通过。

## 6. 改动五：真实加载检查工具

新目录：`sts2-lan-connect/tools/GameAbiLoadCheck/`（独立 net9.0 控制台工程 `GameAbiLoadCheck.csproj`，不要加进 `STS2-Game-Lobby.sln` 的默认构建，也不要被 mod 工程引用）。

- 用法：`dotnet run --project sts2-lan-connect/tools/GameAbiLoadCheck -- <mod dll 路径> <游戏 data 目录>`。
- 实现：自定义 `AssemblyLoadContext(isCollectible: false)`，`Load(AssemblyName)` 时在 data 目录里按 `<Name>.dll` 查找（覆盖 `sts2`、`0Harmony`、`Steamworks.NET`、`GodotSharp`、`GodotSharpEditor` 以及目录里其他 dll），找不到返回 null 让默认上下文处理。`LoadFromAssemblyPath(modDll)` 后调用 `GetTypes()`；捕获 `ReflectionTypeLoadException`，逐条打印 `LoaderExceptions` 的消息；成功则打印类型总数。退出码：0 成功，1 有加载失败，2 参数错误。不要执行任何类型的静态构造函数（`GetTypes()` 本身不会触发）。
- 新脚本：`scripts/verify-game-abi-load.sh [--data-dir <dir>] [--dll <path>]`，默认 dll 为 `sts2-lan-connect/release/.build_mod_output/sts2_lan_connect/sts2_lan_connect.dll`（不存在则回退 `releases/sts2_lan_connect/sts2_lan_connect.dll`），默认 data 目录按 macOS arm64 本机安装路径 `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64`。
- 完成标准：对本机 0.111.0 data 目录运行，退出码 0，打印类型数。（0.107.1 目录由后续步骤提供，工具需支持直接换目录。）

## 7. 执行顺序与验证命令

1. 写第 2 节测试 → `dotnet test sts2-lan-connect.Tests --filter FullyQualifiedName~LanConnectGameAbiTypeLoadContractTests` **必须失败**，把失败输出记进本文末尾的"执行记录"。
2. 第 3 节 → 第 4 节 → 重跑上一步，直到通过。
3. 第 5 节 + 单元测试。
4. 第 6 节工具 + 脚本，对 0.111.0 跑通。
5. 全量：`dotnet build sts2-lan-connect/sts2_lan_connect.csproj -c Release` 无警告级错误；`dotnet test sts2-lan-connect.Tests` 全绿；`./scripts/verify-game-abi-load.sh` 退出码 0。
6. 不要提交（不执行 `git commit`）；把改动文件清单、测试输出摘要、工具输出写到本文末尾"执行记录"一节。

## 8. 约束

- 仓库约定见根目录 `AGENTS.md` 与 `sts2-lan-connect/AGENTS.md`、`sts2-lan-connect/Scripts/Lobby/AGENTS.md`：C# 保持 `LanConnect*` 命名与 `Sts2LanConnect.Scripts` 命名空间；用户可见文案用中文；不要改 `releases/` 下的镜像文件。
- 不要为了绕过契约测试而放宽黑名单；不要给 mod 工程加新的 NuGet 依赖。
- 任何地方都不得新增对 `StartRunLobbyPlayer` / `LoadRunLobbyPlayer` / `IClientConnectionInitializer` / `INetClientGameService` 的类型级引用（字段、基类、接口、泛型基类实参）。

## 执行记录

执行日期：2026-08-27。执行环境：macOS arm64，dotnet SDK 9.0.311，本机游戏为 0.111.0（`data_sts2_macos_arm64`）。全程未执行 `git commit`，未改 `releases/`，未改版本号，未改协议线格式与异常文本（golden vector 测试全部保持通过）。

### 一、改动文件清单

新增：

| 文件 | 说明 |
|---|---|
| `sts2-lan-connect.Tests/Packaging/LanConnectGameAbiTypeLoadContractTests.cs` | 第 2 节元数据契约测试（System.Reflection.Metadata 实现 `ISignatureTypeProvider<string, object?>`） |
| `sts2-lan-connect.Tests/Protocol/LanConnectTailRuntimeSupportTests.cs` | 第 5 节单元测试（7 个 Fact：探测不可用、offer 0..0、tail 不可选、Validate 兜底等） |
| `sts2-lan-connect/Scripts/Protocol/LanConnectTailPlayerAccessors.cs` | `LanConnectTailPlayerAccessors<TPlayer>`，表达式树按成员名构建 id/slotId 访问器 |
| `sts2-lan-connect/Scripts/Protocol/LanConnectTailRuntimeSupport.cs` | tail 运行时探测器 + `SetForTesting/ResetForTesting` 缓存 |
| `sts2-lan-connect/tools/GameAbiLoadCheck/GameAbiLoadCheck.csproj` | 独立诊断工具工程（未加入 sln） |
| `sts2-lan-connect/tools/GameAbiLoadCheck/Program.cs` | ALC 按 data 目录 `<Name>.dll` 解析依赖并做真实 `GetTypes()` |
| `scripts/verify-game-abi-load.sh` | 校验脚本，支持 `--data-dir/--dll` |

修改：

| 文件 | 说明 |
|---|---|
| `sts2-lan-connect/sts2_lan_connect.csproj` | 显式 `Compile Remove="tools/GameAbiLoadCheck/**"`，防止 Godot SDK 默认通配把工具源码/生成物编进 mod 程序集（见第四节坑 4） |
| `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyJoinFlow.cs` | `RitsuSidecarENetClientConnectionInitializer` 去掉 `: IClientConnectionInitializer`，`Connect(NetClientGameService,…)` 直接用具体类并删除 `is not NetClientGameService` 分支；`initializer` 变量改 `object`；JoinAsync 在 `ToValidatedValue` 前对 tail 房间返回中文提示 |
| `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs` | `BeginAsync/ConnectAsync` 参数改 `object`；`ConnectAsync` 先按 `initializer.GetType()` 解析，抛 `MissingMethodException` 再回退 `typeof(IClientConnectionInitializer)`（保留三个既有测试的行为不变） |
| `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs` | `IsCreateProtocolSelectable` tail 分支追加 `&& offer.Supports(TailLanProtocolVersion)`；`GetProtocolProfileDescription` 增加 `bool tailRuntimeAvailable` 重载，不可用时文案改为「仅支持 0.6+；当前游戏版本不支持该协议，请使用兼容模式」；`CanJoinRoom` 对 tail 房间在运行时不可用时给出明确中文 reason |
| `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessageRuntime.cs` | 名册区域泛型化：`ProjectPlayers<TPlayer>`、`RestorePlayers<TPlayer>`、`RestoreJoinedPlayer<TPlayer>`、`RestoreLoadJoinPlayers<TPlayer>`、`BuildLoadJoinCarriers<TPlayer>`、`ResolvePlayerAccessors<TPlayer>`（`ConcurrentDictionary<Type, object>` 缓存）；所有涉及 `StartRunLobbyPlayer/LoadRunLobbyPlayer` 的 lambda 全部移入以 `TPlayer` 为类型参数的泛型方法 |
| `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolOffer.cs` | `CreateCurrent()` 在 `!LanConnectTailRuntimeSupport.IsAvailable` 时 offer 为 0..0 |
| `sts2-lan-connect/Scripts/Protocol/LanConnectCreateRoomIntent.cs` | `Profile == TailV1 && !Offer.Supports(...)` 抛 `lan_protocol_version_mismatch`，详情 `"Tail runtime is unavailable on this game version."` |
| `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectProtocolPatchDispatcher.cs` | serialization 补丁之后仅在 `LanConnectTailRuntimeSupport.IsAvailable` 时 apply tail 补丁，否则打一条 skip 日志（`_applied = true` 照常） |
| `sts2-lan-connect.Tests/Lobby/LanConnectLobbyManagedJoinFlowTests.cs` | 追加 `Resolves_duck_typed_concrete_initializer_that_implements_no_interface`（无接口类的 `Task<DuckConnectResult?> Connect(TestNetService, CancellationToken)` 可被 `ResolveCompatibleConnectMethod` 解析到具体类参数路径） |
| `sts2-lan-connect.Tests/Lobby/LanConnectRitsuLibLobbyCompatibilityTests.cs` | 源码顺序断言的标记串随实现改名同步（`beforeConnect(concrete);` → `beforeConnect(netService);` 等），断言意图（Prepare→Connect→Activate 顺序）不变 |

### 二、契约测试：改动前失败输出

执行第 7 节步骤 1 时（改动三之前），`dotnet test sts2-lan-connect.Tests --filter FullyQualifiedName~LanConnectGameAbiTypeLoadContractTests` 两测均失败，第一测报出 **29 处**类型加载层面的黑名单引用，与第 0 节审计表完全对应：

```
失败 Sts2LanConnect.Tests.Packaging.LanConnectGameAbiTypeLoadContractTests.No_typedef_references_blacklisted_game_types_at_type_load_level
错误消息:
 mod 程序集存在类型加载层面的黑名单游戏类型引用（29 处）：
Sts2LanConnect.Scripts.LanConnectLobbyJoinFlow/RitsuSidecarENetClientConnectionInitializer [iface] -> sts2::MegaCrit.Sts2.Core.Multiplayer.Connection.IClientConnectionInitializer
Sts2LanConnect.Scripts.LanConnectLobbyJoinFlow/<JoinAsync>d__0 [field:<initializer>5__21] -> sts2::MegaCrit.Sts2.Core.Multiplayer.Connection.IClientConnectionInitializer
Sts2LanConnect.Scripts.LanConnectLobbyManagedJoinFlow/<BeginAsync>d__22 [field:initializer] -> sts2::…IClientConnectionInitializer
Sts2LanConnect.Scripts.LanConnectLobbyManagedJoinFlow/<ConnectAsync>d__23 [field:initializer] -> sts2::…IClientConnectionInitializer
Sts2LanConnect.Scripts.LanConnectTailMessageRuntime/<>c [field:<>9__53_2] … Func`2<…LoadRunLobbyPlayer,UInt64>
Sts2LanConnect.Scripts.LanConnectTailMessageRuntime/<>c [field:<>9__53_3] … Func`2<…LoadRunLobbyPlayer,UInt64>
Sts2LanConnect.Scripts.LanConnectTailMessageRuntime/<>c [field:<>9__55_0..56_11 共 17 个静态 lambda 缓存字段] … StartRunLobbyPlayer 各签名（含 UInt64/Int32/Tuple/LanConnectRosterProjectionItem`1<…> 组合）
Sts2LanConnect.Scripts.LanConnectTailMessageRuntime/<>c__DisplayClass47_1 [field:player] -> sts2::…LoadRunLobbyPlayer
Sts2LanConnect.Scripts.LanConnectTailMessageRuntime/<>c__DisplayClass52_0 [field:restored] -> sts2::…StartRunLobbyPlayer
Sts2LanConnect.Scripts.LanConnectTailMessageRuntime/<>c__DisplayClass53_1 [field:player] -> sts2::…LoadRunLobbyPlayer
Sts2LanConnect.Scripts.LanConnectTailMessageRuntime/<>c__DisplayClass56_0 [field:players] -> IReadOnlyList`1<…StartRunLobbyPlayer>
Sts2LanConnect.Scripts.LanConnectTailMessageRuntime/<>c__DisplayClass56_1 [field:player] -> sts2::…StartRunLobbyPlayer
Sts2LanConnect.Scripts.LanConnectLobbyJoinFlow/RitsuSidecarENetClientConnectionInitializer/<Connect>d__6 [field:netService] -> sts2::…INetClientGameService
失败 Sts2LanConnect.Tests.Packaging.LanConnectGameAbiTypeLoadContractTests.No_typedef_implements_iclientconnectioninitializer
错误消息:
 不应有任何 TypeDef 实现游戏接口 IClientConnectionInitializer（两版本间签名漂移）：
Sts2LanConnect.Scripts.LanConnectLobbyJoinFlow/RitsuSidecarENetClientConnectionInitializer [iface] -> sts2::MegaCrit.Sts2.Core.Multiplayer.Connection.IClientConnectionInitializer
失败! - 失败: 2，通过: 0，总计: 2
```

（`…` 为本次记录誊写省略的全名后缀，原始输出中均为完整签名，形如 `Func\`3<sts2::MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer,System.Int32,System.Runtime::System.ValueTuple\`2<…,Int32>>`。）

### 三、契约测试：改动后通过确认

```
$ dotnet test sts2-lan-connect.Tests --filter FullyQualifiedName~LanConnectGameAbiTypeLoadContractTests
已通过! - 失败: 0，通过: 2，已跳过: 0，总计: 2
```

### 四、执行过程中的实现要点与坑（供后续维护者参考）

1. SRM API 名称与预期不同：字段签名的正确解码入口是实例化 `System.Reflection.Metadata.Ecma335.SignatureDecoder<TType,TGenericContext>(provider, reader, genericContext).DecodeFieldSignature(ref blobReader)`（会自行校验字段调用约定头）；`ArrayShape.Rank` 是属性不是枚举值。
2. 接口类型（无基类）的 `TypeDefinition.BaseType` 是 NIL EntityHandle 但 `Kind == TypeDefinition`，渲染前必须先判 `IsNil`，否则 `BadImageFormatException: Read out of bounds`。
3. 测试宿主进程没有 sts2.dll：`LanConnectTailMessagePatches` 的静态构造含 `typeof(NetMessageBus)`，进入该类的任何反射/JIT 路径都会 FileNotFound。因此 `Probe` 自包含地复用 kind→类型名判定逻辑（只依赖纯净的 `LanConnectTailMessageTypeMatrix`），不直接调用 PatchPlan；`Current` 也改为按简单名 `"sts2"` 从 AppDomain/`Assembly.Load` 解析程序集，缺失时给出 unavailable 结论而非抛错。
4. 新增 `tools/GameAbiLoadCheck` 后曾导致 mod 工程构建报 CS0579 特性重复：Godot.NET.Sdk 的默认 Compile 通配把工具工程 `obj/**.AssemblyInfo.cs` 吸进 mod 程序集。修复即在 mod csproj 中 `<Compile Remove="tools/GameAbiLoadCheck/**/*.cs">`。任何未来放入 mod 子树的独立 csproj 都要做同样排除。

### 五、最终验证结果（计划第 7 节步骤 5）

```
$ dotnet build sts2-lan-connect/sts2_lan_connect.csproj -c Release -m:1
0 个警告 / 0 个错误

$ dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj
已通过! - 失败: 0，通过: 1137，已跳过: 1，总计: 1138，持续时间: 2 s
（基线为 通过 1127 / 失败 0 / 跳过 1；净增测试：契约 2 + duck-type 1 + tail 支持 7）

$ dotnet test sts2-lan-connect.ProtocolPlanTests/sts2_lan_connect.ProtocolPlanTests.csproj -m:1   （额外回归保险）
已通过! - 失败: 0，通过: 12，已跳过: 0，总计: 12
```

### 六、真实加载检查工具输出（计划第 6 节完成标准）

```
$ ./scripts/verify-game-abi-load.sh
mod dll:      /Users/mac/Desktop/STS2-Game-Lobby/sts2-lan-connect/release/.build_mod_output/sts2_lan_connect/sts2_lan_connect.dll
game data:    /Users/mac/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64
abi load OK: 1199 types loaded.
（退出码 0，对象为本机 0.111.0 data 目录 + 本次 Release 构建的 mod dll）
```

失败分支也已人工验证：传入不含游戏 dll 的空目录时打印 `abi load FAILED: only 1049/1199 types materialized.` 及逐条 LoaderExceptions（GodotSharp/sts2/Steamworks.NET/0Harmony 缺失），退出码 1；`--dll` 指向不存在文件时退出码 2。0.107.1 目录到位后可直接换 `--data-dir` 运行。

### 第二轮：PeerVersionInfo 兼容路径（计划第 9 节）

执行日期：2026-08-27。约束不变：未改版本号、未动 `releases/`、未 `git commit`。

#### 改动文件清单

新增：

| 文件 | 说明 |
|---|---|
| `sts2-lan-connect/Scripts/LanConnectNetGameServiceFactory.cs` | 9.1 纯反射工厂：先找“恰好一个参数、参数类型简单名 `PeerVersionInfo`”的公共构造，且该参数类型有 `public static LocalDefault()` 则 `ctor.Invoke([LocalDefault()])`；否则退回无参构造；都没有抛 `MissingMethodException` 并在消息里列出实际存在的构造签名。策略按 `ConcurrentDictionary<Type, Func<object>>` 缓存，提供 `ResetForTesting()`。方法体内只有名字字符串常量 `"PeerVersionInfo"` / `"LocalDefault"`，无该标识符引用 |
| `sts2-lan-connect.Tests/LanHost/LanConnectNetGameServiceFactoryTests.cs` | 9.3 五个 Fact：带 LocalDefault 的同名假类型走带参构造且传入值即 `LocalDefault()` 返回（单例 `Same` 断言）；仅有无参构造时走无参；有 `(PeerVersionInfo)` 构造但假类型缺 `LocalDefault()` 时退回无参；两者都不可用时抛 `MissingMethodException` 且消息含实际构造签名列表；策略缓存可 `ResetForTesting` 后重建。假版本信息类型用嵌套容器类 `Alt` 提供第二套简单名为 `PeerVersionInfo` 但无 `LocalDefault()` 的类型 |

修改：

| 文件 | 说明 |
|---|---|
| `sts2-lan-connect/Scripts/LanConnectHostFlow.cs` | 第 55、157 行两处 `new NetHostGameService(PeerVersionInfo.LocalDefault())` → `LanConnectNetGameServiceFactory.CreateHost()` |
| `sts2-lan-connect/Scripts/Lobby/LanConnectMultiplayerSaveCompatibility.cs` | 第 154 行 → `CreateHost()` |
| `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs` | 第 74 行 `new NetClientGameService(PeerVersionInfo.LocalDefault())` → `LanConnectNetGameServiceFactory.CreateClient()` |
| `sts2-lan-connect.Tests/Packaging/LanConnectGameAbiTypeLoadContractTests.cs` | 9.4：黑名单追加 `MegaCrit.Sts2.Core.Multiplayer.PeerVersionInfo`（类型加载层面禁止出现在字段/基类/接口签名）；追加 Fact `No_memberref_constructs_game_services_with_peer_version_info_or_calls_local_default` 扫 MemberRef 表——不存在 `Parent=sts2::…Net{Host,Client}GameService` 且名 `.ctor` 且参数含 PeerVersionInfo 的 MemberRef，也不存在 `Parent=sts2::MegaCrit.Sts2.Core.Multiplayer.PeerVersionInfo` 名 `LocalDefault` 的 MemberRef |

替换后 `grep -rn "PeerVersionInfo" sts2-lan-connect/Scripts` 只命中：`LanConnectPeerVersionInfoPatches.cs`（允许）、`LanConnectGameplayPatches.cs:28` 组名字符串（允许）、以及新工厂文件内的字符串常量与注释（非标识符引用）。全仓已无任何 `new NetHostGameService(` / `new NetClientGameService(` 直接调用。

#### 哨兵断言的红-绿真实性验证

临时把一处调用点写回 `new NetHostGameService(PeerVersionInfo.LocalDefault())` 后重跑 MemberRef Fact：

```
No_memberref_constructs_game_services_with_peer_version_info_or_calls_local_default [FAIL]
 mod 程序集存在对游戏版本信息构造路径的 MemberRef 调用（0.107.1 上 JIT 即抛异常）：
sts2::MegaCrit.Sts2.Core.Multiplayer.PeerVersionInfo::LocalDefault()
sts2::MegaCrit.Sts2.Core.Multiplayer.NetHostGameService::ctor(PeerVersionInfo)
```

两类违规均被精确抓到后还原为工厂调用，测试回到绿。（`LanConnectPeerVersionInfoPatches` 的 `typeof(PeerVersionInfo)` + AccessTools 路径只产生 TypeRef 与字符串常量，未被误报，与 9.4 预期一致。）

#### 关于 9.3 第 4 条的实现说明

计划预设“测试进程引用了本机 0.111 的 sts2.dll”；但本仓库 Tests 工程历史上不直接引用 sts2.dll（第一轮执行记录第四节坑 3：xUnit 宿主进程没有 sts2.dll），因此 `typeof(NetHostGameService)` 在测试宿主内即 FileNotFound，连降级版的 “ResolveStrategy 断言”也无法直接构造真实 Type。真实路径由两层已有机制覆盖：
1. 本轮新增的 MemberRef 契约 Fact（元数据级，见上）；
2. `./scripts/verify-game-abi-load.sh` 对本机 0.111.0 data 目录做真实 `GetTypes()` 加载检查。

#### 最终验证结果

```
$ dotnet build sts2-lan-connect/sts2_lan_connect.csproj -c Release -m:1
0 个警告 / 0 个错误

$ dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj
已通过! - 失败: 0，通过: 1143，已跳过: 1，总计: 1144，持续时间: 2 s
（上轮 1137；净增：工厂测试 5 + 契约 MemberRef Fact 1）

$ dotnet test sts2-lan-connect.ProtocolPlanTests/sts2_lan_connect.ProtocolPlanTests.csproj -m:1 （额外回归保险）
已通过! - 失败: 0，通过: 12，已跳过: 0，总计: 12

$ ./scripts/verify-game-abi-load.sh
mod dll:      /Users/mac/Desktop/STS2-Game-Lobby/sts2-lan-connect/release/.build_mod_output/sts2_lan_connect/sts2_lan_connect.dll
game data:    …/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64   （本机 0.111.0）
abi load OK: 1204 types loaded.
（退出码 0；1204 = 上轮 1199 + 工厂/契约新增类型）
```

#### 实现要点备忘

1. 计划要求工厂只匹配**公共**构造（与真实游戏类型的 public ctor 一致）；首轮测试服务类是 private class 且构造默认 private，导致全部落入异常分支——测试类型需显式 `public` 构造。
2. 嵌套类的反射 `Name` 即简单名，因此用嵌套容器类就能在同一文件内提供第二套简单名为 `PeerVersionInfo` 的假类型，无需另开命名空间（Tests 工程是 file-scoped namespace，不能混用 namespace 块）。
3. `MissingMethodException` 采用单参构造以便 `Message` 可控地包含完整构造函数签名列表（双参形式的 Message 表现依赖运行时拼接）。

### 第三轮：HostNetId 隔离

执行日期：2026-08-27。约束不变：未改版本号、未动 `releases/`、未 `git commit`。

#### 问题

`LanConnectLobbyManagedJoinFlow.OnDisconnected(NetErrorInfo info)` 方法体里直接访问 `NetService.HostNetId` 并调用 `LanConnectTailMessageRuntime.Shared.TryTakeValidatedRejection(...)`。游戏 0.107.1 无从确认有 `NetClientGameService.get_HostNetId`（v0.5.6 发布 DLL 从未引用它），而 OnDisconnected 在 compat 模式加入断线时同样会被 JIT——成员缺失即抛 `MissingMethodException`，导致加入失败场景雪上加霜。

#### 改动文件清单

| 文件 | 说明 |
|---|---|
| `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs` | 新增私有方法 `TryTakeTailRejection(out LanConnectProtocolFailure? failure)`：先判 `_protocolSelection?.Profile == LanConnectProtocolProfile.TailV1 && NetService != null`，然后才执行原有的 `TryTakeValidatedRejection(NetService, NetService.HostNetId, out failure)` 调用；`OnDisconnected` 改为只调用该方法。0.107.1 上 OnDisconnected 体内不再出现任何 tail / HostNetId MemberRef，永远不会 JIT 失败路径。0.111 tail 会话行为逐字保持不变；compat 会话还顺带消除了原代码在无 Tail binding 时 `RequireBinding` 抛 `InvalidOperationException` 的隐患 |

改动后 `grep -rn "HostNetId" sts2-lan-connect/Scripts` 收敛情况：

| 命中 | 定性 |
|---|---|
| `Lobby/LanConnectRoomManagementPanel.cs:288/:519` | 自定义方法 `GetHostNetId()`，硬编码返回 ENet host 自身 NetId=1，不触碰 `HostNetId` 属性；兼容模式可用，无需处理 |
| `Lobby/LanConnectLobbyJoinFlow.cs:145/:148` | RitsuLib sidecar 的 before/afterConnect lambda，仅 tail_v1 + RitsuLib 路径 JIT |
| `Protocol/Patches/LanConnectTailMessageRuntime.cs:147/:1603` | tail 运行时内部（BindClientHostSidecarFlow / GetHostPeerId），仅 tail 会话 JIT |
| `Lobby/LanConnectLobbyManagedJoinFlow.cs:610` | 本轮新方法 `TryTakeTailRejection` 内部，受 TailV1 profile 门禁保护 |

无非 tail 路径的 `NetClientGameService.HostNetId` 属性访问残留。

#### 验证结果

```
$ dotnet build sts2-lan-connect/sts2_lan_connect.csproj -c Release -m:1
0 个警告 / 0 个错误

$ dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj
已通过! - 失败: 0，通过: 1143，已跳过: 1，总计: 1144   （纯行为隔离改动，无新增测试）

$ ./scripts/verify-game-abi-load.sh
abi load OK: 1204 types loaded.
（退出码 0，对象为本机 0.111.0 data 目录 + 本次 Release 构建）
```

## 9. 追加改动（第二轮）：兼容路径上的 `PeerVersionInfo` 直接调用

审阅第一轮结果时，用元数据工具对比了 v0.5.6-rc4 发布 DLL（已知可在 0.107.1 运行）与本次构建的所有 `sts2` 成员引用，发现一组**方法体级**（JIT 期）风险，第一轮的类型加载契约测试覆盖不到：

- `docs/RELEASE_NOTES_V0.5.5_CLIENT_ZH.md:8` 记载：游戏 **0.110.x 才引入 `PeerVersionInfo`**；0.107.1 没有这个类型。
- 0.5.6 用无参构造 `new NetHostGameService()` / `new NetClientGameService()`，并把 `PeerVersionInfo.LocalDefault` 的补丁放在 `TryApplyGroup("PeerVersionInfo", …)` 里允许失败。
- 0.6.0 在**兼容路径**上直接写了 `new NetHostGameService(PeerVersionInfo.LocalDefault())`：
  - `sts2-lan-connect/Scripts/LanConnectHostFlow.cs:55` 与 `:157`
  - `sts2-lan-connect/Scripts/Lobby/LanConnectMultiplayerSaveCompatibility.cs:154`
  - `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs:74`（`new NetClientGameService(PeerVersionInfo.LocalDefault())`）

  在 0.107.1 上，包含这些语句的方法一被 JIT 就会抛 `TypeLoadException`（PeerVersionInfo 不存在）或 `MissingMethodException`（若无该构造重载）——mod 能加载，但**建房 / 续局重开 / 加入房间全部失败**。这与本计划的目标 2、3 直接冲突，必须一并修。

### 9.1 新文件 `sts2-lan-connect/Scripts/LanConnectNetGameServiceFactory.cs`

```csharp
internal static class LanConnectNetGameServiceFactory
{
    public static NetHostGameService CreateHost() => (NetHostGameService)Create(typeof(NetHostGameService));
    public static NetClientGameService CreateClient() => (NetClientGameService)Create(typeof(NetClientGameService));

    // 纯反射，方法体内不得出现 PeerVersionInfo 这个标识符（它在 0.107.1 不存在）：
    //   1) 在 serviceType 的公共构造函数里找"恰好一个参数、参数类型简单名为 PeerVersionInfo"的重载；
    //      若找到，且该参数类型有 public static LocalDefault() 无参方法，则 ctor.Invoke([LocalDefault()]);
    //   2) 否则退回无参构造 serviceType.GetConstructor(Type.EmptyTypes)；两者都没有时抛 MissingMethodException，
    //      消息里列出实际存在的构造函数签名。
    //   3) 结果按 serviceType 缓存"选中的构造策略"（ConcurrentDictionary<Type, Func<object>>），避免每次反射。
    internal static object Create(Type serviceType);
    internal static void ResetForTesting();
}
```

约束：`LanConnectNetGameServiceFactory` 的任何字段 / 签名 / 方法体都不得出现 `PeerVersionInfo`；在第 2 节契约测试的黑名单里追加 `MegaCrit.Sts2.Core.Multiplayer.PeerVersionInfo`（类型加载层面同样不允许出现在字段 / 基类 / 接口上；`LanConnectPeerVersionInfoPatches` 现有的 `LocalDefaultPatchHolder` 只在方法签名 / 静态字段初始化器里用它——静态字段 `LocalDefaultMethod` 类型是 `MethodInfo`，`LocalDefaultPostfix(ref PeerVersionInfo)` 是参数类型，都不属于类型加载层面，不需要动）。

### 9.2 替换四个调用点

上面列出的 4 处分别改为 `LanConnectNetGameServiceFactory.CreateHost()` / `CreateClient()`。其余 `PeerVersionInfo` 引用只允许留在 `LanConnectPeerVersionInfoPatches.cs`（已有 `TryApplyGroup` 容错）与 `LanConnectLobbyHandshakeCompatibility.cs`（运行时识别新旧握手结构）里。完成后 `grep -rn "PeerVersionInfo" sts2-lan-connect/Scripts` 只能命中这两个文件和 `LanConnectGameplayPatches.cs:28` 的组名字符串。

### 9.3 测试

`sts2-lan-connect.Tests/LanHost/LanConnectNetGameServiceFactoryTests.cs`（或放到已有的 LanHost 目录下合适位置）：

1. 对一个测试用类型（同时有 `(FakePeerVersionInfo)` 与 `()` 两个公共构造，`FakePeerVersionInfo` 有 `public static FakePeerVersionInfo LocalDefault()`）：`Create` 选择带参构造，且传入的是 `LocalDefault()` 的返回值。注意：`Create` 按**参数类型简单名 == "PeerVersionInfo"** 匹配，测试里的假类型名必须叫 `PeerVersionInfo`（可放在测试命名空间里）。
2. 对只有无参构造的类型：`Create` 走无参构造。
3. 对有 `(PeerVersionInfo)` 构造但该类型没有 `LocalDefault()` 的情况：退回无参构造；两者都没有时抛 `MissingMethodException`。
4. 对真实的 `NetHostGameService` / `NetClientGameService`（测试进程引用了本机 0.111 的 sts2.dll）：`CreateHost()` / `CreateClient()` 返回非空实例——如果在 xUnit 宿主里构造这两个类型会触发 Godot 原生依赖而失败，就把这一条降级为"`ResolveStrategy(typeof(NetHostGameService))` 选中的是带 PeerVersionInfo 参数的构造"的断言，不要真的构造。

### 9.4 元数据回归保护

在 `LanConnectGameAbiTypeLoadContractTests` 里追加一个 `[Fact]`：扫描程序集的 **MemberRef** 表，断言不存在 `Parent` 为 `sts2::MegaCrit.Sts2.Core.Multiplayer.NetHostGameService` / `NetClientGameService` 且名为 `.ctor` 且参数表含 `PeerVersionInfo` 的 MemberRef，也不存在 `Parent` 为 `sts2::MegaCrit.Sts2.Core.Multiplayer.PeerVersionInfo`、名为 `LocalDefault` 的 MemberRef。这样以后谁再把 `new NetHostGameService(PeerVersionInfo.LocalDefault())` 写回来，测试立刻红。（`LanConnectPeerVersionInfoPatches` 通过 `AccessTools.DeclaredMethod(typeof(PeerVersionInfo), "LocalDefault")` 取方法，不会产生 `LocalDefault` 的 MemberRef，所以不会误报；但它会产生 `PeerVersionInfo` 的 TypeRef，那是允许的。）

### 9.5 完成标准

- `dotnet test sts2-lan-connect.Tests` 全绿（含新增测试）。
- `dotnet build sts2-lan-connect/sts2_lan_connect.csproj` 后 `./scripts/verify-game-abi-load.sh` 退出码 0。
- 把本轮改动文件、测试输出追加到"执行记录"末尾，标题"第二轮：PeerVersionInfo 兼容路径"。
