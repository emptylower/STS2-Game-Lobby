# STS2 LAN Connect 客户端安装说明

这是 `STS2 LAN Connect` 的客户端发布包。

## 当前版本

- 当前客户端版本：`0.2.2`
- 大厅顶部新增公告轮播栏，支持更新、活动、警告、信息四类公告，并使用左亮右暗的暖色渐变底
- 大厅整体改为暗金游戏风格，房间主区与侧栏使用半透明卡片层次
- 房间内新增右上角聊天面板，可直接和同房间玩家收发消息，并显示未读角标
- 聊天面板支持长按拖动，位置会写入本地配置，下次进房自动恢复
- 大厅支持搜索、分页和可叠加筛选
- 筛选支持 `公开`、`上锁`、`可加入`
- 标题栏提供 `切换服务器`，可从中心服务器拉取可用大厅并直接切服
- 建房弹窗支持 `标准模式`、`多人每日挑战`、`自定义模式`
- 开发网络设置支持单独覆盖中心服务器地址
- 大厅延迟显示基于独立 `probe` 探测
- 房间显示真实游戏版本、真实 MOD 版本、`relay` 状态和是否已开局
- 建房弹窗支持选择最大人数（4-8 人），自动适配难度缩放、营地座位、商店布局和宝箱分配；检测到 RMP 等外部扩展人数 MOD 时自动跳过内置补丁
- 建房、续局自动回挂和手动 LAN Host 会自动对齐扩展人数补丁的 `maxPlayers`
- 加入失败会细分为版本不一致、MOD 不一致、房间已开局、房间已满等原因
- 多人续局存档会自动和大厅房间绑定，房主重新进入续局时会自动重新发布
- 续局接管弹窗同时显示角色名和玩家名（如"铁甲战士（小明）"），方便多人选同角色时准确找回自己的槽位
- 设置区提供“复制本地调试报告”按钮，方便把 `roomId`、玩家 ID 和本地失败日志一键发给开发者
- 安装包内的默认大厅地址、兼容档位和连接策略以 `lobby-defaults.json` 为准
- 当前公开包默认指向阿里云大厅 `47.111.146.69:8787`，公共服务器目录是 `47.111.146.69:18787`，并固定使用 `test_relaxed + relay-only`
- 如果默认大厅拥堵或不可用，可在游戏内通过 `快速切换服务器` 改写当前客户端的 HTTP 覆盖地址
- 如果当前服务器配置了大厅公告，客户端会在顶部自动轮播展示；桌面端使用点状页码，窄屏横屏模式下改为 `1/N` 数字指示
- 如果需要切到其他中心目录服务，可在开发网络设置里填写 `中心服务器覆盖`
- `sts2_lan_connect.json` 是当前发布包内的 MOD 版本单一真源

## 安装前

- 先关闭《Slay the Spire 2》
- 保证所有联机玩家使用同一版 MOD
- 如果发布包里已经包含 `lobby-defaults.json`，普通玩家不需要手动填写大厅地址
- 如果你正在使用 `Clash`、`Surge`、系统全局代理或 `TUN`，请让大厅服务器 IP 走 `DIRECT`

## 一键安装 / 卸载

macOS：

- 双击 `install-sts2-lan-connect-macos.command`
- 如果已安装 MOD，则自动卸载
- 如果未安装 MOD，则自动安装
- 安装 / 卸载后会自动刷新 `SlayTheSpire2.app` 的 macOS 签名

Windows：

- 双击 `install-sts2-lan-connect-windows.bat`
- 如果已安装 MOD，则自动卸载
- 如果未安装 MOD，则自动安装

## 命令行安装

macOS：

```bash
./install-sts2-lan-connect-macos.sh --install --package-dir .
```

Windows：

```powershell
powershell -ExecutionPolicy Bypass -File .\install-sts2-lan-connect-windows.ps1 -Action Install -PackageDir .
```

## 命令行卸载

macOS：

```bash
./install-sts2-lan-connect-macos.sh --uninstall --package-dir .
```

Windows：

```powershell
powershell -ExecutionPolicy Bypass -File .\install-sts2-lan-connect-windows.ps1 -Action Uninstall -PackageDir .
```

## 切换行为

- 未安装时会复制 `sts2_lan_connect.dll`、`sts2_lan_connect.pck`、`sts2_lan_connect.json`
- 如果包里存在 `lobby-defaults.json`，会一并复制到游戏 `mods/sts2_lan_connect/`
- macOS 安装 / 卸载时会自动刷新 app 签名
- 安装时会执行一次 vanilla 到 modded 的单向存档同步

如果只想安装 MOD、不做存档同步：

macOS：

```bash
./install-sts2-lan-connect-macos.sh --install --package-dir . --no-save-sync
```

Windows：

```powershell
powershell -ExecutionPolicy Bypass -File .\install-sts2-lan-connect-windows.ps1 -Action Install -PackageDir . -NoSaveSync
```

## 使用要点

- 房间列表支持关键词搜索、分页和筛选
- `公开` / `上锁` 互斥，`可加入` 可叠加
- 进入房间后可通过右上角 `房间聊天` 按钮展开聊天面板
- 顶部公告栏默认每 6 秒轮播一条，鼠标悬停时会暂停，底部进度条会从左往右累积后切换
- 刷新失败或延迟异常时，可优先尝试 `切换服务器`
- 单击房间卡片会选中目标房间，双击会直接尝试加入
- 如果加入时间较长，界面会显示阶段化进度提示
- 如果提示 `MOD 不一致`，当前版本会直接弹窗告诉你缺少哪些 MOD，即使在宽松兼容模式下连接失败也会给出具体的 MOD 名称
