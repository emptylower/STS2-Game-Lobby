# STS2 游戏大厅双端部署指南

这份文档对应当前仓库的两端：

- 服务端：`lobby-service/`
- 客户端：`sts2-lan-connect/`

目标结果：

1. 在 Linux 机器上部署并启动 `lobby-service`
2. 生成带默认大厅与中心注册表绑定的客户端发布包
3. 房主和玩家通过一键安装 / 卸载脚本完成客户端管理
4. 在公开仓库中同步源码和发布产物

## 一、服务端部署

### 方式 A：直接从仓库部署

```bash
sudo ./scripts/install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

脚本会自动：

- 复制源码到 `/opt/sts2-lobby/lobby-service`
- 首次安装生成 `.env`
- 执行 `npm ci`
- 执行 `npm run build`
- 生成启动脚本
- 在 root + systemd 环境下自动安装并启动 `sts2-lobby.service`
- 如果传入并行实例的 service 名、端口与注册表变量，也可以直接安装 feature 测试实例

默认需要放行：

- `8787/TCP`
- `39000-39511/UDP`

部署完成后检查：

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8787/probe
```

### 方式 B：先打包再发到服务器

```bash
./scripts/package-lobby-service.sh
```

产物：

- `lobby-service/release/sts2_lobby_service/`
- `lobby-service/release/sts2_lobby_service.zip`

上传并解压后，在服务器执行：

```bash
sudo ./install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

### 阿里云 feature 并行实例示例

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /home/admin/sts2-lobby-feature \
  --service-name sts2-lobby-feature \
  --port 18787 \
  --relay-port-start 39100 \
  --relay-port-end 39163 \
  --registry-data-dir /home/admin/sts2-lobby-feature/lobby-service/data \
  --registry-official-base-url http://47.111.146.69:18787 \
  --registry-official-ws-url ws://47.111.146.69:18787/control \
  --admin-username admin \
  --admin-password-hash '<your-scrypt-hash>' \
  --admin-session-secret '<random-secret>'
```

这套并行实例不会覆盖现有 `sts2-lobby.service`，而是额外安装 `sts2-lobby-feature.service`。

## 二、客户端打包

### 1. 生成带默认大厅绑定的客户端包

```bash
export STS2_LOBBY_DEFAULT_BASE_URL="http://<your-host-or-domain>:8787"
export STS2_LOBBY_DEFAULT_WS_URL="ws://<your-host-or-domain>:8787/control"
export STS2_LOBBY_REGISTRY_BASE_URL="http://<your-host-or-domain>:8787"

./scripts/package-sts2-lan-connect.sh
```

如果不显式设置 `STS2_LOBBY_DEFAULT_WS_URL`，打包脚本会根据 `STS2_LOBBY_DEFAULT_BASE_URL` 自动推导。`STS2_LOBBY_REGISTRY_BASE_URL` 留空时会默认跟随 `STS2_LOBBY_DEFAULT_BASE_URL`。

产物：

- `sts2-lan-connect/release/sts2_lan_connect/`
- `sts2-lan-connect/release/sts2_lan_connect-release.zip`

### 2. 只刷新发布目录，不重新编译

```bash
./scripts/package-sts2-lan-connect.sh --skip-build
```

适用于只改了文档、安装脚本或发布目录内容的场景。

## 三、客户端安装 / 卸载

### macOS 玩家

- 双击 `install-sts2-lan-connect-macos.command`
- 或命令行执行：

```bash
./install-sts2-lan-connect-macos.sh --install --package-dir .
```

### Windows 玩家

- 双击 `install-sts2-lan-connect-windows.bat`
- 或 PowerShell 执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\install-sts2-lan-connect-windows.ps1 -Action Install -PackageDir .
```

## 四、公开仓库同步

如果你本地已经 clone 了公开仓库 `STS-Game-Lobby`：

```bash
./scripts/sync-release-repo.sh --repo-dir ~/Desktop/STS-Game-Lobby
```

同步结果：

- 源码目录会同步到公开仓库根目录
- 发布产物会集中同步到公开仓库 `releases/`
- 旧的根目录 release-only 布局会被清理掉

同步完成后，在公开仓库里执行常规的：

```bash
git add -A
git commit -m "Open source STS2 LAN Connect 0.2.1"
git push
```

## 五、游戏内验证

建议至少验证：

1. 大厅刷新是否正常
2. 搜索、分页、筛选是否正常
3. 建房和加入是否正常
4. 线路目录切换、提交服务器和恢复官方默认是否正常
5. `复制本地调试报告` 是否可用
