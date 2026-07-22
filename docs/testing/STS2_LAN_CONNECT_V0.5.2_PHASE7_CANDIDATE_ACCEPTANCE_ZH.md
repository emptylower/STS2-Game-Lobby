# STS2 LAN Connect v0.5.2 Phase 7 候选包验收证据

日期：2026-07-22

## 最终候选与自动化门禁

- 候选源码 commit：`d19b943`（`release: prepare v0.5.2 rich chat candidate`）。
- lobby-service：TypeScript check 通过，Node 测试 `433/433` 通过。
- 客户端 xUnit：`697` 通过，`1` 个既有 monster target 双客户端证明测试跳过，`0` 失败。
- GdUnit：`258/258` 通过，`0` 跳过，`0` 失败。
- 客户端 build：`0 warning`、`0 error`。
- `scripts/verify-release.sh`：完整通过，包括 package allowlist 与法律文件检查。

两次从固定 commit 独立打包后，目录 `diff -qr` 无差异，ZIP `cmp` 完全一致：

- 客户端 ZIP SHA-256：`cf635e75194ba5fe48038061a861bf7cf20bad328847c84ee4f96251b759cdd6`
- `sts2_lan_connect.dll` SHA-256：`efd4cead2da11088733e14f5635a878a6d495dd11cf7430a36194b3afd07a698`

最终候选已按同一 DLL 哈希安装到 Steam v0.109.0、v0.107.1 fixture 和 MuMu Android
启动器。Mac app bundle 的 `codesign --verify --deep --strict` 通过。

## 游戏版本加载证据

### v0.107.1

- 真实冷启动进入主菜单，界面为 `v0.107.1`。
- `godot.log`：`Version=0.5.2.0`、patch groups `failed=0`、
  `initialized with ready hooks`、`Release Version: v0.107.1`。

### v0.109.0

- 从 Steam 客户端真实冷启动进入主菜单，界面为 `v0.109.0`。
- `godot.log`：`Version=0.5.2.0`、patch groups `failed=0`、
  `initialized with ready hooks`、`Release Version: v0.109.0`。

用户已明确移除 v0.108.0 适配要求；发布 gate 只包含 v0.107.1 与 v0.109.0。

## Android + Mac v0.5.2 实机矩阵

MuMu Android v0.109.0 以候选 0.5.2 创建密码房间 `P7FinalRef`，Mac Steam v0.109.0
搜索并加入成功。双方进入同一首场战斗后完成：

- Android 点击聊天输入区的引用按钮进入一次性引用模式。
- 触摸捕获卡牌后输入区出现结构化卡牌实体，敌人仍为 `123/123`、能量仍为 `3/3`，
  证明成功捕获点击没有同时打牌。
- 引用后焦点回到真实文本输入槽；MuMu 使用 Mac 实体键盘输入拼音 `yinyong` 并以空格
  组合为“引用”，随后发送“打击引用”。
- Android 点击消息中的“打击”引用，原生卡牌说明固定打开；点击外部关闭，未打开暂停菜单。
- Android 发送玩家引用加“测试”，Mac 收到统一行内内容并可点击玩家固定预览。
- Mac 发送 `Mac final 0.5.2`，Android 正常收到，双向房间消息链路通过。

同一功能提交在此前实机轮次还验证了 Android 的遗物、药水、Power 与玩家捕获，以及
“触媒 4”、力量、中毒等动态 Power 最终说明；桌面 `Alt+R` 捕获药水与固定预览通过。

MuMu 不唤起 Android 软键盘，本轮按用户说明使用 Mac 实体键盘控制，因此不声称取得了
软键盘弹出/闪烁证据；真实 TextEdit 的中文组合输入和引用后继续输入已验证。

## v0.5.2 与正式 v0.5.1 客户端互通

Mac 临时安装 GitHub `v0.5.1` Release 的正式客户端资产：

- Release ZIP SHA-256：`642c1e8a0d562b3201d75101972a88e5306852ca987eb2bd4c745ea0f2a124c6`
- 安装 DLL SHA-256：`56acee350143d77043c3056e1b470e1258614fdc246d00798fa7c9f0ae13c154`

Android 0.5.2 创建密码房间 `P7FinalReCompat052051` 后：

- v0.5.1 客户端能发现房间并显示 `MOD 0.5.2`。
- v0.5.1 输入密码并加入成功；日志记录房间 `modVersion=0.5.2`、`protocolAligned=True`，
  并取得 `State=InLobby`。
- v0.5.1 发出的 `v0.5.1 compat ok` 在 Android 0.5.2 显示。
- Android 0.5.2 发出的 `v0.5.2 compat ok` 在 Mac v0.5.1 显示。

测试后 Mac 已恢复精确最终候选 DLL，哈希重新核对为
`efd4cead2da11088733e14f5635a878a6d495dd11cf7430a36194b3afd07a698`。

## 双桌面矩阵

使用两个独立 v0.107.1 app 实例，二者均安装上述最终候选 DLL：

- 实例一创建密码房间 `DeskDual052`，实例二在测试节点发现并加入。
- 房间列表显示 `GAME v0.107.1`、`MOD 0.5.2`。
- 实例二发送 `desktop2 hello`，实例一收到。
- 实例一发送 `desktop1 reply`，实例二收到。

测试完成后两个实例均已退出，临时房间关闭。

## 环境恢复与剩余人工确认

- Steam v0.109.0 已恢复最终 0.5.2 候选 DLL。
- 测试前暂存的 `SayTheSpire2` 可访问性 MOD 已恢复，并重新完成 app bundle codesign。
- MuMu 已退出游戏并停在 STS2 启动器，安装的候选 MOD 保留。
- 用户已确认实机验收，并授权从 `feat/rich-chat-reference-ux-0.5.2` 功能分支发布
  `v0.5.2-rc.1` GitHub Pre-release；仍不合并 main、不创建无尾缀 `v0.5.2` 正式标签，
  也不更新 Steam Workshop。

电脑控制接口无法在鼠标点击期间保持 Option/Alt 修饰键，所以原 `Alt+左键` 路径保留自动化
回归证据，但本轮没有新增真实修饰键点击证据。用户已接受该剩余风险，以及“MuMu 使用实体
键盘、无软键盘证据”的验收边界，并选择先发布 GitHub 预览版。
