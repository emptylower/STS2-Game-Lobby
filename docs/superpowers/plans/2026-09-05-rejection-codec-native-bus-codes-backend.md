# 修复：tail 拒绝码表缺少 0.6.1 新错误码，运行时失败退化为原版“模组不匹配”（后端 / 客户端协议逻辑）

## 背景（已定位，勿重新调查）

0.6.1 新增了 7 个错误码，但 tail 线上拒绝条目的编码表没有同步：

- `LanConnectRejectionCodec.ReasonCodes`（`sts2-lan-connect/Scripts/Protocol/Tail/LanConnectRejectionCodec.cs`）只有 10 项；
  `Encode()` 遇到未知码直接 `throw Invalid("Cannot encode unknown rejection code ...")`。
- `LanConnectProtocolFailureMapper.TailCodes`（`sts2-lan-connect/Scripts/Protocol/LanConnectProtocolFailureMapper.cs`）有 15 项，
  但 `FromTail()` 只映射 `1..10`，且表里没有 `lan_type_id_mismatch` / `lan_extension_missing`。

后果：房主端 `LanConnectTailMessageRuntime.RejectAndDisconnect` 把失败压栈后发送 `InitialGameInfoMessage(ModMismatch)`，
序列化钩子调用 `LanConnectTailMessageProtocol.Encode(...)` 附加拒绝条目时 `Encode` 抛异常，拒绝条目附不上，
加入者只看到原版“模组不匹配”，而不是 `lan_extension_missing`（扩展帧 2000ms 未到）等具体原因。
这正是 0.111.0 测试者加入新协议房失败时“模组不匹配”的直接来源。`LanConnectProtocolUiMessages` 已有这 7 个码的中文提示。

## 已写好的失败测试（先跑确认红，再改）

- `sts2-lan-connect.Tests/Protocol/LanConnectRejectionCodecTests.cs`：`Round_trips_all_fixed_reason_codes` 新增 11..17 七行；
  `Unknown_reason_is_preserved_as_terminal_generic_protocol_failure` 改为写入 18 并期望 `unknown_tail_rejection_18`。
- `sts2-lan-connect.Tests/Protocol/LanConnectProtocolFailurePropagationTests.cs`：新增 `Native_bus_tail_rejection_codes_map_to_canonical_codes`（11..17）；
  `Unknown_tail_rejection_retains_raw_reason_code` 改为 18。

## 要做的改动（只改这些文件）

1. `LanConnectRejectionCodec.ReasonCodes`：在现有 10 项之后**按此顺序追加**（不得改动前 10 项的顺序，线上 reasonCode = index+1 是协议）：
   11 `lan_legacy_carrier_unsupported`，12 `lan_registry_fingerprint_required`，13 `lan_registry_fingerprint_mismatch`，
   14 `lan_native_frame_invalid`，15 `lan_client_version_too_old`，16 `lan_type_id_mismatch`，17 `lan_extension_missing`。
2. `LanConnectProtocolFailureMapper.TailCodes`：改为与上表**完全相同的 17 项同顺序**（现有 15 项的前 15 个顺序已一致，只需追加 16、17），
   `FromTail` 的范围改为 `>= 1 and <= TailCodes.Length`（不要再硬编码 10）。
   最好让两个类共用同一个表（例如 Mapper 引用 Codec 的 `internal static IReadOnlyList<string> ReasonCodes`），避免再次漂移；若共用，加一个 xUnit 断言两表一致。
3. `LanConnectProtocolFailure.Validate()` 若对 code 有白名单，确认这 7 个码能通过（grep `Validate` 看是否有限制；若有，补上）。
4. `CHANGELOG.md` `## [Unreleased]` 的 `### Fixed` 下加一条（中文，一句话：tail 拒绝码表补齐 0.6.1 七个错误码，运行时失败不再退化为“模组不匹配”）。

兼容性说明（写进 CHANGELOG 那句话或代码注释均可）：旧客户端收到 11..17 会解码为 `unknown_tail_rejection_N` 的通用协议失败，不会崩溃。

不要改 `LanConnectTailMessageRuntime`、服务端、前端文案。不要 git commit。

## 完成标准

```bash
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj -m:1
dotnet test sts2-lan-connect.ProtocolPlanTests/sts2_lan_connect.ProtocolPlanTests.csproj -m:1
```

全绿。完成后用简短中文汇报改动文件与测试结果。
