# STS2 LAN Connect v0.5.2 Phase 0 基线证据

日期：2026-07-21

## 仓库与发布基线

- 功能分支：`feat/rich-chat-reference-ux-0.5.2`
- 基线：`origin/main@3927eb1ef3f1e0ebc1d23989b26cd6fa732f1848`
- 正式版本：客户端 `0.5.1`
- GitHub Release：`v0.5.1`，正式发布（非 draft、非 prerelease）
- v0.5.1 客户端资产 SHA-256：`642c1e8a0d562b3201d75101972a88e5306852ca987eb2bd4c745ea0f2a124c6`

开始前工作树只有用户未跟踪资料。`.omc/`、`.playwright-mcp/`、`.superpowers/`、
`docs/superpowers/`、v0.5.2 计划与 handoff prompt 均未修改、未加入索引。

## main 基线测试

- xUnit：672 通过，1 个既有 monster 双客户端证明测试跳过，0 失败。
- GdUnit：226 通过，0 跳过，0 失败。

## 五类可重复失败

Phase 0 新增测试不修改生产代码。focused 结果：

- xUnit：1 通过，1 失败。
  - 通过：v0.5.1 混合 `text/emoji/item_ref/power_state/target_ref` JSON 字节级 round-trip。
  - 失败：聊天生产代码没有 `SmartDescription`、`DynamicVars` 与完整 Power 上下文注入。
- GdUnit：2 个截图用例通过，4 个行为用例失败。
  - 实体末尾没有真实 `TextEdit`，焦点仍落在实体按钮。
  - 没有 Android 引用按钮，`InputEventScreenTouch` 不进入捕获链。
  - 触屏点击消息引用不能打开固定预览。
  - 混合消息没有单一 `MegaRichTextLabel`，仍由 `HFlowContainer` 分段渲染。

以上正好构成计划要求的五类失败：IME、Android 引用入口、触屏预览、动态 Power 说明、
混合消息行内换行。协议 fixture 同时证明 v0.5.2 不需要修改 v0.5.1 线格式。

## 截图基线

自动化截图保存在系统临时目录，不复制进仓库：

| 场景 | 路径 | SHA-256 |
|---|---|---|
| 桌面 1920x1080 | `$TMPDIR/sts2-v052-phase0-baseline/desktop-1920x1080.png` | `7dd3db55f6455f55854c34682ac25d1fe02c984b701de344b6710690f8c464c5` |
| 触屏纵向仿真 1080x1920 | `$TMPDIR/sts2-v052-phase0-baseline/touch-portrait-simulated-1080x1920.png` | `4838f184fea86546588add25bb40e0bd4aecb8fff49419a70189a51782510fa5` |

两张图片均由 Godot `SubViewport` 真实渲染并通过非空 PNG、像素尺寸检查。当前没有连接的
Android 设备（`adb devices` 为空），第二张只证明纵向触屏布局现状，不能替代后续 Phase 2、
Phase 3、Phase 5 和 Phase 7 的 Android 实机验收。

## 固定参考审计

参考固定为 `Shiroim/sts2_typing@3cc057fa613285a4a54a614ca2aacb90827a3d32`。
审计确认其使用单一 `LineEdit`、`MegaRichTextLabel` meta、原生
`card_hover_tip.tscn` / `hover_tip.tscn`，并为 Power smart description 注入 Amount、
OnPlayer、IsMultiplayer、PlayerCount、OwnerName、ApplierName、TargetName、能量前缀和
DynamicVars。它的消息预览仍只接入 meta hover，因此不能作为 Android 点击验收证据。

Phase 0 未复制参考项目源码，不需要新增 MIT 第三方署名。
