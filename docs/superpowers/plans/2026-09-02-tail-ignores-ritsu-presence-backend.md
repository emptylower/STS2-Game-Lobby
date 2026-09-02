# 2026-09-02 新协议（tail_v1 / native_bus_v1）解除 RitsuLib 一致性门禁

## 背景
- 0.6.1 起 `tail_v1` 房间载体固定为 `native_bus_v1`（官方 Mod 消息注册通道），设计上**完全忽略 RitsuLib 是否安装**（见 `CHANGELOG.md` 0.6.1-alpha.1 “Changed” 与 `lobby-service/src/protocol-capabilities.ts` `selectRoomProtocol` 内注释）。客户端 tail 运行时也已放行（`LanConnectCapabilitiesCodec.ValidateCarrierPresence` 对 `NativeBusV1` 直接 return）。
- 但两处门禁仍按旧规则拒绝 `ritsulib_presence_mismatch`：
  1. 服务端 `lobby-service/src/protocol-capabilities.ts` `assertJoinerCompatible`：`selection.profile !== "compat_4_5_v1"` 时仍比较 `joinerOffer.ritsuLibPresent !== selection.ritsuLibPresent`。
  2. 客户端 `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolSelection.cs` `Validate(localOffer)` 的 `TailV1` 分支：`if (RitsuLibPresent != localOffer.RitsuLibPresent) throw RitsuLibPresenceMismatch`。
- 兼容房（`compat_4_5_v1`）继续禁止 RitsuLib（`ritsulib_not_allowed_in_compat_mode`），**不改**。

## 要做的事
1. 服务端：`assertJoinerCompatible` 在非 compat 分支删除 presence 相等检查；保留 legacy carrier 拒绝（`lan_legacy_carrier_unsupported`）、fingerprint 与 client version 门禁。`selectRoomProtocol` 与 `capabilityDigest` 的字段不变（`ritsuLibPresent` 仍记录在 selection 中，只是不再作为加入条件），以免改变已冻结房间的 digest。
2. 客户端：`LanConnectProtocolSelection.Validate` 的 `TailV1` 分支删除 presence 相等检查；其余检查不动。`ritsulib_presence_mismatch` 错误码、`LanConnectProtocolFailure.RitsuLibPresenceMismatch` 工厂、RejectionCodec 索引都**保留**（旧服务端/旧房间仍可能返回它）。
3. 测试同步：
   - `lobby-service/src/protocol-capabilities.test.ts`（约 25-37 行：presence 交叉场景改为 `doesNotThrow`，用例名同步）。
   - `lobby-service/src/store.test.ts`（约 309-330 行 “Tail join gate chain …” 的两条 presence 场景改为 `code: null`，即应成功签发工单；如该用例统计了 id 分配次数，按成功路径调整）。
   - `lobby-service/src/app.integration.test.ts`（约 523-530 行两条 `ritsulib_presence_mismatch` 场景改为期望 200/成功，或删掉并在同文件另加一条“presence 不一致仍可加入 tail 房间”的用例）。
   - 客户端 xUnit 中凡断言 `Validate`/`ToValidatedValue` 在 TailV1 下因 presence 不一致抛错的用例改为断言通过（先 `grep -rn "RitsuLibPresenceMismatch\|ritsulib_presence_mismatch" sts2-lan-connect.Tests`，只改与 `LanConnectProtocolSelection.Validate` 语义相关的；编码/UI/重试策略类用例保留）。
4. 在 `lobby-service/src/protocol-capabilities.ts` 与 `LanConnectProtocolSelection.cs` 改动处各留一行中文注释说明“native_bus_v1 不再要求 RitsuLib presence 一致”。

## 允许修改的文件（其它文件一律不动）
- `lobby-service/src/protocol-capabilities.ts`、`lobby-service/src/protocol-capabilities.test.ts`、`lobby-service/src/store.test.ts`、`lobby-service/src/app.integration.test.ts`（如 `store.ts` 里有同样的 presence 判断也可改，先 grep）
- `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolSelection.cs`
- `sts2-lan-connect.Tests/Protocol/**`（**除** `LanConnectProtocolUiMessagesTests.cs`，另一位同事在改）
- 禁止：`git commit`/`git push`/`git stash`；禁止改 `sts2-lan-connect/Scripts/Lobby/**`、`LanConnectProtocolUiMessages.cs`、`CHANGELOG.md`、`docs/**`、任何 GdUnit 测试。

## 完成标准
1. `cd lobby-service && npm test` 全绿（会先 `tsc` 编译再跑 node --test）。
2. `dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj -m:1` 全绿。
3. `dotnet test sts2-lan-connect.ProtocolPlanTests/sts2_lan_connect.ProtocolPlanTests.csproj -m:1` 全绿。
4. 用简短中文汇报：改了哪些文件、每条测试命令的结果、有无未完成项。
