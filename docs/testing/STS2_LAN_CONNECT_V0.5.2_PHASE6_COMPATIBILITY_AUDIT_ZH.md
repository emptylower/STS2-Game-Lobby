# STS2 LAN Connect v0.5.2 Phase 6 兼容与可访问性证据

日期：2026-07-22

## TDD 与回归结果

Phase 6 先用失败测试锁定以下问题，再完成最小实现：

- Android 引用按钮的最小触摸目标小于 `44x44`。
- 固定预览关闭按钮的最小触摸目标小于 `44x44`，且缺少可访问名称与提示。
- 固定预览容器没有包含标题和说明的可访问名称。
- 本地化表缺少预览关闭动作的中英文标签。

focused 红灯为 GdUnit `2/2` 失败、xUnit 本地化 `2` 失败，以及 GdUnit 本地化关闭动作
`1` 失败。最小实现后的 focused 绿灯为 GdUnit `3/3`、xUnit `34/34`。

完整回归结果：

- xUnit：696 通过，1 个既有 monster target 双客户端证明测试跳过，0 失败。
- GdUnit：258 通过，0 跳过，0 失败。
- 客户端 build/package：0 warning，0 error。
- 候选 ZIP SHA-256：`becb09ca2f299cb3c86a5133c1d93733a10c64589968f05766f3e7e31d862a7f`。

该 ZIP 仍携带 Phase 7 前的程序集版本 `0.5.1.0`，只作为 Phase 6 行为与加载候选，不能作为
最终 v0.5.2 发布包。

## 游戏版本兼容证据

### v0.107.1

- 官方 Steam fixture：build `23811903`，release commit `59260271`。
- 使用该 fixture 的实际程序集重新编译：0 warning，0 error。
- 候选 MOD 安装到 fixture 后 codesign 验证通过。
- 2026-07-22 实际启动进入 v0.107.1 主菜单，界面显示“已加载 1 个模组”。
- `godot.log` 确认加载本地 `sts2_lan_connect.dll` / PCK、调用 `Entry`、所有补丁组
  `failed=0`、`initialized with ready hooks`，并记录 `Release Version: v0.107.1`。

### v0.109.0

- 当前 Steam `public-beta` 安装，release commit `c12f634d`。
- 候选 MOD 安装后 codesign 验证通过。
- 2026-07-22 从 Steam 客户端实际启动进入 v0.109.0 主菜单，界面显示“已加载 1 个模组”。
- `godot.log` 确认调用 `Entry`、所有补丁组 `failed=0`、`initialized with ready hooks`，并记录
  `Release Version: v0.109.0`。

### v0.108.0 范围调整

已识别官方历史 build `24032229`、macOS depot manifest `1977841934321910790`，但当前账号与
anonymous SteamCMD 均无法取得该历史 depot，不能形成可审计的真实加载证据。

2026-07-22 用户明确确认“不再要求对 108 做适配”。因此 v0.5.2 的发布兼容 gate 调整为
v0.107.1 与 v0.109.0；本记录不声称 v0.108.0 已构建或已加载验证。

## API 漂移审计

对 v0.107.1 与 v0.109.0 的实际游戏程序集反编译并核对：

- `PowerModel`、`EnergyIconHelper`、`MegaLabel`、`NPower`、`HoverTip` 的相关实现一致。
- 两版均存在本功能使用的 `NCard.UpdateVisuals(PileType, CardPreviewMode)`、
  `AssetCache.GetScene/GetMaterial`、`NHotkeyManager.AddBlockingScreen/RemoveBlockingScreen`。
- 两版 PCK 均包含 `card_hover_tip.tscn`、`hover_tip.tscn` 与 Power hover 材质路径。

未启用 monster target reference，未改变 v0.5.1 聊天协议、feature intersection、限额、
generation、legacy fallback、MOD 同步或加入流程。
