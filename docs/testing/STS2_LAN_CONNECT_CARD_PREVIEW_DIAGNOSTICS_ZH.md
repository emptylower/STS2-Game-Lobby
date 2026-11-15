# STS2 LAN Connect 手机卡牌预览第三轮诊断记录

> 后续状态（2026-11-15）：用户已确认独立界面中文正常，并在第三轮 `0.6.3+compat.3` 诊断包后反馈“没问题了”，记录为手机卡牌预览复测通过。改动收录至客户端 `0.6.4`。以下保留 2026-11-14 的过程、构建哈希与当时验证边界；其中“待实机复查”等描述指当时状态。用户本次未附新日志，不将功能确认扩展为单一根因已获日志证明。当前说明见 [v0.6.4 发布说明](../RELEASE_NOTES_V0.6.4_ZH.md)。

日期：2026-11-14。本轮构建标识为 `0.6.3+compat.3`，用于定位手机端点按聊天卡牌引用后无法预览的问题，并补上预览创建失败时的可见反馈。Android 实机根因尚未确定。

## 已确认的现象与证据

- 手机能看到金色的具体卡名，例如“防御”，但点按后没有预览。按当前渲染路径推断，这比“只有普通聊天文字”更接近已经生成可交互引用的情况；仍需本轮日志确认实际激活和显示阶段。
- 用户提供的电脑端、手机端 `godot.log` 均为 `0.6.3+compat.2`，DLL SHA-256 均为 `f207975419389d1a60897414e1e156c1f79c19c263d9b1b1e3f09d842b9e671d`，未发现两端安装了不同构建的证据。
- 这两份日志中的真实 `tail_v1` 续局加入正常，不能把本次卡牌预览问题归为已经失败的房间加入流程。
- 手机日志中有 RitsuLib 0.5.14 因旧 `SingletonModel` 引用产生的装载错误；目前没有证据证明它导致了内置卡牌引用无法预览。
- 原日志缺少引用激活和预览显示链路的诊断，无法据此区分输入未到达、预览创建失败或显示后立即关闭。

第二轮文档中把触摸、模拟鼠标和悬停冲突作为修复方向，不能据此认定 Android 根因已经查明。原生 Godot 探针已观察到 RichText `MetaClicked` 和 Button `Pressed` 都能响应解析后的触摸事件；因此，本轮保留现有手势状态机，也没有另加按钮入口。

## 本轮变更

新增前缀为 `sts2_lan_connect reference_diag: ` 的结构化日志，贯穿消息渲染、模型解析、输入、引用激活、视觉创建、显示和关闭。每进程最多输出 240 条，同一 `stage + reason` 最多 12 条，避免悬停重复刷屏。

日志仅保留白名单元数据，包括模型 ID、升级等级、状态、控件与引用标识、输入来源、可见性和矩形。异常只记录类型及最多 4 帧方法名，不记录异常消息、聊天正文、玩家名、服务器、密码或令牌。日志输出失败会被隔离，不中断聊天界面；纯单元测试可注入独立日志接收器。

卡牌视觉创建失败或原生卡牌场景在 Ready 阶段配置失败时，改为显示“预览暂时无法显示，请关闭后重试。”等本地化友好提示。点按打开的提示沿用固定预览的关闭按钮及关闭行为。提示不会伪造卡牌内容，也不会显示技术堆栈。未知物品和普通文本仍沿用不可交互的原有降级方式。

## 手机端复查步骤

1. 只需将手机端 MOD 更换为本轮 `compat.3` 包，并完全退出、重新启动游戏，确保新代码已加载。
2. 让电脑端重新分享一张卡牌，在手机端找到这条新消息的金色具体卡名，点按一次。
3. 返回这次手机端的 `godot.log`，并说明是否出现卡牌预览、友好错误提示，或仍然完全没有显示。

电脑端可保留 `compat.2`，本轮没有修改卡牌分享的线上报文格式。复查以重启后的新消息和一次点按为准，便于将日志串成同一条链路。

## 工程日志阅读

`sequence` 是进程内诊断序号。`view_id` 关联同一消息控件的输入与激活，`reference_id` 关联已解析引用与后续预览；它们不是跨设备或跨重启的标识。

| 阶段 | 主要 reason | 关键字段与判断 |
|---|---|---|
| `render` | `structured_content`、`text_only`、`item_result`、`item_degraded_to_text`、`view_ready` | `segments`、`item_refs`、`references`、`status`、`preview_type`。先确认消息含物品引用，且生成了可交互引用；降级为文本时不会进入点按预览。 |
| `resolve` | `complete`、`cache_hit`、`model_missing`、`exception` | `model_id`、`item_type`、`upgrade`、`status`。区分本地模型不存在、解析异常和复用负缓存。 |
| `input` | `touch_press`、`touch_release`、`mouse_press`、`mouse_release`、`meta_clicked`、`meta_waits_for_touch_release`、`meta_suppressed`、`meta_without_reference` | 按 `view_id` 查看事件是否到达；`device`、`index`、`position`、`cancelled` 记录原生输入形态。按压与松开后没有激活时，继续检查 meta 路径及抑制原因。 |
| `activate` | `view_reference_pressed`、`reference_pressed`、`controller_unavailable` | `source`、`view_id`、`reference_id`、`model_id`。前两项分别表示消息控件已发出激活、聊天面板已接收激活；控制器不可用会单独记录。 |
| `visual` | `create_begin`、`native_card_fallback`、`native_card_ready_exception`、`native_hover_tip_fallback` | 区分开始创建、原生场景失败后尝试备用视觉，以及进入树后配置失败；异常附受限 `exception`、`stack`。`native_card_fallback` 本身不代表最终失败，需继续看后续 `preview`。 |
| `preview` | `request`、`gate_rejected`、`shown`、`visual_exception`、`failure_surface_shown`、`failure_surface_exception` | `status`、`preview_type`、`in_tree`、`visible`、`visible_in_tree`、`bounds`、`actual_bounds`、`viewport`、`pinned`。区分前置条件拒绝、视觉异常、友好提示已显示，以及节点存在但父节点不可见或矩形异常。 |
| `preview` | `close`、`exit_tree`、`pointer_exit_ignored_while_pinned`、`hover_ignored_while_pinned` | `close.source` 记录 `outside_press`、`close_shortcut`、`PointerExited`、`MessageRemoved`、`TabSwitched`、`ContextCleared` 等来源。用于判断是否显示后立即关闭，以及固定预览是否保留。 |

正常点按的核心链路应能看到 `view_reference_pressed → reference_pressed → request → create_begin → shown`。若视觉创建失败，则预期转为 `visual_exception → failure_surface_shown`；Ready 阶段失败还会先出现 `native_card_ready_exception`。

确认本轮渲染诊断正常、消息确实含引用后，如果一次复现没有对应输入记录，才继续调查输入路由。诊断有总量和重复次数限制，不能用已经耗尽预算的会话推断“输入未到达”。

## 已执行验证及边界

| 检查 | 当前结果 | 证据 |
|---|---|---|
| 主单元测试集 | 1352 通过、1 项既有跳过、0 失败 | `/tmp/sts2-reference-diagnostics-unit.log` |
| 真实 Godot C# 生产控件输入探针 | `PRODUCTION_PROBE_OK`；触摸只激活一次并固定预览，悬停后保持，外部点按关闭，工厂抛异常时失败提示可见；运行日志无 Godot ERROR／WARNING | `/tmp/sts2-production-probe-final.log` |
| 原生场景 Ready 失败路径探针 | 第二次配置失败后释放旧节点，显示友好失败提示，再通过真实触摸事件点按“×”关闭 | `/tmp/sts2-production-probe-final.log` |
| 临时探针项目构建 | 成功；有 1 条 `GodotPlugins.Main` 生成器与 MOD 类型冲突警告 | `/tmp/sts2-production-probe-build.log` |
| 最终安装包构建 | 0 警告、0 错误 | `/tmp/sts2-reference-diagnostics-package.log` |
| 最终交付 DLL 加载及直接成员引用检查：0.113 | 1250 类型、284 处引用全部通过 | `/tmp/sts2-reference-diagnostics-abi-new.log` |
| 同一 DLL 加载及直接成员引用检查：0.111 | 1250 类型、284 处引用全部通过 | `/tmp/sts2-reference-diagnostics-abi-old.log` |
| 最终交付 DLL 对真实 0.113 加入响应编解码 | 180 项全部通过 | `/tmp/sts2-reference-diagnostics-wire-new.log` |
| 同一 DLL 对真实 0.111 加入响应编解码 | 36 项全部通过 | `/tmp/sts2-reference-diagnostics-wire-old.log` |
| 源码空白检查 | `git diff --check` 通过 | 实现阶段执行 |

生产控件探针使用真实的消息控件与预览控制器，通过 `Input.ParseInputEvent` 送入事件，未用 `EmitSignal` 代替触摸。视觉工厂与原生视觉端口使用替身，Ready 失败路径经过实际 Godot 节点生命周期。临时项目没有游戏 PCK，因此仅在 `/tmp` 探针环境注入本机 `FontFile` 和 `InputMap`；这些设置没有改动生产 MOD 或安装包。

上述结果不等于 Android 实机测试，也没有验证真实原生 `CardModel` 的完整卡牌视觉创建。新增 GdUnit 场景尚未执行。临时探针构建的 1 条警告与最终生产安装包构建的 0 警告分别记录，不混为同一项检查。原生加入响应检查沿用第二轮工具，其覆盖边界见第二轮验证记录，不代表完整双客户端联机验收。

本轮没有再次启动完整游戏或进行双端联网复现。手机端是否恢复预览，需以上述一次实机复查结果为准。

## 交付记录

- 目标安装包：`/Users/macmini/Documents/ChatGPT/联机大厅/交付/0.113.0卡牌预览诊断-第三轮/sts2_lan_connect-release.zip`
- ZIP SHA-256：`1fdc27e0941b9c79f76a7aedcf060332aecbd6bad4f295703d86b64ec79d194c`。
- DLL SHA-256：`ab1ed2e58f36f1630673d1e62f53f87dc4fd06c68ba53d7c51db32c634f0bd2a`。
- ZIP 内、交付暂存目录及标准产物目录的 DLL 完全一致。
- 最终 Godot 控件探针日志：`/tmp/sts2-production-probe-final.log`，结果为 `PRODUCTION_PROBE_OK`；Android 实机验收待手机端复查。

此记录保留第二轮修复结果及其交付包，本轮仅补充手机卡牌预览的可观测性和创建失败反馈，不宣称已经修复 Android 实机根因。
