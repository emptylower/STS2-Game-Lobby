# 任务 E（追加）：v0.6.2 前端文案与遗漏的版本号位点

本文件是对 `docs/superpowers/plans/2026-09-08-v0-6-2-peer-addressed-type-id.md` 的补充，
在任务 A–D 完成之后执行。约束与那份任务书完全相同（禁止 git commit / push，禁止碰
`releases/`、`tem/`、`.omo/`、`.zcode/`，不得新增错误码，不得为过测试而弱化断言）。

## 背景

0.6.2 把新协议的 `minimumClientVersion` 提到 `0.6.2-alpha.1`，并撤除了 registry fingerprint
门禁。前端有若干处**写死了 0.6.1**、以及若干处**描述的还是旧门禁语义**的用户可见文案，
必须同步调整，否则玩家看到的说明与实际行为不符。

## E1. 建房协议选择的描述文案

`sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs` 有**两处完全相同**的字符串
（约 `:2945` 的按钮构造处，与 `:6326` 的 `GetProtocolProfileDescription`）：

旧：
```
通过官方 Mod 消息注册通道传输，需 0.6.1 及以上客户端；与是否安装 RitsuLib 无关
```

新（两处都改成完全一致的这一句）：
```
通过官方 Mod 消息注册通道传输，需 0.6.2 及以上客户端；与双方是否安装 RitsuLib、Mod 列表是否相同均无关
```

「Mod 列表是否相同均无关」是 0.6.2 的核心用户可见收益，必须写进去。

同一文件里 `Compat4x5V1` 的描述（`沿用旧版联机协议，可与 0.3–0.5 旧版客户端同房；不支持 RitsuLib`）
**保持不变**。

## E2. 失败文案：拆分两个 fingerprint 错误码

`sts2-lan-connect/Scripts/Protocol/LanConnectProtocolUiMessages.cs`

目前两个码共用一条文案：

```csharp
"lan_registry_fingerprint_required" or "lan_registry_fingerprint_mismatch" =>
    "双方的联机消息注册表不一致（通常是 Mod 列表不同），无法使用新协议加入。",
```

0.6.2 之后这两个码的含义完全不同，必须拆开：

- `lan_registry_fingerprint_required` —— 本机没能算出注册表指纹或 native bus 消息 ID（注册表未就绪），
  或客户端过旧没带该字段。新文案：
  ```
  本机的联机消息注册表尚未就绪，无法使用新协议。请完整重启游戏后重试；若仍然失败，请更新 LAN Connect。
  ```

- `lan_registry_fingerprint_mismatch` —— 0.6.2 客户端**自身不再产生**该码，只可能来自**旧版服务端**
  （0.6.1 及以前的 lobby-service 仍在执行已被撤除的全表指纹门禁）。新文案要直接给出可操作的出路：
  ```
  该大厅服务端为旧版本，仍要求双方 Mod 列表完全一致。请联系服主把 lobby-service 升级到 0.6.2，或改用“兼容旧版 Mod”房间。
  ```

同时把 `lan_type_id_mismatch` 从现有的三码合并分支里**单独拆出来**（它在 0.6.2 的含义是
「对端消息 ID 寻址失败或未协商到」，不是帧格式问题）：

```csharp
"lan_type_id_mismatch" =>
    $"新协议消息寻址失败（{failure.Code}），连接已停止；请双方完整重启游戏后重试。",
"lan_native_frame_invalid" or "lan_extension_missing" =>
    $"新协议通信帧校验失败（{failure.Code}），连接已停止；请确认双方 LAN Connect 版本一致。",
```

其余分支一律不动。

## E3. 遗漏的版本号位点

1. `sts2-lan-connect/Scripts/Lobby/ModSync/LanConnectModPreflightCoordinator.cs` 约 `:32`
   的默认 offer `new(1, 1, "0.6.1-alpha.1", false, false)` → `"0.6.2-alpha.1"`。
2. `lobby-service/src/app.ts:504` 的 `tailV1MinimumClientVersion: "0.6.1-alpha.1"`
   → `"0.6.2-alpha.1"`（这是 `/probe` 的投影字段，任务书 B3 漏写了）。
3. `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolSelection.cs` 约 `:62` 的注释
   仍写着「0.6.1 起 tail_v1 完全忽略 RitsuLib 安装状态」，补一句 0.6.2 起同时不再要求
   两端消息注册表一致。

## E4. 同步更新断言这些文案的测试

1. `sts2-lan-connect.Tests/Protocol/LanConnectProtocolUiMessagesTests.cs`
   - 约 `:66`/`:69`：原本对 `required` 与 `mismatch` 断言**同一条**文案，现在必须拆成两条不同断言。
   - 约 `:92`：`Create_protocol_descriptions_match_the_alpha_ui_contract` 里的新协议描述断言更新为 E1 的新句子。
   - 如果该文件里还有断言 `lan_type_id_mismatch` 走合并分支的用例，一并按 E2 拆开。
2. `sts2-lan-connect.GdUnitTests/Lobby/LanConnectCreateProtocolDialogTests.cs` 约 `:112`
   的 `IsEqual("通过官方 Mod 消息注册通道传输，需 0.6.1 及以上客户端；与是否安装 RitsuLib 无关")`
   更新为 E1 的新句子。**该套件不要运行**（需要真实游戏程序集），只改文本。

## E5. 测试夹具里的 clientVersion 扫尾

`lobby-service` 的多个测试文件把 `"0.6.1-alpha.1"` 当作**合法的当前客户端版本**使用
（`app.integration.test.ts`、`store.test.ts`、`protocol-capabilities.test.ts` 等，几十处）。
`minimumClientVersion` 提到 `0.6.2-alpha.1` 之后，这些夹具会被判为版本过低而失败。

处理原则：
- 夹具中表示「当前/合法客户端」的 `clientVersion` / `modVersion` 一律改为 `"0.6.2-alpha.1"`。
- **专门用来验证「旧客户端被拒绝」的用例保留 `"0.6.1-alpha.1"`**，并确认其期望仍是
  426 `lan_client_version_too_old`。
- `protocol-capabilities.test.ts` 里 `assert.equal(..., "0.6.1-alpha.1")` 这类对
  `minimumClientVersion` 取值的断言，改为 `"0.6.2-alpha.1"`。

在报告里列出你改了哪些夹具、以及哪些用例是故意保留旧版本号的。

## 完成判据

与主任务书相同的三条命令必须全绿：

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby/lobby-service && npm run check && npm run test
dotnet test sts2-lan-connect.ProtocolPlanTests/sts2_lan_connect.ProtocolPlanTests.csproj -m:1
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj -m:1
```
