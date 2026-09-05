# 修复：0.107.1 上 `AssemblyInfo` 类型不存在，启动自检直接引用导致整个 mod 初始化失败（后端 / 客户端逻辑）

## 背景（已定位，勿重新调查）

本机 0.107.1（Steam 正式分支）冒烟 alpha.3 候选：

```
[ERROR] Exception thrown when calling mod initializer of type Sts2LanConnect.Scripts.Entry: ... TypeLoadException:
Could not load type 'MegaCrit.Sts2.Core.Modding.AssemblyInfo' from assembly 'sts2, Version=0.1.0.0'
```

`MegaCrit.Sts2.Core.Modding.AssemblyInfo` 是 0.111 才有的类型（0.107.1 的 sts2.dll 里没有，fixture 已验证）。
alpha.2 只在 `LanConnectRegistryFingerprint.WriteEntry` 里引用它（该方法只在 0.111 tail 路径被 JIT），
alpha.3 的修复把 `AssemblyInfo.ModMap` / `AssemblyInfo.MockTypes` 写进了 Entry 阶段必经的
`LanConnectNativeBusStartupCheck.Run()` 和 `LanConnectRegistryFingerprint.Compute()`，0.107.1 上 JIT 这两个方法即抛
`TypeLoadException`，mod 初始化器整体失败，0.107.1 完全不可用。

仓库既定决策（project memory: "Avoid hard member references to version-drifting STS2 runtime APIs"）：
对跨版本漂移的游戏 API 使用窄反射适配器，源码中不得出现直接 MemberRef。参考现有写法：
`sts2-lan-connect/Scripts/Protocol/LanConnectTailRuntimeSupport.cs`（纯反射探测 0.111 类型）、
`sts2-lan-connect/Scripts/Lobby/LanConnectPeerVersionInfoPatches.cs`。

## 已写好的失败测试（先跑确认红，再改）

`sts2-lan-connect.Tests/Packaging/LanConnectGameAbiTypeLoadContractTests.cs` 新增
`No_memberref_targets_game_types_absent_in_0_107_1`：扫描 mod DLL 的 MemberRef 表，任何指向
`sts2::MegaCrit.Sts2.Core.Modding.AssemblyInfo` 的成员引用都算违规。当前应报 3 处左右
（`get_ModMap`、`get_MockTypes`、`ModForType`）。

注意：本机 Steam 正在切回 0.111.0；`dotnet build` 依赖已安装游戏的 sts2.dll（`Sts2DataDir` 属性）。
若编译报 `PeerVersionInfo` 找不到，说明游戏还在 0.107.1，等几分钟再跑，不要改 csproj。

## 要做的改动

1. 新增 `sts2-lan-connect/Scripts/Protocol/NativeBus/LanConnectAssemblyInfoAdapter.cs`（internal static）：
   - 通过 `typeof(MessageTypes).Assembly.GetType("MegaCrit.Sts2.Core.Modding.AssemblyInfo", throwOnError: false)` 解析类型，
     结果（含 `PropertyInfo ModMap`、`PropertyInfo MockTypes`、`MethodInfo ModForType(Type, out bool)`）用 `Lazy<>` 缓存一次。
   - `bool IsAvailable`：类型存在且三个成员都解析成功。
   - `bool IsInitialized`：`IsAvailable && (ModMap != null || MockTypes != null)`（反射读静态属性）。
   - `Mod? ModForType(Type type, out bool isBaseGame)`：反射调用；`!IsAvailable` 时抛
     `InvalidOperationException("AssemblyInfo is unavailable on this game version.")`。
     `MegaCrit.Sts2.Core.Modding.Mod` 类型在 0.107.1 存在（ModManager 同时代），可直接强类型引用；
     若不确定，用 `object?` 返回并在 `WriteEntry` 里反射读 `manifest?.id` / `manifest?.affectsGameplay`。
   - 只允许在这个文件里出现 `AssemblyInfo` 这个字符串；其它文件不得再直接引用该类型。
2. `LanConnectNativeBusStartupCheck.Run()`：把 `AssemblyInfo.ModMap == null && AssemblyInfo.MockTypes == null`
   改为 `!LanConnectAssemblyInfoAdapter.IsInitialized`；当 `!IsAvailable`（0.107.1）同样返回 `Result.AssemblyInfoPending()`
   （0.107.1 没有 tail runtime，永远不会走到 EnsureReadyOrThrow，挂起是安全的）。
3. `LanConnectRegistryFingerprint.Compute()` / `WriteEntry`：改用适配器（`IsInitialized` 守卫 + `ModForType`）。
4. GdUnit 测试项目（编译对象是 0.111，可直接用 `AssemblyInfo.Init()/ClearForTests()`）不需要改，
   但要确认 `sts2-lan-connect.GdUnitTests/Protocol/NativeBus/LanConnectNativeBusStartupCheckTests.cs` 全部仍绿。
5. `CHANGELOG.md` `## [0.6.1-alpha.3]` 的 `### Fixed` 第一条末尾补一句：
   “`AssemblyInfo` 改经反射适配器访问，保证 0.107.1（无该类型）上 mod 仍可加载；新增 MemberRef 黑名单契约测试。”

不要改 csproj、Entry.cs、服务端、前端文案。不要 git commit。

## 完成标准

```bash
export GODOT_BIN=/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj -m:1
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings \
  --filter "FullyQualifiedName~NativeBus"
```

两条全绿。完成后用简短中文汇报：改动文件、测试结果、适配器解析失败时的行为。
