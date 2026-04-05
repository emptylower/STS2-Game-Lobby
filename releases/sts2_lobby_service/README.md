# STS2 Lobby Service

`STS2 Lobby Service` 是 `STS2 LAN Connect` 的大厅服务端，负责：

- 房间目录
- 房间密码校验
- 房主心跳与僵尸房间清理
- 控制通道握手与广播
- 控制通道会按房间透明广播聊天 envelope，可直接承载房间聊天
- 向客户端返回 `ENet` 直连优先、失败时自动切 relay 的连接计划
- 保存续局大厅房间的 `savedRun` 元数据与可接管角色槽位
- `maxPlayers/currentPlayers` 上限已放宽，用于兼容扩展人数房间元数据
- 在 join 前置校验里区分 `version_mismatch`、`mod_version_mismatch`、`mod_mismatch`、`room_started`、`room_full`
- 记录 `direct_timeout` / `relay_success` / `relay_failure` 等连接阶段日志
- 内置子服务器控制面板 `/server-admin`
- 子服务器控制面板可维护大厅公告，并通过 `GET /announcements` 下发给客户端
- 子服务器控制面板会显式展示 `未申请`、`已提交待审`、`已加入公开列表`、`已拒绝`、`配置错误`、`同步失败` 等状态，并在异常时弹出提醒
- 子服务器控制面板的 header 与状态区已补齐响应式布局，移动端和桌面端窄窗口下不再互相挤压
- 向官方母面板自动上报公开申请、claim 令牌和 3 分钟心跳

它不负责：

- 战斗同步
- 账号系统
- NAT 必成功穿透

当前 relay 的定位是“直连失败时的房间级兜底路径”，不是完整的独立联机协议。

说明：

- 官方公共服务器母面板不再随公开仓库一起发布
- 当前公开仓库只保留 `lobby-service` 本体
- 如果你要进入官方公开列表，只需要把 `SERVER_REGISTRY_BASE_URL` 指向官方母面板，并在 `/server-admin` 中打开“公开列表申请”

## Docker 部署

如果只想单独容器化当前 `lobby-service`，也可以直接在本目录下使用：

```bash
cp deploy/lobby-service.docker.env.example deploy/lobby-service.docker.env
$EDITOR deploy/lobby-service.docker.env

docker compose -f deploy/docker-compose.lobby-service.yml build
docker compose -f deploy/docker-compose.lobby-service.yml up -d
```

默认会把 `./deploy/data/lobby-service` 挂到容器内 `/app/data`，并把 `SERVER_ADMIN_STATE_FILE` 指向 `/app/data/server-admin.json`。

如果部署机器拉 Docker Hub 很慢，可以先复制 `deploy/.env.example` 为 `deploy/.env`，再把 `STS2_NODE_IMAGE` 改成国内镜像。

注意：

- Docker 方式也一样需要把 `RELAY_PUBLIC_HOST` 或 `SERVER_REGISTRY_PUBLIC_*` 改成公网 IP / 域名
- 这三个 `SERVER_REGISTRY_PUBLIC_*` 不是 Docker 自动推导出来的；如果它们仍是 `127.0.0.1`、`0.0.0.0`、`localhost` 或占位值，母面板无法反向探测这台子服

日志维护默认由 Docker `json-file` 轮转处理：

- `10MB` 单文件上限
- `5` 个历史文件

## systemd 一键部署

从仓库根目录执行：

```bash
sudo ./scripts/install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

这个脚本会自动：

- 复制服务文件到 `/opt/sts2-lobby/lobby-service`
- 执行 `npm ci`
- 执行 `npm run build`
- 首次安装时生成 `.env`
- 生成 `/opt/sts2-lobby/start-lobby-service.sh`
- 在 systemd 可用且以 root 执行时，自动安装并启动 `sts2-lobby.service`

安装后健康检查：

```bash
curl http://127.0.0.1:8787/health
```

如果你准备让这台 `systemd` 子服务进入官方公开列表，建议首次安装时直接带上公网主机名：

```bash
sudo ./scripts/install-lobby-service-linux.sh \
  --install-dir /opt/sts2-lobby \
  --relay-public-host <你的公网 IP 或域名>
```

这样安装脚本会自动把 `SERVER_REGISTRY_PUBLIC_BASE_URL`、`SERVER_REGISTRY_PUBLIC_WS_URL`、`SERVER_REGISTRY_BANDWIDTH_PROBE_URL` 写成同一套公网地址。

如果你已经装好了，再手动编辑：

- `/opt/sts2-lobby/lobby-service/.env`

至少保证下面两种方式之一成立：

- 方式 A：配置 `RELAY_PUBLIC_HOST=<公网 IP 或域名>`
- 方式 B：显式配置全部 `SERVER_REGISTRY_PUBLIC_*`

否则子面板虽然可能仍能向母面板发出申请请求，但母面板回头探测时拿到的是本机地址，公开申请就会失败。

## 生成 SERVER_ADMIN_PASSWORD_HASH

`SERVER_ADMIN_PASSWORD_HASH` 不是明文密码，格式是 `salt:hash`。

仓库已经内置了生成脚本：

```bash
cd lobby-service
npm run hash-admin-password -- '你的面板密码'
```

输出结果直接填进 `.env`：

```text
SERVER_ADMIN_PASSWORD_HASH=<上一步输出的整串内容>
```

会话密钥可以单独生成：

```bash
node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"
```

再填入：

```text
SERVER_ADMIN_SESSION_SECRET=<上一步输出的随机字符串>
```

## 外部页面怎么打开

当前 `lobby-service` 对外提供的是接口和管理面板，不提供给玩家直接浏览房间的独立网页大厅。

自部署后，浏览器里最常用的外部地址是：

- 子服务管理面板：`http://<你的公网 IP 或域名>:8787/server-admin`
- 健康检查：`http://<你的公网 IP 或域名>:8787/health`
- 公告公开接口：`http://<你的公网 IP 或域名>:8787/announcements`
- 房间列表接口：`http://<你的公网 IP 或域名>:8787/rooms`

说明：

- 要让外部浏览器能打开，至少需要放行 `8787/TCP`
- 如果你用了域名，直接把上面的 `<你的公网 IP 或域名>` 替换成域名即可
- `/server-admin` 页面能打开，不代表一定能修改设置；如果没配置 `SERVER_ADMIN_PASSWORD_HASH` 和 `SERVER_ADMIN_SESSION_SECRET`，它只能浏览，不能登录保存
- 玩家平时不需要打开这些网页；玩家加入房间走的是游戏客户端，浏览器页面主要给服主做健康检查和管理

## 手动运行

```bash
cd /path/to/STS2_Learner/lobby-service
npm ci
npm run build
npm start
```

默认监听：

- HTTP: `http://0.0.0.0:8787`
- WebSocket: `ws://0.0.0.0:8787/control`
- Relay UDP: `udp://0.0.0.0:39000-39149`

公网部署时至少需要放行：

- `8787/TCP`
- `39000-39149/UDP`

## 打包分发

如果要把服务端单独打包给部署机器：

```bash
./scripts/package-lobby-service.sh
```

产物：

- `lobby-service/release/sts2_lobby_service/`
- `lobby-service/release/sts2_lobby_service.zip`

打包结果现在也会包含：

- `Dockerfile`
- `.dockerignore`
- `deploy/docker-compose.lobby-service.yml`
- `deploy/lobby-service.docker.env.example`

如果这个服务是放进公共双服务 Docker 栈里运行，当前推荐直接使用宿主机网络，而不是再让 Docker bridge 发布整段 relay UDP 端口。

这是为了规避已在线上复现过的问题：某些云主机上，`lobby-service` 一旦通过 bridge 映射大段 UDP relay 端口，可能会同时拖慢 `8787`、`18787`，甚至让 SSH 只剩 TCP 连接但不回 banner。

## 环境变量

- `HOST`
- `PORT`
- `HEARTBEAT_TIMEOUT_SECONDS`
- `TICKET_TTL_SECONDS`
- `WS_PATH`
- `RELAY_BIND_HOST`
- `RELAY_PUBLIC_HOST`
- `RELAY_PORT_START`
- `RELAY_PORT_END`
- `RELAY_HOST_IDLE_SECONDS`
- `RELAY_CLIENT_IDLE_SECONDS`
- `STRICT_GAME_VERSION_CHECK`
- `STRICT_MOD_VERSION_CHECK`
- `CONNECTION_STRATEGY`
- `SERVER_ADMIN_USERNAME`
- `SERVER_ADMIN_PASSWORD_HASH`
- `SERVER_ADMIN_SESSION_SECRET`
- `SERVER_ADMIN_SESSION_TTL_HOURS`
- `SERVER_ADMIN_STATE_FILE`
- `SERVER_REGISTRY_BASE_URL`
- `SERVER_REGISTRY_SYNC_INTERVAL_SECONDS`
- `SERVER_REGISTRY_SYNC_TIMEOUT_MS`
- `SERVER_REGISTRY_PUBLIC_BASE_URL`
- `SERVER_REGISTRY_PUBLIC_WS_URL`
- `SERVER_REGISTRY_BANDWIDTH_PROBE_URL`
- `SERVER_REGISTRY_PROBE_FILE_BYTES`

示例见 [`.env.example`](./.env.example)。

这些开关的含义：

- `STRICT_GAME_VERSION_CHECK=false` 时，服务端不会因为游戏版本字符串不同而拒绝 join
- `STRICT_MOD_VERSION_CHECK=false` 时，服务端不会因为 MOD 版本字符串不同而拒绝 join
- 如果客户端和房主都上报了 `modList`，服务端会额外比较双方缺失项，并在 `mod_mismatch` 里返回 `missingModsOnLocal` / `missingModsOnHost`
- `CONNECTION_STRATEGY` 可选 `direct-first`、`relay-first`、`relay-only`
- 当前公开服部署默认使用 `relay-only`；如果要回到其他策略，请显式覆盖 `CONNECTION_STRATEGY`
- `SERVER_ADMIN_*` 控制子面板登录，不配置密码哈希和会话密钥时，`/server-admin` 页面仍可打开，但无法登录修改设置
- `SERVER_ADMIN_STATE_FILE` 默认保存这台子服的显示名称、公开设置、公告配置和同步状态
- `SERVER_REGISTRY_BASE_URL` 只负责“把申请和心跳发到哪台母面板”；当前默认写成官方母面板 `http://47.111.146.69:18787`
- 只要 `/server-admin` 里打开了“公开列表申请”，子服务就会自动创建申请、自动 claim 审核结果，并持续同步心跳
- `SERVER_REGISTRY_PUBLIC_*` 用于告诉母面板“这台子服务器对外的 HTTP / WS / 带宽探针地址”
- 如果 `SERVER_REGISTRY_PUBLIC_*` 留空，服务端会先尝试用 `RELAY_PUBLIC_HOST` 推导；如果连 `RELAY_PUBLIC_HOST` 也没配，最终会退回本机地址，母面板无法从公网访问
- 现在如果你打开了公开申请，但上报地址仍是 `127.0.0.1`、`0.0.0.0`、`localhost` 这类本机地址，子面板会直接把同步状态标成 `公网地址配置错误`

## API

- `GET /health`
- `GET /probe`
- `GET /registry/bandwidth-probe.bin`
- `GET /server-admin`
- `POST /server-admin/login`
- `GET /server-admin/settings`
- `PATCH /server-admin/settings`
- `GET /rooms`
- `GET /announcements`
- `POST /rooms`
- `POST /rooms/:id/join`
- `POST /rooms/:id/heartbeat`
- `POST /rooms/:id/connection-events`
- `DELETE /rooms/:id`
- `WS /control`

当前和续局联机相关的关键字段：

- `POST /rooms`
  - 支持可选的 `savedRun`
  - `savedRun.saveKey` 用于把续局存档和大厅房间绑定
  - `savedRun.slots` 描述每个可接管角色槽位及其 `netId`
- `POST /rooms/:id/join`
  - 支持可选的 `desiredSavePlayerNetId`
  - 支持可选的 `modList`
  - 当续局房间存在多个空闲角色槽位时，客户端必须显式选择一个槽位再加入
  - 失败时会明确返回 `version_mismatch`、`mod_version_mismatch`、`mod_mismatch`、`room_started`、`room_full` 等错误码
- `POST /rooms/:id/heartbeat`
  - 支持上报 `connectedPlayerNetIds`
  - 服务端会据此更新哪些续局角色槽位当前已被占用
- `POST /rooms/:id/connection-events`
  - 客户端会上报 `direct_timeout`、`relay_success`、`relay_failure` 等阶段事件
  - 这些记录会进入服务端日志，便于排查公网联机失败原因

## 控制通道约定

查询参数：

- `roomId`
- `controlChannelId`
- `role=host|client`
- `token` 或 `ticketId`

当前实现包括：

- host/client 握手校验
- ping/pong 保活
- 同房间 peers 广播

这已经足够支撑当前大厅模式，但整体联机仍以游戏原生 `ENet` 直连为主。

## 日志排查

推荐直接看 systemd journal：

```bash
journalctl -u sts2-lobby.service -n 100 --no-pager
```

常见日志包括：

- `create room`
- `join ticket issued`
- `relay_host_registered`
- `relay_client_connected`
- `connection_event ... phase=direct_timeout`
- `connection_event ... phase=relay_success`
- `connection_event ... phase=relay_failure`
- `relay_allocated`
- `relay_removed`

如果日志里能看到 `create room`、`join ticket issued`，却始终没有 `relay_host_registered`，通常不是服务端 API 挂了，而是客户端到 relay 端口段的 UDP 没有真正打到服务器。常见原因包括：

- 服务器公网 `39000-39149/UDP` 没有放行
- 客户端启用了 `Clash`、`Surge`、系统全局代理或 `TUN`，大厅服务器 IP 没有走 `DIRECT`

如果当前是 Docker 部署，则改看：

```bash
docker compose -f deploy/docker-compose.lobby-service.yml logs --tail 200 -f
```
