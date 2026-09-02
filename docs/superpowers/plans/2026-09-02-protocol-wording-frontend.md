# 2026-09-02 联机协议前端措辞重构（兼容旧版 Mod / 新协议）

## 背景（开工前必读）
- 本 MOD（`sts2-lan-connect/`，C# + Godot）建房时有两套联机协议，线上 ID **不能改**：`compat_4_5_v1`（兼容）和 `tail_v1`（新协议）。
- 0.6.1 起 `tail_v1` 的载体固定为 `native_bus_v1`：通过游戏官方的 Mod 消息注册通道（自定义 `INetMessage`）传输协议容器，**不再**在原版消息尾部追加数据，**不再**经 RitsuLib 的 sidecar/内部映射 API，**与 RitsuLib 是否安装无关**。
- 但前端仍在用旧的说法：“0.6 新协议（RitsuLib 状态必须一致）”、“仅支持 0.6+；RitsuLib 状态必须一致”、房间卡片上显示 `tail_v1 / ritsulib_sidecar_v1 / 需要 RitsuLib` 等。用户要求：前端只区分“**兼容旧版 Mod**”和“**新协议**”两种，描述要反映上述事实。
- 线上标识、枚举名、服务端契约、Harmony 补丁一律不动；本任务只改玩家可见文案、房间列表展示逻辑和相关测试。

## 目标文案（照此实现，可微调语气但不改语义）
1. 建房弹窗两枚协议按钮（`LanConnectLobbyOverlay.cs` 约 2937-2947 行 `CreateProtocolChoiceButton`，及 6295-6306 行 `GetProtocolProfileDescription`）：
   - 兼容按钮：标题 `兼容旧版 Mod`；描述 `沿用旧版联机协议，可与 0.3–0.5 旧版客户端同房；不支持 RitsuLib`
   - 新协议按钮：标题 `新协议`；描述（可用时）`通过官方 Mod 消息注册通道传输，需 0.6.1 及以上客户端；与是否安装 RitsuLib 无关`
   - 新协议按钮描述（当前游戏版本不支持 tail runtime 时，即 `tailRuntimeAvailable == false`）：`需要游戏测试分支（0.111+）；当前游戏版本不支持，请选择“兼容旧版 Mod”`
   - `GetProtocolProfileDescription` 的 `_` 分支保持“当前客户端不支持该房间的联机协议。”
2. 测试桩 `CreateProtocolOptionLabelsForTests()`（约 620 行）返回 `["兼容旧版 Mod", "新协议"]`。
3. 房间卡片 / 房间详情协议摘要 `BuildRoomProtocolSummary`（约 3633 行）、`GetRoomProtocolPill`（约 3655 行）、`GetRoomRitsuPresencePill`（约 3665 行）：
   - profile 显示：`compat_4_5_v1`/`extended_8p` → `兼容旧版 Mod`；`tail_v1` → `新协议`；未知值原样显示。
   - carrier 显示：`none`/空 → `旧版协议`；`native_bus_v1` → `官方通道`；`standalone_tail_v1`、`ritsulib_sidecar_v1` → `旧载体（需房主升级）`；其他原样显示。
   - RitsuLib 提示：只在兼容房间显示 `不支持 RitsuLib`；新协议房间**不显示**任何 RitsuLib 字样（摘要变为两段：`新协议 / 官方通道`）。
   - Pill：`TAIL` → `新协议`，`COMPAT` → `兼容`。
4. 加入前门禁提示（`LanConnectLobbyOverlay.cs` 约 6617 行，`LanConnectLobbyJoinFlow.cs` 约 75 行，两处同文案）：
   `该房间使用新协议，需要游戏测试分支（0.111+），当前游戏版本 {版本} 不支持。请切换到 Steam 测试分支（public-beta），或加入“兼容旧版 Mod”房间。`
5. `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolUiMessages.cs`：
   - `ritsulib_not_allowed_in_compat_mode` → `“兼容旧版 Mod”房间不能启用 RitsuLib。请关闭 RitsuLib 后重试，或改用新协议房间。`
   - `ritsulib_presence_mismatch` 保留但改为：`该房间是旧版本创建的，要求所有玩家 {启用/关闭} RitsuLib；新协议房间不再有此限制。`
   - `ritsulib_sidecar_unavailable` → `该房间使用已停用的旧版 RitsuLib 通道，请房主升级 LAN Connect 后重新建房。`
   - 新增：`lan_legacy_carrier_unsupported` → `该房间由旧版 LAN Connect 创建（旧载体），请房主升级后重新建房。`；`lan_registry_fingerprint_required`、`lan_registry_fingerprint_mismatch` → `双方的联机消息注册表不一致（通常是 Mod 列表不同），无法使用新协议加入。`；`lan_client_version_too_old` → 复用 `client_update_required` 的文案逻辑；`lan_native_frame_invalid`、`lan_type_id_mismatch`、`lan_extension_missing` → `新协议通信帧校验失败（{code}），连接已停止；请确认双方 LAN Connect 版本一致。`
   - 其余分支与 `_ =>` 兜底不变。
6. 文档同步（只改措辞）：`docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md` 第 26-29 行“双协议房间”段和第 154-155、233 行，把“0.6 新协议（RitsuLib 状态必须一致）/ standalone carrier / sidecar”说法改成与上面一致的“兼容旧版 Mod / 新协议（官方 Mod 消息注册通道，与 RitsuLib 无关）”。

## 必须同步更新的测试
- `sts2-lan-connect.Tests/Protocol/LanConnectProtocolUiMessagesTests.cs`（`Create_protocol_descriptions_match_the_alpha_ui_contract` 等，按新文案断言；新增错误码需有对应用例）。
- `sts2-lan-connect.GdUnitTests/Lobby/LanConnectCreateProtocolDialogTests.cs`（约 90-135 行：标签、描述、房间卡片摘要断言改为新文案；`room-b` 是 `tail_v1 + ritsulib_sidecar_v1 + RitsuLibPresent` 的夹具，新期望是 `新协议` / `旧载体（需房主升级）`，且不出现 “RitsuLib”）。
- 用 `grep -rn "RitsuLib 状态必须一致\|兼容旧版客户端\|0.6 新协议\|需要 RitsuLib\|无 RitsuLib\|支持 0.3-0.5\|仅支持 0.6+\|兼容模式" sts2-lan-connect sts2-lan-connect.Tests sts2-lan-connect.GdUnitTests` 复查，不得残留旧文案（日志/诊断字符串除外）。

## 允许修改的文件（其它文件一律不动）
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs`
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyJoinFlow.cs`（只改第 75-76 行的提示文案）
- `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolUiMessages.cs`
- `sts2-lan-connect.Tests/Protocol/LanConnectProtocolUiMessagesTests.cs`
- `sts2-lan-connect.GdUnitTests/Lobby/LanConnectCreateProtocolDialogTests.cs`（如别的 GdUnit 测试也断言了这些文案，可一并改）
- `docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md`
- 禁止：`git commit`/`git push`/`git stash`；禁止改 `lobby-service/**`、`sts2-lan-connect/Scripts/Protocol/LanConnectProtocolSelection.cs`、`CHANGELOG.md`、任何 `*Patch*.cs`/`Protocol/Tail/**`（另一位同事正在改后端）。

## 完成标准
1. `dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj -m:1` 全绿。
2. `dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings --filter "FullyQualifiedName~LanConnectCreateProtocolDialogTests"` 全绿（Godot Mono 在 `/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot`，runsettings 已配置；若环境跑不起来，如实报告失败原因，不要跳过）。
3. 上面的 grep 复查无残留。
4. 用简短中文汇报：改了哪些文件、每条测试命令的结果、有无未完成项。
