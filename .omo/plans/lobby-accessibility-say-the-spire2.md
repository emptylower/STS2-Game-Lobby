# 联机大厅无障碍适配计划：兼容 say-the-spire2

Date: 2026-06-11
Status: Draft for execution
Scope: `sts2-lan-connect/` client mod only

## 1. Goal

让 STS2 LAN Connect 的游戏内联机大厅在没有鼠标的情况下可用，并在安装 say-the-spire2 盲人辅助模组时可被正常朗读。

用户已确认的优先级：

1. 主目标：大厅 UI 可用键盘/手柄导航，并能通过 say-the-spire2 朗读。
2. 配套功能：快捷键接受邀请码 / 打开聊天。
3. 配套功能：剪贴板有邀请链接时，进入大厅直接弹出加入确认，不再先要求点击服务器。
4. 排除：通过配置文件创建房间或替代原生窗口。这是兜底方案；由于主路径可行，本轮不做。

## 2. Existing Facts and File Anchors

- `sts2-lan-connect/AGENTS.md:28` 要求保持 `LanConnect*` 命名和 `Sts2LanConnect.Scripts` namespace。
- `sts2-lan-connect/Scripts/Lobby/AGENTS.md:10` 标注 `LanConnectLobbyOverlay.cs` 是大厅 UI 热点，且 `sts2-lan-connect/Scripts/Lobby/AGENTS.md:22` 要求大 UI 改动保持局部化。
- `sts2-lan-connect/sts2_lan_connect.csproj:1` 使用 `Godot.NET.Sdk/4.5.1`，`sts2-lan-connect/sts2_lan_connect.csproj:4` 目标框架是 `net9.0`。
- `sts2-lan-connect/Scripts/Entry.cs:18` 已加载配置，`sts2-lan-connect/Scripts/Entry.cs:19` 已做外部模组检测，可在此附近初始化无障碍桥接。
- `sts2-lan-connect/Scripts/LanConnectExternalModDetection.cs:45` 至 `sts2-lan-connect/Scripts/LanConnectExternalModDetection.cs:76` 已有 `AppDomain.CurrentDomain.GetAssemblies()` 反射检测先例。
- `sts2-lan-connect/Scripts/Patches.MultiplayerSubmenu.cs:169` 是大厅入口；当前 `sts2-lan-connect/Scripts/Patches.MultiplayerSubmenu.cs:193` 总是先显示服务器选择器，导致剪贴板邀请确认无法先弹出。
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:220` 的 `ShowOverlay()` 会重建房间列表并调用 `CheckClipboardForInviteCode()`，但这是在服务器选择器之后。
- `sts2-lan-connect/Scripts/Lobby/LanConnectInviteCode.cs:20` 的邀请码编码包含 `serverBaseUrl`，`sts2-lan-connect/Scripts/Lobby/LanConnectInviteCode.cs:57` 校验 `S` 与 `R` 非空，因此跳过服务器选择后仍能知道目标服务器。
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:1790` 的 `RebuildRoomStage()` 会 `QueueFree()` 旧房间卡片，焦点可能在刷新/翻页时丢失。
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:2207` 创建房间卡片为 `PanelContainer`，`sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:2318` 仅连接 `GuiInput`，目前鼠标优先。
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:2363` 的房间卡片只处理鼠标点击/双击，`ui_accept` 尚无入口。
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:1463` 的 `CreateDialogShell()` 创建所有自定义弹窗骨架，但当前没有统一 Escape/focus restoration 机制。
- `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:3695` 创建邀请确认弹窗，`sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:3715` 已有“加入房间”按钮，可复用为 F7 目标。
- `sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs:438` 有 `TogglePanel()`，`sts2-lan-connect/Scripts/Lobby/LanConnectRoomChatOverlay.cs:456` 打开后会聚焦输入框。
- `sts2-lan-connect/Scripts/Lobby/LanConnectServerSelectionDialog.cs:77` 支持 Escape 关闭；`sts2-lan-connect/Scripts/Lobby/LanConnectServerSelectionDialog.cs:461` 的服务器行已经是 `Button`。
- `sts2-lan-connect/Scripts/Lobby/LobbyAnnouncementCarousel.cs:41` 起构建公告轮播，本轮不做完整无障碍化。

say-the-spire2 research summary:

- emptylower/say-the-spire2 与 bradjrenshaw/say-the-spire2 当前 `main` 0 ahead / 0 behind。
- say-the-spire2 朗读链路是 `UIManager.Update()` -> `SpeechManager.Output()`，引擎链 Prism -> Tolk -> SAPI -> Clipboard。
- 它不使用 Godot 原生无障碍树，而依赖 Godot focus + 自己的 `UIManager.SetFocusedControl(Control, UIElement? = null)`。
- 它没有稳定第三方 API；桥接必须使用反射、签名探测和熔断。

## 3. Principles

1. **先测试基建，再改 UI**：用户没有真实盲测者，必须用自动测试和诊断日志减少不可见失败。
2. **先 keyboard/focus，再 speech bridge**：focus 化本身对所有键盘/手柄用户有价值，且是朗读前提。
3. **小步改 Overlay**：`LanConnectLobbyOverlay.cs` 已超过 5600 行，禁止大重写；通过 helper 类隔离新增逻辑。
4. **软依赖，不硬绑定 say-the-spire2**：没有安装 say-the-spire2 时行为必须与今天一致。
5. **失败可诊断**：桥接失败、签名不匹配、焦点恢复失败必须有一次性日志，不能静默吞掉关键状态。

## 4. Decision Drivers

1. 盲人用户是否能从“进入大厅”到“选择服务器 / 创建房间 / 加入房间 / 聊天”完成闭环。
2. 在没有真实盲测者时，测试链路能否定位 focus、桥接、快捷键、剪贴板入口的问题。
3. 改动是否足够局部，避免破坏现有房间筛选、分页、加入、relay 和继续运行逻辑。

## 5. ADR

### Decision

采用“两层适配 + 分层测试”的方案：

- Layer A：无依赖 focus 导航改造。
- Layer B：反射软桥接 say-the-spire2 的 `UIManager.SetFocusedControl`。
- 测试链路：纯逻辑 xUnit + bounded GdUnit4 headless smoke tests + say-the-spire2 静态契约测试 + 运行时诊断日志。

### Alternatives considered

1. **只做 focus 导航，不做桥接**：键盘可动，但 say-the-spire2 不会朗读原生 Godot 控件，不能满足盲人用户核心需求。
2. **直接调用 `SpeechManager.Speak/Output`**：更直接，但属于 say-the-spire2 内部 TTS API，耦合更强，版本漂移风险更高。
3. **给 say-the-spire2 上游提 PR 后等待支持**：长期可取，但不可控，不能解决当前 issue。
4. **改用配置文件/原生窗口创建房间**：绕过 UI 而非修复 UI，且无法覆盖加入别人房间、服务器选择、聊天等主流程。

### Why chosen

Layer A 是必要前提且独立收益高；Layer B 用反射软接入满足朗读需求，同时通过签名检测和熔断控制风险。测试链路把高风险行为抽成可验证单元，GdUnit4 只覆盖 Godot runtime 必需部分，避免测试范围失控。

### Consequences

- 需要新增测试项目和 helper 类，短期工作量上升。
- say-the-spire2 更新时可能破坏桥接；契约测试和日志会帮助快速定位。
- 部分 UI（公告轮播、房主管理完整面板、玩家列表）本轮不完整无障碍化，需要后续 issue。

### Follow-ups

- 后续可给 say-the-spire2 提 PR：提供正式第三方控件注册 API 或 generic FocusOwner 监听。
- 后续可增加热键配置 UI。
- 后续可做公告轮播/房主管理/玩家列表完整无障碍化。

## 6. Scope

### In scope

- 客户端测试基建：`sts2-lan-connect.Tests` 或等价项目。
- 纯逻辑测试：焦点顺序规格、朗读文案生成、快捷键路由、邀请码入口判定。
- GdUnit4 headless smoke tests：Godot `Control` focus 行为、主要弹窗初始焦点、房间卡片可 focus/accept。
- `LanConnectFocusHelper`：focus mode、focus order、focus restoration、dialog initial focus、room-card accept。
- `LanConnectAccessibilityBridge`：say-the-spire2 反射探测、签名校验、熔断、一次性日志。
- `LanConnectAccessibilityAnnouncements`：中文朗读文案。
- `LanConnectAccessibilityHotkeys`：F7 邀请、F8 聊天；T 仅在严格不影响文本输入时可加入，否则延期。
- 邀请剪贴板入口：在 `OnLobbyPressed` 处提前识别有效邀请码并绕过服务器选择器。

### Explicitly out of scope

- 不做配置文件创建房间或替代原生窗口。
- 不做完整公告轮播无障碍化；保持 `LobbyAnnouncementCarousel` 非核心可跳过区域。
- 不做房主管理面板、玩家列表完整无障碍化；仅保证不会被新热键/弹窗破坏。
- 不做热键配置 UI。
- 不直接调用 say-the-spire2 的 `SpeechManager.Speak/Output`。
- 不做大规模视觉重设计或 `CreateDialogShell()` 重写。
- 不引入对 say-the-spire2 的编译期引用。

## 7. Target Test Stack

### Pure .NET tests

Use xUnit on `net9.0` for Godot-free logic.

Candidate project:

```text
sts2-lan-connect.Tests/
  sts2_lan_connect.Tests.csproj
  Accessibility/
    LanConnectAccessibilityAnnouncementsTests.cs
    LanConnectAccessibilityHotkeyRouterTests.cs
    LanConnectInviteEntryDecisionTests.cs
    LanConnectFocusOrderSpecTests.cs
    LanConnectAccessibilityBridgeContractTests.cs
```

Rules:

- Pure tests must not instantiate `Godot.Control`, `Button`, `PanelContainer`, etc.
- If a behavior touches Godot objects, create a pure adapter/spec class first and test that.
- Bridge contract test checks current say-the-spire2 source or fixture signature, not a compile-time package reference.

### GdUnit4 headless tests

Use GdUnit4 only for engine/runtime behavior.

Recommended versions from research:

- `gdUnit4.api` `5.1.0-rc1`
- `gdUnit4.test.adapter` `3.0.0`
- `gdUnit4.analyzers` `1.0.0`
- `Microsoft.NET.Test.Sdk` `18.0.0` or repo-compatible latest

Run settings:

```xml
<RunSettings>
  <RunConfiguration>
    <MaxCpuCount>1</MaxCpuCount>
    <EnvironmentVariables>
      <GODOT_BIN>/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot</GODOT_BIN>
    </EnvironmentVariables>
  </RunConfiguration>
  <GdUnit4>
    <Parameters>--headless --verbose</Parameters>
    <DisplayName>FullyQualifiedName</DisplayName>
    <CaptureStdOut>true</CaptureStdOut>
    <CompileProcessTimeout>30000</CompileProcessTimeout>
  </GdUnit4>
</RunSettings>
```

Bounded GdUnit4 test targets:

- Every test that instantiates `Control`, `Button`, `PanelContainer`, or any other `GodotObject` must be annotated with `[RequireGodotRuntime]`.
- A `PanelContainer` room card with `FocusMode.All` can receive focus and `ui_accept` callback.
- Dialog shell focus helper can set initial focus and restore previous focus.
- Focus order spec can be applied to a small fake tree without getting stuck.
- No more than one smoke test per major dialog family in this plan.

Test project access strategy:

- Prefer `[assembly: InternalsVisibleTo("sts2_lan_connect.Tests")]` and `[assembly: InternalsVisibleTo("sts2_lan_connect.GdUnitTests")]` for current `internal` helpers such as `LanConnectInviteCode`.
- If the implementation extracts pure logic into a separate internal helper, keep it under the same assembly and expose it to test assemblies rather than making runtime APIs public only for tests.
- If GdUnit4 adapter requires it during implementation, add `CopyLocalLockFileAssemblies=true` and document any `NU1605` suppression in the test csproj only.

### Build/test gates

Local verification commands:

```bash
export PATH="/Users/mac/.dotnet:$PATH"
export DOTNET_ROOT="/Users/mac/.dotnet"
DOTNET_BIN=/Users/mac/.dotnet/dotnet \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
bash scripts/build-sts2-lan-connect.sh

/Users/mac/.dotnet/dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj

/Users/mac/.dotnet/dotnet test sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1
```

Exact paths may be adjusted during implementation, but the acceptance gate must include build + pure tests + GdUnit4 smoke tests unless a documented GdUnit4 environment blocker appears.

## 8. Implementation Plan

### Phase 0: Preflight and source mapping

Purpose: reduce risk before touching the large overlay.

Tasks:

1. Use LSP/reference search for:
   - `CreateDialogShell`
   - `RebuildRoomStage`
   - `CreateRoomCard`
   - `ShowInviteConfirmDialog`
   - `LanConnectRoomChatOverlay.TogglePanel`
2. Confirm keybind conflicts:
   - Inspect game `InputMap`/available actions at runtime if possible.
   - If no reliable static list exists, implement F7/F8 only and log registration/handling decisions.
   - Do not enable `T` unless tests prove it never fires while a `LineEdit` or text-editing control has focus.
3. Confirm bridge target signature against current say-the-spire2:
   - Assembly/type: `SayTheSpire2.UI.UIManager` or discovered equivalent.
   - Method: `SetFocusedControl`.
   - Parameters: first assignable from `Godot.Control`, second optional/nullable UIElement.
4. Confirm how spoken text reaches say-the-spire2:
   - Do not assume `TryAnnounce(Control, string)` can pass arbitrary text directly.
   - Read say-the-spire2 `ProxyFactory`, `ProxyButton`, `ProxyTextInput`, and `ProxyElement` behavior and decide the text carrier.
   - Preferred carrier: visible child `Label` / existing button text / node name / tooltip that the generic proxy demonstrably reads.
   - If no generic text carrier is proven, stop Phase 4 and switch to an upstream PR or a separately tested reflected `UIElement` construction path; do not silently ship focus-only speech.
5. Confirm current invite entry behavior uses `LanConnectInvitePayload.S` server address and can skip server picker safely.

Acceptance:

- Preflight notes added to plan or implementation notes.
- No source behavior change in this phase except tests/project scaffolding if desired.
- Preflight runs before test scaffolding and before any UI edits.

### Phase 1: Test infrastructure first

Files likely touched:

- `sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj`
- `sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj`
- `sts2-lan-connect.GdUnitTests/gdunit4.runsettings`
- optional solution/build script updates if needed

Tasks:

1. Add pure test project on `net9.0`.
2. Add GdUnit4 test project on `Godot.NET.Sdk/4.5.1` or compatible `4.5` SDK.
3. Add test fixtures that compile before implementation:
   - invite code encode/decode tests around `LanConnectInviteCode` if accessible, or extracted pure wrapper.
   - hotkey router tests as pending target class (create class in implementation).
   - bridge contract fixture test with a local expected signature model.
4. Keep test projects out of packaged release output.

Acceptance:

- `dotnet test` pure tests exits 0.
- GdUnit4 smoke test can create a basic `Control` in headless runtime and exits 0.
- Main mod build still succeeds.

### Phase 2: Pure accessibility primitives

New candidate files:

- `sts2-lan-connect/Scripts/Lobby/Accessibility/LanConnectAccessibilityAnnouncements.cs`
- `sts2-lan-connect/Scripts/Lobby/Accessibility/LanConnectAccessibilityHotkeyRouter.cs`
- `sts2-lan-connect/Scripts/Lobby/Accessibility/LanConnectInviteEntryDecision.cs`
- `sts2-lan-connect/Scripts/Lobby/Accessibility/LanConnectFocusOrderSpec.cs`

Tasks:

1. Extract Chinese announcement text generation:
   - room card: room name, host, player count, lock state, joinability, game mode, selected state.
   - server row: display name/address, RTT, room count, creation guard.
   - dialogs: title + primary action + Escape behavior.
2. Extract hotkey routing:
   - F7: if valid pending invite or clipboard invite, open/accept path depending current state.
   - F8: toggle chat only when chat overlay/session is available.
   - T: only if implemented, ignored while text input has focus.
3. Extract invite entry decision:
   - valid clipboard invite with non-empty `S` and `R` => skip server picker and open overlay/invite confirm.
   - invalid/no invite => current behavior unchanged.
4. Define focus order specs as stable names rather than direct Godot nodes.

Acceptance:

- Tests cover every branch listed above.
- No `Godot.Control` instances in pure test classes.
- Announcement strings are concise Chinese and do not include secrets/password text.

### Phase 3: Layer A focus navigation

New candidate file:

- `sts2-lan-connect/Scripts/Lobby/Accessibility/LanConnectFocusHelper.cs`

Overlay changes should be surgical in:

- `LanConnectLobbyOverlay.cs:220`
- `LanConnectLobbyOverlay.cs:1463`
- `LanConnectLobbyOverlay.cs:1790`
- `LanConnectLobbyOverlay.cs:2207`
- `LanConnectLobbyOverlay.cs:2363`
- `LanConnectLobbyOverlay.cs:3695`

Tasks:

1. Add overlay-level keyboard handler without disrupting text entry:
   - Escape closes topmost visible dialog and restores focus.
   - `ui_accept` on a focused room card selects/joins as appropriate.
   - Do not handle shortcuts when a text input has focus unless explicitly allowed.
2. Make room cards focusable:
   - `FocusMode = FocusModeEnum.All`.
   - stable `Name`/metadata containing room id.
   - visible focus style at least as clear as selected style.
   - `FocusEntered` selects the room and updates side action state.
3. Define focus order:
   - server selector rows already buttons; add initial focus and predictable next/prev.
   - main overlay: search/filter/page controls -> room cards -> action/sidebar controls.
   - dialogs trap focus inside dialog where practical.
   - after every room-list rebuild, either assign explicit `FocusNext`/`FocusPrevious`/neighbor `NodePath`s or route `ui_up/down/left/right` through an overlay focus router; “default Godot order” is not sufficient for the room list.
4. Dialog focus behavior:
   - opening a dialog saves previous focus target.
   - each dialog has an explicit initial focus target.
   - close/Escape restores previous focus if still valid, otherwise deterministic fallback.
5. Room list rebuild behavior:
   - before rebuild, capture focused room id if focus is inside a room card.
   - after rebuild, restore focus to same room id if still visible.
   - if removed, focus next visible room; if none, focus refresh/create/search fallback and announce list changed.
6. Progress dialog:
   - if non-cancellable, do not let Escape close it.
   - if cancellable, Escape invokes the same cancel path as the cancel button.

Acceptance:

- Keyboard can enter lobby, select a room, and invoke join path without mouse.
- Keyboard can open create-room dialog, fill fields, submit with Enter, cancel with Escape.
- Keyboard can close every custom dialog with Escape unless the progress dialog is intentionally non-cancellable.
- Focus survives room refresh/pagination according to the rule above.
- GdUnit4 covers room-card focus/accept and dialog focus restore smoke cases.

### Phase 4: Layer B say-the-spire2 bridge

New candidate file:

- `sts2-lan-connect/Scripts/Lobby/Accessibility/LanConnectAccessibilityBridge.cs`

Entry point:

- `Entry.cs:18` to `Entry.cs:24` initialization area.

Tasks:

1. Implement bridge detection using project-local reflection style:
   - scan loaded assemblies for `SayTheSpire2` assembly/type.
   - probe exact `UIManager.SetFocusedControl` signature.
   - cache `MethodInfo` once.
2. Implement public surface:
   - `IsAvailable` / `Status` diagnostics.
   - `TryAnnounce(Control control, string announcement)` or equivalent, where `announcement` is first installed into the text carrier verified in Phase 0.
3. Do not call `SpeechManager.Speak` or `SpeechManager.Output`.
4. Use `Control.Name`, tooltip, or child label metadata for the null-UIElement fallback only if Phase 0 proves say-the-spire2 reads that carrier.
5. Wrap invocation in try/catch; first failure disables bridge for session and logs once.
6. Connect focus events through `LanConnectFocusHelper` so UI code does not know reflection details.

Acceptance:

- When say-the-spire2 absent, bridge logs unavailable once and no UI behavior changes.
- When signature missing/changed, bridge disables itself and does not throw.
- Contract test validates expected method shape.
- Contract/smoke test or documented source proof validates that the chosen text carrier is what say-the-spire2 will read for the relevant control class.
- Focus on a registered control calls bridge exactly once per focus-enter event in test harness/mocked bridge.

### Phase 5: Invite entry and hotkeys

Files likely touched:

- `Patches.MultiplayerSubmenu.cs:169`
- `LanConnectLobbyOverlay.cs:3555`
- `LanConnectRoomChatOverlay.cs:438`
- `LanConnectConstants.cs` if names/actions are centralized

Tasks:

1. Move/extract clipboard invite decision so `OnLobbyPressed` can check before showing server picker.
2. If valid invite:
   - ensure overlay exists.
   - show overlay directly.
   - surface the invite confirm dialog using existing `ShowInviteConfirmDialog` path.
   - use payload server `S` during accept; existing `AcceptInviteAsync` already handles temporary server switch.
3. If invalid/no invite:
   - behavior identical to today: show server selection first.
4. Add F7:
   - if invite confirm visible, trigger accept.
   - else if clipboard has valid invite and no blocking dialog/action, show invite confirm.
   - else log/status no-op; no noisy popup loop.
5. Add F8:
   - toggle chat only if chat overlay exists and session/chat is available.
   - no-op with concise status/log if unavailable.
6. T shortcut:
   - default defer unless conflict verification proves safe.
   - if implemented, never trigger while any `LineEdit`/text input has focus.

Acceptance:

- Clipboard valid invite at lobby entry skips server picker and opens invite dialog.
- Invalid/no clipboard invite preserves current server picker flow.
- F7 behavior is deterministic for invite-visible, invite-present, and invite-absent states.
- F8 toggles chat only when available and never injects text into `LineEdit`.
- Pure tests cover invite decision and hotkey routing.

### Phase 6: Diagnostics, docs, and packaging checks

Files likely touched:

- `docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md` or client docs if user-facing shortcut documentation is desired.
- `docs/CLIENT_RELEASE_README_ZH.md` if accessibility support should be announced in release docs.

Tasks:

1. Add concise diagnostics:
   - bridge detection status.
   - bridge disabled due to exception/signature mismatch.
   - focus restore fallback when room disappears.
   - invite skip path chosen.
2. Document shortcuts and accessible flow in Chinese user docs.
3. Confirm package scripts do not include test projects or transient test outputs.

Acceptance:

- Logs can explain: no say-the-spire2, signature mismatch, valid invite skip, focus fallback.
- User docs include F7/F8 and invite auto-popup behavior.
- Package output unchanged except intended mod DLL/docs.

## 9. Acceptance Criteria

### User-visible behavior

- From the multiplayer menu, a keyboard-only user can enter the lobby flow.
- From a controller/gamepad mapped to Godot `ui_up`, `ui_down`, `ui_left`, `ui_right`, `ui_accept`, and `ui_cancel`, the same major lobby flow works without mouse.
- The server selection screen has a deterministic initial focus and can be operated with keyboard.
- The main lobby room list is navigable with keyboard; focused room cards are visible and selectable.
- Room list navigation uses deterministic `ui_*` action handling or explicit focus neighbors, not incidental tree order.
- Pressing accept on a room card starts the same join flow as mouse double-click / join button.
- Create-room flow is keyboard-operable: fields, room type, max players, submit, cancel.
- Join-password and invite-confirm dialogs are keyboard-operable and Escape behavior is defined.
- F7 opens/accepts valid invite flow; F8 toggles room chat when available.
- Clipboard invite link present at lobby entry opens invite confirmation without first clicking a server.
- Chinese announcements are generated for room cards, major buttons, dialogs, invite state, and chat toggle.

### Safety

- say-the-spire2 absent: no crash, no behavior regression, bridge unavailable logged once.
- say-the-spire2 present but incompatible: no crash, bridge disabled with warning.
- No direct dependency on SayTheSpire2 DLL at compile time.
- No direct calls to `SpeechManager.Speak`/`Output`.
- No broad rewrite of `LanConnectLobbyOverlay.cs`; new logic lives primarily in helper classes.

### Test gates

- Main mod build exits 0.
- Pure .NET tests exit 0.
- GdUnit4 headless smoke tests exit 0 or, if blocked by environment, a documented fallback issue is filed and pure tests still exit 0.
- Bridge contract test verifies expected say-the-spire2 method shape.
- Test gate verifies or documents source proof that the chosen text carrier is consumed by say-the-spire2 generic proxy/read path.
- Hotkey routing tests include text-input focus guard.
- Invite decision tests include valid invite, invalid clipboard, missing server, missing room, and expired/nonexistent room handling path where applicable.

## 10. Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Room list refresh frees the focused card | Capture focused room id before rebuild; restore by room id or fallback deterministically after rebuild |
| say-the-spire2 API changes | Exact signature probe, contract test, bridge circuit breaker, one-time warning |
| Overlay file becomes unmaintainable | Helper extraction; no `CreateDialogShell()` rewrite; localized edits only |
| F7/F8/T conflict with game input | F7/F8 only by default; T deferred unless safe; ignore shortcuts while text input focused |
| GdUnit4 setup brittle | Keep pure logic tests primary; bound GdUnit4 to small smoke tests; require `GODOT_BIN` runsettings |
| Clipboard privacy concern | Only parse the existing `STS2INV:` prefix; no logging of full clipboard or password |
| Announcement text leaks room password | Announcement builder must never speak `payload.P` or password fields |
| Dialog stacking from hotkeys | Single topmost-dialog policy; hotkeys ignored or routed only when no blocking dialog is open |

## 11. Verification Plan

1. Static checks:
   - `lsp_diagnostics` on changed C# files.
   - Build main mod.
2. Pure tests:
   - announcement text.
   - hotkey router.
   - invite entry decision.
   - focus order specs.
   - bridge contract.
3. GdUnit4 tests:
   - headless Control creation.
   - focusable room card smoke test.
   - dialog focus restore smoke test.
4. Manual/agent QA surface:
   - Launch game/mod build where available.
   - Use keyboard only through server selection, room list, create dialog, invite confirm, chat toggle.
   - Repeat the same major flow through controller-mapped Godot `ui_up/down/left/right/accept/cancel` actions where a controller or simulator is available.
   - Review `godot.log` for bridge/focus/invite diagnostics.
5. Field validation later:
   - Ask issue reporter or say-the-spire2 user to test with real screen reader.
   - If they report missed announcements, map logs to bridge/focus events before changing UX.

## 12. Execution Order and Stop Conditions

1. Do Phase 0 first. Stop if the bridge text carrier cannot be proven, F7/F8 are unsafe, or invite payload server switching cannot be validated.
2. Do Phase 1. Stop if test projects cannot compile; resolve test infrastructure before UI work.
3. Do Phase 2. Stop if pure tests cannot represent focus/hotkey/invite decisions cleanly; redesign helper boundaries.
4. Do Phase 3. Stop if room-card focus cannot work on `PanelContainer`; switch to a focusable `Button`/custom Control wrapper while preserving visuals.
5. Do Phase 4. Stop if `SetFocusedControl` external invocation does not cause say-the-spire2 to consume the expected text carrier; document and switch to upstream PR strategy rather than direct TTS.
6. Do Phase 5. Stop if invite payload server switching fails; do not skip server picker until server switch is proven.
7. Do Phase 6 and final verification.

## 13. Implementation Notes for Future Executor

- Use `lsp_find_references` before editing `CreateDialogShell`, `RebuildRoomStage`, and `CreateRoomCard`.
- Prefer new helper methods/classes over expanding inline logic in `LanConnectLobbyOverlay.cs`.
- Preserve Chinese UI strings.
- Do not edit `releases/` directly.
- Do not commit test artifacts, `.godot/`, `build/`, or `release/` outputs.
- If GdUnit4 packages are unstable, pin versions in test project and document the reason.
- If actual implementation adds public config fields, route all persistence through `LanConnectConfig` only.
