# 修复：native_bus 启动自检在 AssemblyInfo 未初始化时误判为终局失败（后端 / 客户端逻辑）

## 背景（已定位根因，勿重新调查）

2026-09-05 Windows 0.111.0 测试者日志（alpha.2）：启动即弹“联机协议补丁未能完整安装（通常与 RitsuLib 的补丁冲突）”，删掉 RitsuLib 仍然弹。日志：

```
[ERROR] sts2_lan_connect native_bus: DISABLED reason="InvalidOperationException: Operation is not valid due to the current state of the object."
[ERROR] sts2_lan_connect DEGRADED MODE: ... reason=protocol_patch_conflict fingerprint=native_bus_self_check:InvalidOperationException: ...
```

调用链：`Entry.Init` stage `native_bus_startup_check` → `LanConnectNativeBusStartupCheck.Run()` →
`IsRegistryAvailable()` 返回 true（该测试者的某个第三方 mod 在 mod 初始化阶段就把 `MessageTypes._cache` 建好了，
本仓库 Mac 上没有这种 mod，所以 Mac 走的是 Pending）→ `LanConnectRegistryFingerprint.Compute()` → `WriteEntry` →
`AssemblyInfo.ModForType(type, out _)` → 游戏代码 `if (ModMap == null) throw new InvalidOperationException();`
（`AssemblyInfo.Init()` 要到 `OneTimeInitialization.ExecuteEssential` 才运行，晚于所有 mod 初始化器）。
`Run()` 的 catch 把它包装成 `Result.Fail(...)`，Entry 据此进入降级模式，整个会话联机停用。

这是自检的假阳性：注册表可用 ≠ AssemblyInfo 可用。正确行为：AssemblyInfo 未就绪时返回 `Result.RegistryPending()`
（或同语义的 Pending），交给首次 tail 会话的 `EnsureReadyOrThrow` 补跑（那时 ExecuteEssential 已完成，两者都就绪）。

## 已写好的失败测试（先跑它确认红，再改代码）

`sts2-lan-connect.GdUnitTests/Protocol/NativeBus/LanConnectNativeBusStartupCheckTests.cs`
新增 `Run_with_the_registry_but_without_AssemblyInfo_returns_pending_and_never_disables`：注册表已建、`AssemblyInfo.ClearForTests()` 后 `Run()` 必须 `Pending == true && Ok == false`。当前失败（Pending 为 false）。

## 要做的改动（只改这些文件）

1. `sts2-lan-connect/Scripts/Protocol/NativeBus/LanConnectNativeBusStartupCheck.cs`
   - 在 `Run()` 里，`IsRegistryAvailable()` 之后、进入 try 之前，增加 AssemblyInfo 就绪判断：`AssemblyInfo.ModMap == null`
     （`MockTypes != null` 视为就绪，测试用）→ 返回 Pending。Pending 的 Reason 可改为更准确的文案，例如
     `"message registry or AssemblyInfo not yet initialized (deferred to first tail session)"`，
     若改文案请同步更新 `LogDiagnostics` 中依赖该文案的地方（如有）与现有测试断言。
   - 不要吞掉其他异常：catch 分支保持 `Result.Fail`，只有“未就绪”走 Pending。
2. `sts2-lan-connect/Scripts/Protocol/NativeBus/LanConnectRegistryFingerprint.cs`（可选但建议）：在 `Compute()` 开头
   若 `AssemblyInfo.ModMap == null && AssemblyInfo.MockTypes == null` 抛带明确文案的 `InvalidOperationException`
   （如 "AssemblyInfo is not initialized; the fingerprint must not be computed before OneTimeInitialization."），
   避免再出现无消息异常。
3. `CHANGELOG.md` `## [Unreleased]` 下加一条 Fixed（中文，一句话，说明 alpha.2 在存在提前初始化消息注册表的第三方 mod 时会误进降级模式）。

不要改 `Entry.cs`、`LanConnectDegradedMode`、服务端、任何前端文案。不要 git commit。

## 完成标准

```bash
export GODOT_BIN=/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot
dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings \
  --filter "FullyQualifiedName~LanConnectNativeBusStartupCheckTests"
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj -m:1
```

两条全绿。完成后用简短中文汇报：改动文件、测试结果、Pending 文案是否变更。
