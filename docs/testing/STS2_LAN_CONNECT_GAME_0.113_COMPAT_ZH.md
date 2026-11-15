# STS2 LAN Connect 游戏 0.113.0 兼容验证记录

> 后续状态（2026-11-15）：用户已确认独立界面中文正常，并在第三轮 `0.6.3+compat.3` 诊断包后反馈“没问题了”，记录为手机卡牌预览复测通过。改动收录至客户端 `0.6.4`。以下保留 2026-11-14 的过程、构建哈希与当时验证边界；其中“待实机复查”等描述指当时状态。用户本次未附新日志，不将功能确认扩展为单一根因已获日志证明。当前说明见 [v0.6.4 发布说明](../RELEASE_NOTES_V0.6.4_ZH.md)。

日期：2026-11-14。客户端保持 `0.6.3`，本记录对应尚未发布的兼容修复构建。

## 修复范围

用户提供的 `godot.log` 第 710 行报告 `InitialGameInfoMessage.gameMode` 缺失，加入房间时触发 `InternalError`；第 318 行报告难度缩放模型类型无法加载。

本轮按运行时接口适配以下变化，使同一份 MOD DLL 支持新旧游戏：

- 加入消息的模式位置、独立自定义标志、难度上限字段，以及 `JoinResult.sessionState` 的可空布局。
- 难度缩放模型命名空间及运行状态访问方式。
- 主机断开客户端方法的 3/4 参数签名，以及连接玩家集合返回类型。
- 放弃存档时进度更新方法的返回类型。
- 宝箱奖励的 `relicIndex` 和遗物争抢方法签名。

保留旧版自定义模式及无 `GameMode` 存档的 `Modifiers` / `DailyTime` 回退路径。支持多版游戏不代表允许不同游戏版本玩家互联，原有版本匹配检查继续生效。

用户随后反馈中文字体显示异常。本轮补充 `LanConnectLobbyFonts`，通过根控件 `Theme.DefaultFont` 继承字体：用 `FontVariation` 保留原拉丁字体，并以静态缓存、`CacheMode.Reuse` 复用游戏官方简体中文字体作为回退；旧版资源布局缺失时使用系统字体回退。不随 MOD 打包游戏字体。0.111 同样已有官方中文字体资源，问题在于普通 Godot 控件未走游戏专用控件的语言替换逻辑，并非 0.113 新增字体管理器导致。

最终字体适配覆盖大厅与服务器选择的独立控件树、房间聊天和管理的 CanvasLayer 根、选择弹窗、LAN 模式选择、MOD 同步、基础聊天、确认窗口和表情选择。原生悬浮提示与富文本消息显式应用字体回退，避免独立界面或显式字体覆盖绕过根主题。

## 自动化验证

| 检查 | 结果 | 证据 |
|---|---|---|
| 最终主测试集（.NET 9.0.7） | 1330 通过、1 项既有跳过、0 失败 | `/tmp/sts2-tests-all-font-roots.log` |
| 协议补丁测试集（.NET 9.0.7） | 11 通过、0 失败 | `/tmp/sts2-protocol-tests-net9.log` |
| 最终 0.113.0 编译与源码打包 | 0 警告、0 错误 | `/tmp/sts2-package-all-font-roots.log` |
| 针对旧版 0.111.0 编译（字体修复前的兼容构建） | 0 警告、0 错误 | `/tmp/sts2-old-build.log` |
| 最终交付 DLL 加载及直接成员引用检查：0.113.0 | 1245 类型、284 处引用通过，0 失败或未验证 | `/tmp/sts2-abi-new-all-font-package.log` |
| 同一最终交付 DLL 加载及直接成员引用检查：0.111.0 | 1245 类型、284 处引用通过，0 失败或未验证 | `/tmp/sts2-abi-old-all-font-package.log` |
| Godot 4.5.1 中文字形与继承探针 | 中文回退及组合字体覆盖“中、文、房、间”；Label、Button、LineEdit、RichTextLabel，以及 PopupPanel、AcceptDialog、ConfirmationDialog 子标签和内置标签均通过 | `/tmp/sts2-font-probe-windows.log` |

新版游戏 commit 为 `16ac0fe3`；旧版真实程序集 commit 为 `41cef1ea`。新增 `scripts/verify-game-abi-load.sh --members` 检查直接游戏字段和方法引用，补足仅执行 `Assembly.GetTypes()` 无法发现的方法体成员漂移。

该检查不执行游戏代码，不覆盖反射字符串、泛型方法约束或完整 JIT/运行行为。0.107 的兼容形状由回归测试覆盖，本机没有 0.107 真实程序集，未完成该版实机验证。游戏程序集和反编译源码未纳入本次交付。

## 本机安装与启动边界

修复包已安装至本机 Steam 游戏，并完成 macOS 签名刷新；安装使用 `--no-save-sync`，未执行存档迁移。验证过程串行构建、测试，同一时间仅运行一个游戏实例。

首次启动时，游戏因尚未确认 MOD 提示而跳过加载。用户随后手动确认启用 MOD，再次使用 `--force-steam=off` 启动。字体修复前的同轮兼容构建日志 `/tmp/sts2-runtime-accepted-final.log` 确认：

- Steam 初始化已跳过，命令行包含 `--force-steam=off`。
- 已调用 MOD 初始化入口，11 个初始化阶段全部成功。
- 游戏补丁组 `applied=10, failed=0`，大厅运行时进入 ready 状态。
- 游戏在 12,537 ms 到达主菜单。
- 用户完成建房，主机控制通道已连接，退出房间的 DELETE 返回 204。

该构建的启动加载、建房和退出检查通过。日志没有加入其他房间或开始一局的证据，不能据用户“成功进入”的反馈推断加入流程已验收。当前环境的组织策略禁止自动操作游戏界面；完整双客户端联机、续局、重连及旧版实机仍未验收。

最终字体修复包已安装并刷新 macOS 签名，实际使用 `--force-steam=off` 启动。`/tmp/sts2-runtime-all-font-final.log` 确认 Steam 初始化已跳过、补丁组 `applied=10, failed=0`、大厅运行时 ready，并在 12,745 ms 到达主菜单；日志记录 `Chinese fallback ready, glyphsAvailable=True`。

字体修复已通过字形、控件及窗口继承探针和实际游戏字体资源加载检查。用户截图确认大厅主页面中文恢复，随后明确确认最终包的服务器选择页和房间聊天窗“两处中文均正常”。测试结束后已确认游戏进程退出，回收内存。

## 交付产物

- 安装包：`/Users/macmini/Documents/ChatGPT/联机大厅/交付/0.113.0兼容修复/sts2_lan_connect-release.zip`
- ZIP SHA-256：`6255fbeb7292a8fe1108e8acce10dd2f01fab34c29e9b6d45c9d92d4b98f1c72`
- DLL SHA-256：`8f1c8ebd48d09cc57c34121a674522b5453edf8b854c43676e763ab9c67496b0`

以上哈希对应包含中文字体修复的最终交付包。
