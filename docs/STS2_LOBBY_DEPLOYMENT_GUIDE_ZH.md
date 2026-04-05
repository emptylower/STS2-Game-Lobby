# STS2 游戏大厅部署指南

这份文档对应当前公开仓库里的两部分：

- 服务端：`lobby-service/`
- 客户端：`sts2-lan-connect/`

说明：

- 官方公共服务器母面板为私有服务，不再包含在公开仓库中
- 公开仓库里的子服务仍然可以接入官方母面板
- 当前客户端默认大厅固定为阿里云：`http://47.111.146.69:8787`

当前推荐版本：

- 客户端：`0.2.2`
- 子服务：`0.2.2`

目标结果：

1. 在 Linux 机器上部署并启动 `lobby-service`
2. 让这台子服务在需要时自动向官方母面板申请公开展示
3. 生成带默认大厅绑定的客户端发布包
4. 在公开仓库中同步源码和发布产物

本次版本新增：

- 房间聊天面板与控制通道聊天消息
- 客户端 / 子服务对扩展人数房间元数据的统一兼容
- 子服务器控制台响应式布局修复，移动端和桌面端窄窗口下不再出现 header / 登录状态 / 退出按钮互相挤压
- 子服务器控制台自动轮询状态时不再覆盖未保存的设置和公告草稿；手动重新加载配置前会先要求确认
- 客户端建房支持选择 5-8 人房间，包含难度缩放、营地座位、商店布局和宝箱分配适配
- 续局接管弹窗现在同时显示角色名和玩家名，方便准确找回槽位

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

默认需要放行：

- `8787/TCP`
- `39000-39149/UDP`

部署完成后检查：

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8787/probe
```

如果你准备让这台 `systemd` 子服务进入官方公开列表，建议首次安装时直接写上公网主机名：

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <你的公网 IP 或域名>
```

这样安装脚本会自动补出：

- `SERVER_REGISTRY_PUBLIC_BASE_URL`
- `SERVER_REGISTRY_PUBLIC_WS_URL`
- `SERVER_REGISTRY_BANDWIDTH_PROBE_URL`

如果你已经安装完了，也可以后补 `/opt/sts2-lobby/lobby-service/.env`，至少保证下面二选一：

- 配 `RELAY_PUBLIC_HOST=<公网 IP 或域名>`
- 或显式填写全部 `SERVER_REGISTRY_PUBLIC_*`

如果这几项都没配，子服务会把本机地址上报给母面板，母面板无法从公网反向探测。

### 管理面板登录哈希怎么生成

`SERVER_ADMIN_PASSWORD_HASH` 不是明文密码，必须填 `salt:hash`。

仓库自带生成脚本：

```bash
cd lobby-service
npm run hash-admin-password -- '你的面板密码'
```

把输出整串填进 `.env`：

```text
SERVER_ADMIN_PASSWORD_HASH=<上一步输出的整串内容>
```

`SERVER_ADMIN_SESSION_SECRET` 可以这样生成：

```bash
node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"
```

### 对外页面如何打开

如果这台子服务已经绑定了公网 IP 或域名，并且安全组 / 防火墙已经放行 `8787/TCP`，外部浏览器可直接访问：

- 管理面板：`http://<你的公网 IP 或域名>:8787/server-admin`
- 健康检查：`http://<你的公网 IP 或域名>:8787/health`
- 公告接口：`http://<你的公网 IP 或域名>:8787/announcements`
- 房间列表接口：`http://<你的公网 IP 或域名>:8787/rooms`

说明：

- `lobby-service` 没有给玩家直接浏览房间的独立网页大厅；玩家联机仍通过游戏客户端完成
- 服主如果要维护公告、公开列表申请和带宽设置，打开的是 `/server-admin`
- 如果页面能打开但无法登录，优先检查 `.env` 里的 `SERVER_ADMIN_PASSWORD_HASH` 和 `SERVER_ADMIN_SESSION_SECRET`

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

### 方式 C：先清理旧版本再重装

```bash
sudo systemctl stop sts2-lobby || true
sudo rm -rf /opt/sts2-lobby/lobby-service /opt/sts2-lobby/start-lobby-service.sh
sudo find /opt/sts2-lobby -maxdepth 1 -type f \( -name 'sts2_lobby_service*.zip' -o -name '*.tgz' \) -delete
sudo ./install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

## 二、官方公开列表说明

官方公共服务器母面板不在公开仓库里，但公开仓库内的子服务默认已经准备好接入它。

当前默认配置：

- 官方母面板：`http://47.111.146.69:18787`
- 官方默认大厅：`http://47.111.146.69:8787`

如果你使用仓库默认配置或安装脚本默认配置：

- `SERVER_REGISTRY_BASE_URL` 会默认写成 `http://47.111.146.69:18787`
- 这只表示“申请发往哪台母面板”，不表示母面板就一定能访问到你的子服务
- 当你在 `/server-admin` 里打开“公开列表申请”后，子服务会自动：
  - 创建申请
  - claim 审核结果
  - 按固定周期发送心跳

要让公开申请真正成立，还必须保证母面板能拿到这台子服的公网地址：

- `SERVER_REGISTRY_PUBLIC_BASE_URL`
- `SERVER_REGISTRY_PUBLIC_WS_URL`
- `SERVER_REGISTRY_BANDWIDTH_PROBE_URL`

如果这三项留空，服务端会优先尝试从 `RELAY_PUBLIC_HOST` 推导；如果连 `RELAY_PUBLIC_HOST` 也没配，就会退回 `127.0.0.1` / `0.0.0.0` 这种本机绑定地址。

因此第 2 个问题的结论是：

- 不是只有 `systemd` 才会出问题，Docker 也一样会出问题
- 真正的关键不是“是不是 Docker”，而是“上报给母面板的公网地址是否可达”
- 当前版本已经补了显式校验：如果公开申请上报的是本机地址，`/server-admin` 会直接显示 `公网地址配置错误`
- `/server-admin` 现在会显式展示 `未申请`、`已提交待审`、`已加入公开列表`、`已拒绝`、`申请发送失败`、`同步失败` 等状态，并对异常弹出提醒

如果你不想接入官方公开列表，可以清空：

```text
SERVER_REGISTRY_BASE_URL=
```

或者直接不要打开 `/server-admin` 里的“公开列表申请”。

### Docker 方式的额外说明

Docker 并不会自动替你填公网地址。

如果你使用：

- `deploy/lobby-service.docker.env.example`
- `deploy/docker-compose.lobby-service.yml`

仍然需要把下面这些占位值改成真实公网 IP / 域名：

- `RELAY_PUBLIC_HOST`
- `SERVER_REGISTRY_PUBLIC_BASE_URL`
- `SERVER_REGISTRY_PUBLIC_WS_URL`
- `SERVER_REGISTRY_BANDWIDTH_PROBE_URL`

如果你用了反向代理、HTTPS 或非 `8787` 外部端口，也要按真实外网地址手动改，不要保留默认值。

### 线上故障记录

这次线上迁移时，实际遇到过一个和 Docker 网络模型相关的问题：

- 当 `lobby-service` 在小规格阿里云 ECS 上通过 Docker bridge 发布大段 UDP relay 端口时
- 可能同时出现 `8787` 空响应、`18787` 超时
- 更严重时，`22` 端口只剩 TCP 连接，但 SSH banner 不返回

最终结论：

- 问题不在子服务的业务逻辑
- 问题点在“Docker bridge + 大段 UDP 端口映射”这层
- 官方线上已经改为让 `lobby-service` 走宿主机网络，避免再触发这一类故障

如果你将来在自己的私有环境中也部署“子服务 + 私有母面板”的同机栈，建议优先避开这类大段 UDP bridge 发布方式。

## 三、客户端打包

### 1. 使用仓库默认大厅

当前仓库内的 [`lobby-defaults.json`](../sts2-lan-connect/lobby-defaults.json) 已经固定指向：

- `baseUrl`: `http://47.111.146.69:8787`
- `registryBaseUrl`: `http://47.111.146.69:18787`
- `wsUrl`: `ws://47.111.146.69:8787/control`

所以如果你不额外设置环境变量，打出来的客户端默认大厅就是阿里云这台。

### 2. 生成客户端包

```bash
./scripts/package-sts2-lan-connect.sh
```

产物：

- `sts2-lan-connect/release/sts2_lan_connect/`
- `sts2-lan-connect/release/sts2_lan_connect-release.zip`

### 3. 如需临时覆盖默认大厅

```bash
export STS2_LOBBY_DEFAULT_BASE_URL="http://<your-host-or-domain>:8787"
export STS2_LOBBY_DEFAULT_WS_URL="ws://<your-host-or-domain>:8787/control"
export STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL="http://<your-registry-host-or-domain>:18787"

./scripts/package-sts2-lan-connect.sh
```

如果不显式设置 `STS2_LOBBY_DEFAULT_WS_URL`，打包脚本会根据 `STS2_LOBBY_DEFAULT_BASE_URL` 自动推导。

## 四、客户端安装 / 卸载

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

## 五、公开仓库同步

如果你本地已经 clone 了公开仓库 `STS-Game-Lobby`：

```bash
./scripts/sync-release-repo.sh --repo-dir ~/Desktop/STS-Game-Lobby
```

同步结果：

- 源码目录会同步到公开仓库根目录
- 发布产物会集中同步到公开仓库 `releases/`
- 私有母面板源码、脚本和 release 产物不会再同步到公开仓库

## 六、游戏内验证

建议至少验证：

1. 大厅刷新是否正常
2. 顶部公告轮播是否正常
3. 搜索、分页、筛选是否正常
4. 进房后 `房间聊天` 是否能双向收发消息，未读角标和拖动保存位置是否正常
5. 建房和加入是否正常；如果使用扩展人数补丁，确认房间人数元数据与实际配置一致
6. `复制本地调试报告` 是否可用
7. 外部浏览器是否能打开 `http://<公网 IP 或域名>:8787/server-admin`
8. 子服 `/server-admin` 是否可登录并维护大厅公告
9. 如果打开了“公开列表申请”，检查同步状态是否进入 `pending_review`、`approved` 或 `heartbeat_ok`
