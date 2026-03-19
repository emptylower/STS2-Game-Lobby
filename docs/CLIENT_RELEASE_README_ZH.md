# STS2 LAN Connect 客户端安装说明

这是 `STS2 LAN Connect` 的客户端发布包。

## 当前版本

- 当前客户端版本：`0.2.1`
- 大厅支持搜索、分页和可叠加筛选
- 筛选支持 `公开`、`上锁`、`可加入`
- 大厅延迟显示基于独立 `probe` 探测
- 房间显示真实游戏版本、真实 MOD 版本、`relay` 状态和是否已开局
- 加入失败会细分为版本不一致、MOD 不一致、房间已开局、房间已满等原因
- 多人续局存档会自动和大厅房间绑定，房主重新进入续局时会自动重新发布
- 设置区提供“复制本地调试报告”按钮，方便把 `roomId`、玩家 ID 和本地失败日志一键发给开发者
- 大厅右侧提供“当前线路”卡片，可直接切换官方/社区服务器并提交新的社区服务器
- 安装包内的默认大厅地址、兼容档位和连接策略以 `lobby-defaults.json` 为准
- 当前 feature 测试包默认指向阿里云大厅 `47.111.146.69:18787`，并固定使用 `test_relaxed + relay-first`
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
- 单击房间卡片会选中目标房间，双击会直接尝试加入
- 如果加入时间较长，界面会显示阶段化进度提示
- 如果提示 `MOD 不一致`，当前版本会优先直接告诉你缺少哪些 MOD
