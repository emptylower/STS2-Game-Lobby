# 修复：native_bus_v1 载体没有 wire 值，房主每次存档抛异常、续局绑定从未写入（后端 / 客户端协议逻辑）

## 背景（已定位，勿重新调查）

0.6.1 把 tail_v1 房间的载体改成了 `LanConnectProtocolCarrier.NativeBusV1`（枚举值 3），但
`sts2-lan-connect/Scripts/Protocol/LanConnectProtocolProfile.cs` 里的 `ToWireValue` 只映射了
`None` / `LegacyTailV1` / `LegacySidecarV1` 三个值，`NativeBusV1` 落到 `_ => throw`。
`ParseCarrier` 那一侧已经有 `"native_bus_v1" => NativeBusV1`，只是写方向漏了。

0.6.1-alpha.4 测试者日志（Windows / 0.111.0 / 新协议房间房主）：

```
[ERROR] Failed to save run: Sts2LanConnect.Scripts.LanConnectProtocolException: Unknown protocol carrier enum value 3.
   at Sts2LanConnect.Scripts.LanConnectProtocolProfileExtensions.ToWireValue(LanConnectProtocolCarrier carrier)
   at Sts2LanConnect.Scripts.LanConnectMultiplayerSaveRoomBinding.PersistHostBinding(...)
   at Sts2LanConnect.Scripts.LanConnectLobbyRuntime.PersistCurrentSaveBinding(...)
   at Sts2LanConnect.Scripts.LanConnectCurrentSaveBindingWriter.Persist(...)
   at Sts2LanConnect.Scripts.LanConnectLobbyRuntime.PersistBindingForCurrentSave(String source)
   at Sts2LanConnect.Scripts.LanConnectLobbyRuntime.OnRunSaved()
   at MegaCrit.Sts2.Core.Saves.Managers.RunSaveManager.SaveRun(SerializableRun save, Boolean isMultiplayer)
   at STS2RitsuLib.RunData.Patches.RunSavedDataPatchHelpers.EndSaveRunCaptureAfterAsync(...)
   at JmcModLib.Persistence.Run.RunPersistenceManager.AppendPersistenceAfterOriginalSaveAsync(...)
```

后果链：
1. 房主在 tail_v1 房间里每次存档，`SaveManager.Saved` 事件处理器 `OnRunSaved` 抛异常，原版 `SaveManager.SaveRun` 落到 "Failed to save run"，
   异常还沿 RitsuLib / JmcModLib 的存档后置链（QuickSL 依赖 JmcModLib 持久化）继续传播。
2. 房间绑定从未写入（`binding=missing`），续局时弹"无法确认上次是大厅还是 LAN"，选"恢复大厅房间"后走 `CreateLocalCompat`
   兜底，本机装了 RitsuLib 就被 `ritsulib_not_allowed_in_compat_mode` 拒绝。

其它 carrier switch 站点（`LanConnectCapabilityDigest.cs:33`、`LanConnectCapabilitiesCodec.cs:204/213`）已经覆盖 NativeBusV1，不用动。
lobby-service 端 `protocol-capabilities.ts` 已支持 `native_bus_v1`，不用动。

## 已写好的失败测试（先跑确认红，再改）

`sts2-lan-connect.Tests/Protocol/LanConnectProtocolProfileTests.cs` 末尾新增两条：
- `Every_carrier_round_trips_through_its_wire_value`：遍历 `Enum.GetValues<LanConnectProtocolCarrier>()`，`ParseCarrier(ToWireValue(c)) == c`。
- `Native_bus_carrier_writes_native_bus_v1`：`NativeBusV1.ToWireValue() == "native_bus_v1"`。

当前两条都红（`Unknown protocol carrier enum value 3`）。

## 要做的改动

### A. 根因（必做）

`LanConnectProtocolProfile.cs` `ToWireValue`：加一行 `LanConnectProtocolCarrier.NativeBusV1 => "native_bus_v1",`。
保持 `_ => throw` 兜底不变。

### B. 兜底：存档事件处理器永远不能把异常抛进游戏存档管线（必做）

`sts2-lan-connect/Scripts/Lobby/LanConnectLobbyRuntime.cs` 里 `OnRunSaved()`（约 2141 行）是 `SaveManager.Instance.Saved` 的订阅者。
要求：这个处理器内部任何异常都要被捕获并以 `Log.Warn` / `GD.Print` 记录（前缀 `sts2_lan_connect save_binding: persist failed source=save_event, error=...`，
带异常类型与消息，`LanConnectProtocolException` 额外带 `Failure.Code`），绝不向上抛。
做法建议：抽一个可测试的静态守卫，例如在 `Scripts/Lobby/` 新增 `LanConnectSaveEventGuard`（`internal static bool Run(string source, Action body, Action<string> log)`，
返回是否成功），`OnRunSaved` 改为经它调用 `PersistBindingForCurrentSave("save_event")`，`LanConnectSaveDiagnostics.LogNow("save_event:after_persist")` 无论成功失败都要打印。
给守卫写 xUnit：抛 `LanConnectProtocolException` 时返回 false、日志包含 code、不抛；正常时返回 true。
不要把 try/catch 加在 `PersistHostBinding` 内部（它的调用方在续局 UI 路径上需要看到失败）。

### C. CHANGELOG

`CHANGELOG.md` `## [Unreleased]` 下加 `### Fixed`，一条中文：新协议房间房主每次存档抛 `Unknown protocol carrier enum value 3`（`native_bus_v1` 载体缺少 wire 值），
续局绑定从未写入、续局时误判为兼容房并被 RitsuLib 门禁拒绝；存档事件处理器增加异常兜底，不再把 MOD 内部错误抛进原版存档管线。

## 约束

- 只改：`LanConnectProtocolProfile.cs`、`LanConnectLobbyRuntime.cs`（仅 `OnRunSaved` 附近）、新增守卫类及其测试、`CHANGELOG.md`。
- 不要改 `LanConnectHostFlow.cs`、`LanConnectDirectJoinFlow.cs`、`LanConnectProtocolSelection.cs`、lobby-service、任何版本号。
- 不要 git commit。不要动 `tem/`、`.omo/`、`.zcode/`。
- 仓库里有与本任务无关的未提交改动（`.omo/` 删除、`tem/` 未跟踪），不要碰。

## 完成标准

```bash
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj -m:1
dotnet test sts2-lan-connect.ProtocolPlanTests/sts2_lan_connect.ProtocolPlanTests.csproj -m:1
```

两个命令全绿（含新加的测试）。完成后用简短中文汇报改动文件与测试结果。
