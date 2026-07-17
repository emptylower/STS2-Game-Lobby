# STS2 服务 Docker 化部署与运维指南

> 本文档对应 **v0.5.1**。部署仍只需要 `lobby-service` 一个容器；v0.5.1 在既有去中心化与聊天能力上新增加入前 gameplay MOD 私有预检。以前的"双服务"栈（lobby-service + server-registry + postgres）仅作历史运维参考。

## 一、当前推荐：lobby-service 单容器

v0.5.1 沿用 Cloudflare discovery worker（`https://sts2-gamelobby-register.xyz`）聚合去中心化节点，本地不需要 PostgreSQL 或 `server-registry`。升级时应替换完整 lobby-service 包并重新构建容器，确保 MOD 预检、chat gateway、管理面板和 env 默认值来自同一版本。

### 1.1 准备 env 文件

从仓库根目录开始：

```bash
cp deploy/lobby-service.env.example deploy/lobby-service.env
$EDITOR deploy/lobby-service.env
```

必须替换的占位符：

- `RELAY_PUBLIC_HOST=` 改成你的公网 IP 或域名
- `PEER_SELF_ADDRESS=` 改成 `http://<你的公网 IP 或域名>:8787`（HTTPS 反代场景请写代理对外的真实 URL）
- `SERVER_ADMIN_PASSWORD_HASH=` 改成 `npm run hash-admin-password -- 'your-password'` 的输出
- `SERVER_ADMIN_SESSION_SECRET=` 改成 `node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"` 的输出

可选：

- `PEER_PUBLIC_LISTING_ENABLED=false` 如果只想跑私有节点（仍在节点网络里但不公开）
- `PEER_NETWORK_ENABLED=false` 如果完全不想加入节点网络
- `MOD_SYNC_ENABLED=true` 只在 v0.5.1 候选测试节点开启；正式节点验收前保持默认 `false`

`MOD_SYNC_MAX_DESCRIPTORS=64` 和 `MOD_SYNC_MAX_PAYLOAD_BYTES=65536` 是协议硬上限，不应调高。预检只返回差异，不签发 join ticket、不改变房间人数或状态；游戏版本不同始终拒绝。服务端不会托管或传输任何 MOD DLL、PCK、ZIP。

> ⚠️ `PEER_SELF_ADDRESS` 是 v0.4.0+ 新部署最容易漏掉的一项。漏了就会在 `/server-admin` 看到"节点网络未配置"。

### 1.2 启动

```bash
docker compose -f lobby-service/deploy/docker-compose.lobby-service.yml build
docker compose -f lobby-service/deploy/docker-compose.lobby-service.yml up -d
```

默认对外端口：

- `8787/TCP` —— HTTP / WebSocket
- `39000-39149/UDP` —— relay 端口段

默认日志策略：Docker `json-file`，`max-size=10m`，`max-file=5`。

### 1.3 验证

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8787/peers/health
curl http://127.0.0.1:8787/announcements
docker compose -f lobby-service/deploy/docker-compose.lobby-service.yml ps
```

`/peers/health` 应该显示：

- `publicListing: true`（公开节点）
- `selfAddress` 等于你配的公网 URL
- `activePeers` 在启动 1-2 分钟后变为正数

浏览器侧打开 `http://<公网 IP 或域名>:8787/server-admin`，登录后查看"节点网络"区域：

| 状态 | 含义 |
|------|------|
| `节点网络未启用` | `PEER_NETWORK_ENABLED=false` |
| `节点网络未配置` | 启用了但 `PEER_SELF_ADDRESS` 为空 |
| `仅私有可见` | `PEER_SELF_ADDRESS` 已配但公开开关关闭 |
| `正在加入节点网络` | 已公开但尚未观察到外部节点 |
| `已加入节点网络` | 已观察到外部节点，列表传播正常 |

### 1.4 日志

```bash
docker compose -f lobby-service/deploy/docker-compose.lobby-service.yml logs --tail 200 -f lobby-service
```

关键日志条目：

- `[peer] mounted; self=...` —— 节点网络已启动
- `[peer] disabled (set PEER_SELF_ADDRESS to enable)` —— `PEER_SELF_ADDRESS` 没读到
- `relay_host_registered`、`relay_client_connected` —— relay 流量
- `connection_event phase=...` —— 客户端联机阶段事件

### 1.5 升级

```bash
git pull   # 或同步打包产物
docker compose -f lobby-service/deploy/docker-compose.lobby-service.yml build
docker compose -f lobby-service/deploy/docker-compose.lobby-service.yml up -d
curl http://127.0.0.1:8787/health
```

数据目录默认挂载在 `lobby-service/deploy/data/lobby-service`，升级不会丢失 `server-admin.json` 和 peer 状态。

### 1.6 常见排障

| 现象 | 优先排查 |
|------|----------|
| `/server-admin` 显示"节点网络未配置" | `deploy/lobby-service.env` 里 `PEER_SELF_ADDRESS` 是否填了真实公网 URL |
| 状态卡在"正在加入节点网络" | 公网是否真能反向访问 `PEER_SELF_ADDRESS`；防火墙是否放行 `8787/TCP` |
| 客户端进房后没有 relay 流量 | `39000-39149/UDP` 是否真正放行；`RELAY_PUBLIC_HOST` 是否写了真实公网地址 |
| 容器起来但 `8787` 空响应、SSH 也变慢 | 见下方"附录 A：Docker bridge UDP 端口段拖坏宿主网络" |

也可使用诊断脚本：

```bash
sudo ./scripts/diagnose-lobby-peer.sh
```

---

## 二、可选：自建公开列表 stack（v0.3.x 兼容路径）

> **何时需要**：你想完全自托管节点列表服务（不依赖 Cloudflare discovery worker），或者你正在维护 v0.3.x 时代部署、暂时还不想迁到 v0.4.0 的 CF 发现路径。  
> **v0.4.0 默认部署不需要看这一节。**

仓库里仍保留三容器栈：

1. `sts2-lobby-service`
2. `sts2-server-registry`
3. `sts2-server-registry-postgres`

注意：v0.4.0 的 `lobby-service` 已经**不再读取**任何 `SERVER_REGISTRY_*` 环境变量；即便和 `server-registry` 一起部署，lobby 端也不会向它上报。`server-registry` 现在只是一个独立的、可选的公开列表服务，给那些想跑自家 discovery 的运维一个完整的可参考实现。

如果你只是想跑 v0.4.0 的"标准"部署，请回到第一节使用单容器 compose。

### 2.1 打包

```bash
./scripts/package-server-stack-docker.sh
```

产物：

- `releases/sts2_server_stack_docker/`
- `releases/sts2_server_stack_docker.zip`

### 2.2 部署

```bash
sudo ./scripts/install-server-stack-docker-linux.sh --install-dir /opt/sts2-server-stack-docker
```

或解压发布包后：

```bash
unzip sts2_server_stack_docker.zip
cd sts2_server_stack_docker
sudo ./install-server-stack-docker-linux.sh --install-dir /opt/sts2-server-stack-docker
```

首次执行时，脚本会：

- 复制 `deploy/`、`lobby-service/`、`server-registry/`
- 自动创建 `deploy/lobby-service.env`、`deploy/server-registry.env`、`deploy/postgres.env`
- 自动创建数据目录
- 构建 Docker 镜像

发现仍是示例占位符时，脚本会停止并要求先编辑 env 文件。

### 2.3 env 文件清单

- `deploy/.env`（compose 默认变量，可调 Docker 镜像源）
- `deploy/postgres.env`
- `deploy/server-registry.env`
- `deploy/lobby-service.env`

必须替换的占位符：

- `CHANGE_ME_POSTGRES_PASSWORD`
- `CHANGE_ME_PASSWORD_HASH`
- `CHANGE_ME_SESSION_SECRET`
- `CHANGE_ME_SERVER_TOKEN_SECRET`
- `CHANGE_ME_PUBLIC_HOST`

要点：

- `lobby-service.env`：`RELAY_PUBLIC_HOST` + `PEER_SELF_ADDRESS` 必须是玩家能访问到的真实公网地址。`lobby-service` 在公共栈里默认使用宿主机网络，避免大段 UDP 端口发布把 Docker 网络层拖挂。
- `server-registry.env`：`DATABASE_URL` 必须指向容器内 PostgreSQL：`postgres://...@postgres:5432/...`。

### 2.4 启动与验证

```bash
docker compose \
  -p sts2-public-stack \
  -f /opt/sts2-server-stack-docker/deploy/docker-compose.public-stack.yml \
  up -d

curl http://127.0.0.1:8787/health
curl http://127.0.0.1:18787/health
curl http://127.0.0.1:18787/servers/
```

### 2.5 维护脚本

```bash
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker status
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker logs lobby-service --follow
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker backup
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker rebuild
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker prune-images
```

### 2.6 从旧 systemd 迁移到 Docker

推荐顺序：

1. 备份 `/opt/sts2-lobby`、`/opt/sts2-server-registry`、旧 `.env`、`lobby-service` 的 `data/server-admin.json`
2. `pg_dump <old_database_name> > registry-backup.sql`
3. 停掉旧 systemd 服务
4. 部署 Docker 栈
5. 导入数据库
6. `curl` 两个 `/health` 验证

如果你只用 v0.4.0 的 lobby（不再需要自建公开列表），直接跳过 server-registry 与 postgres，按第一节用单容器 compose 即可。

---

## 附录 A：Docker bridge UDP 端口段拖坏宿主网络

这是已经在线上复现过的问题。

典型症状：

- `docker start sts2-lobby-service` 后，`http://127.0.0.1:8787/health` 连得上但返回空响应
- 同机部署的其他服务也会一起变慢、超时
- 宿主机 `22` 端口还能建立 TCP，但 SSH banner 不返回，表现为 `Connection timed out during banner exchange`

排查结论：

- 真正的问题在于某些云主机上，`lobby-service` 通过 Docker bridge 发布一整段 relay UDP 端口时，会把宿主机网络层一起拖坏
- 与 systemd 服务、`server-registry` 容器、其他容器都无关

修复方法：

- `lobby-service` 改用 `network_mode: host`（双服务栈的 `docker-compose.public-stack.yml` 已经按此调整）
- 或者把 relay 端口段缩短

遇到这类现象时，不要继续反复 `restart` 或盲目重启容器；优先确认：

1. 该容器是否仍在用 bridge 网络发布 `39000-39149/UDP`
2. `deploy/lobby-service.env` 中 `RELAY_PORT_START` / `RELAY_PORT_END` 是否和实际放行范围一致

## 附录 B：日志轮转

`json-file` 驱动默认开启：

- 单个容器最多保留 5 个日志文件
- 每个文件最大 10MB

一般不需要额外清理；如果镜像层积累过多，执行：

```bash
docker image prune -f
```

或（双服务栈）：

```bash
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker prune-images
```
