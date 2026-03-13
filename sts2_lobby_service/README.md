# STS2 Lobby Service

`STS2 Lobby Service` 是 `STS2 LAN Connect` 的大厅服务端，负责：

- 房间目录
- 房间密码校验
- 房主心跳与僵尸房间清理
- 控制通道握手与广播
- 向客户端返回 `ENet` 直连优先、失败时自动切 relay 的连接计划
- 保存续局大厅房间的 `savedRun` 元数据与可接管角色槽位
- 记录 `direct_timeout` / `relay_success` / `relay_failure` 等连接阶段日志

它不负责：

- 战斗同步
- 账号系统
- NAT 必成功穿透

当前 relay 的定位是“直连失败时的房间级兜底路径”，不是完整的独立联机协议。

## 一键部署

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

## 手动运行

```bash
cd /Users/mac/Desktop/STS2_Learner/lobby-service
npm ci
npm run build
npm start
```

默认监听：

- HTTP: `http://0.0.0.0:8787`
- WebSocket: `ws://0.0.0.0:8787/control`
- Relay UDP: `udp://0.0.0.0:39000-39063`

公网部署时至少需要放行：

- `8787/TCP`
- `39000-39063/UDP`

## 打包分发

如果要把服务端单独打包给部署机器：

```bash
./scripts/package-lobby-service.sh
```

产物：

- `lobby-service/release/sts2_lobby_service/`
- `lobby-service/release/sts2_lobby_service.zip`

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
- `RELAY_PORT_COOLDOWN_SECONDS`
- `ROOM_TOMBSTONE_SECONDS`
- `IGNORE_VERSION_MISMATCH`
- `FORCE_RELAY_ONLY`

示例见 [lobby-service/.env.example](/Users/mac/Desktop/STS2_Learner/lobby-service/.env.example)。

## API

- `GET /health`
- `GET /rooms`
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
  - 当续局房间存在多个空闲角色槽位时，客户端必须显式选择一个槽位再加入
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

- 服务器公网 `39000-39063/UDP` 没有放行
- 客户端启用了 `Clash`、`Surge`、系统全局代理或 `TUN`，大厅服务器 IP 没有走 `DIRECT`
