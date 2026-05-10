# STS2 双服务 Docker 化部署与运维指南

这份文档对应当前两套服务：

- 子服务器大厅：`lobby-service/`
- 公共服务器母面板：`server-registry/`

当前推荐部署方式已经调整为 Docker Compose。原因很直接：

- 两个 Node 服务都能稳定容器化
- `server-registry` 依赖的 PostgreSQL 可以一起纳入同一套栈
- 日志、重启策略、升级和备份路径更统一
- 更适合后续做清理旧版本、滚动更新和环境迁移

## 一、容器栈组成

完整公共栈包含 3 个容器：

1. `sts2-lobby-service`
2. `sts2-server-registry`
3. `sts2-server-registry-postgres`

默认对外端口：

- `8787/TCP`：大厅 HTTP / WebSocket
- `39000-39149/UDP`：大厅 relay 端口段
- `18787/TCP`：母面板 HTTP

默认日志策略：

- Docker `json-file`
- `max-size=10m`
- `max-file=5`

这意味着日志默认会自动轮转，不需要再手动清理无限增长的容器日志。

## 二、本地打包

从仓库根目录执行：

```bash
./scripts/package-server-stack-docker.sh
```

产物：

- `releases/sts2_server_stack_docker/`
- `releases/sts2_server_stack_docker.zip`

压缩包内已经包含：

- 两个服务的源码
- 两个服务的 `Dockerfile`
- 顶层 `docker-compose.public-stack.yml`
- 示例环境变量文件
- Docker 安装脚本
- Docker 运维脚本

## 三、部署目录建议

推荐固定目录：

- 安装根目录：`/opt/sts2-server-stack-docker`
- 数据目录：`/opt/sts2-server-stack-docker/deploy/data`
- 环境变量目录：`/opt/sts2-server-stack-docker/deploy`
- 备份目录：`/opt/sts2-server-stack-docker/backups`

## 四、首次安装

### 方式 A：直接从源码仓库安装

```bash
sudo ./scripts/install-server-stack-docker-linux.sh --install-dir /opt/sts2-server-stack-docker
```

首次执行时，脚本会：

- 复制 `deploy/`、`lobby-service/`、`server-registry/`
- 自动创建 `deploy/lobby-service.env`
- 自动创建 `deploy/server-registry.env`
- 自动创建 `deploy/postgres.env`
- 自动创建数据目录
- 构建 Docker 镜像

如果脚本发现还是示例占位符，会停止在“准备完成”状态，要求你先编辑 env 文件。

### 方式 B：使用打包后的发布包安装

```bash
unzip sts2_server_stack_docker.zip
cd sts2_server_stack_docker
sudo ./install-server-stack-docker-linux.sh --install-dir /opt/sts2-server-stack-docker
```

## 五、环境变量准备

最少需要修改这 3 个文件：

- `deploy/.env`
- `deploy/postgres.env`
- `deploy/server-registry.env`
- `deploy/lobby-service.env`

如果部署机器直连 Docker Hub 很慢或经常超时，可以先改：

- `deploy/.env` 里的 `STS2_NODE_IMAGE`
- `deploy/.env` 里的 `STS2_POSTGRES_IMAGE`

例如切到国内镜像：

```text
STS2_NODE_IMAGE=docker.m.daocloud.io/library/node:20-bookworm-slim
STS2_POSTGRES_IMAGE=docker.m.daocloud.io/library/postgres:16-alpine
```

必须替换的占位符包括：

- `CHANGE_ME_POSTGRES_PASSWORD`
- `CHANGE_ME_PASSWORD_HASH`
- `CHANGE_ME_SESSION_SECRET`
- `CHANGE_ME_SERVER_TOKEN_SECRET`
- `CHANGE_ME_PUBLIC_HOST`

关键说明：

- `lobby-service.env`
  - `RELAY_PUBLIC_HOST` 必须是玩家能访问到的公网 IP 或域名
  - `SERVER_ADMIN_STATE_FILE` 在容器内固定写 `/app/data/server-admin.json`
  - 公共栈里的 `lobby-service` 默认使用宿主机网络，避免大段 UDP 端口发布把 Docker 网络层拖挂
  - 因为使用宿主机网络，双服务同机部署时，`SERVER_REGISTRY_BASE_URL` 应写成 `http://127.0.0.1:18787`
- `server-registry.env`
  - `DATABASE_URL` 必须指向容器内 PostgreSQL：`postgres://...@postgres:5432/...`
  - `PUBLIC_BASE_URL` 要写母面板的公网地址

## 六、启动与验证

准备好 env 文件后，启动：

```bash
docker compose \
  -p sts2-public-stack \
  -f /opt/sts2-server-stack-docker/deploy/docker-compose.public-stack.yml \
  up -d
```

验证：

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:18787/health
docker compose -p sts2-public-stack -f /opt/sts2-server-stack-docker/deploy/docker-compose.public-stack.yml ps
```

建议再额外验证：

```bash
curl http://127.0.0.1:8787/announcements
curl http://127.0.0.1:18787/servers/
```

### 对外页面如何打开

如果你是公网部署，并且已经放行对应端口，那么外部浏览器通常这样访问：

- 子服务管理面板：`http://<你的公网 IP 或域名>:8787/server-admin`
- 子服务健康检查：`http://<你的公网 IP 或域名>:8787/health`
- 母面板健康检查：`http://<你的公网 IP 或域名>:18787/health`
- 公共服务器列表接口：`http://<你的公网 IP 或域名>:18787/servers/`

说明：

- `lobby-service` 没有单独给玩家使用的网页大厅；玩家联机还是通过游戏客户端
- 服主日常主要打开的是 `8787/server-admin`
- 如果 `server-admin` 页面能打开但不能登录，检查 `deploy/lobby-service.env` 里的 `SERVER_ADMIN_PASSWORD_HASH` 和 `SERVER_ADMIN_SESSION_SECRET`
- 如果你走反向代理或域名，只需要把 `<你的公网 IP 或域名>` 替换成实际域名，并保证代理把对应路径转发到正确端口

## 七、日志与维护

统一使用：

```bash
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker status
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker logs lobby-service --follow
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker logs server-registry --tail 300
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker backup
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker rebuild
```

可用维护动作：

- `status`
- `start`
- `stop`
- `down`
- `restart [service]`
- `rebuild`
- `logs [service] [--follow] [--tail N]`
- `backup`
- `prune-images`

其中：

- `logs` 用来看容器日志
- `backup` 会打包 env 文件和 `deploy/data`
- `rebuild` 用于源码更新后重建镜像并拉起
- `prune-images` 用于清掉无用镜像层

## 八、升级流程

推荐流程：

1. 先备份
2. 同步新包或新源码
3. 覆盖安装目录
4. 重建镜像并重启容器
5. 做健康检查

示例：

```bash
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker backup
sudo ./scripts/install-server-stack-docker-linux.sh --install-dir /opt/sts2-server-stack-docker --skip-up
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker rebuild
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:18787/health
```

## 九、从旧 systemd 迁移到 Docker

推荐迁移顺序：

1. 备份旧目录
2. 导出旧 PostgreSQL 数据
3. 停掉旧 systemd 服务
4. 清理旧运行态
5. 部署 Docker 栈
6. 导入数据库
7. 验证两个健康接口

最少备份内容：

- `/opt/sts2-lobby`
- `/opt/sts2-server-registry`
- 旧 `.env`
- `lobby-service` 的 `data/server-admin.json`
- `server-registry` 对应 PostgreSQL 数据库导出

如果旧母面板使用的是宿主机 PostgreSQL，推荐先：

```bash
pg_dump <old_database_name> > registry-backup.sql
```

再把备份导入容器内 PostgreSQL。

## 十、常见问题

### 1. 容器起来了，但 lobby 客户端连不上 relay

优先检查：

- `39000-39149/UDP` 是否已放行
- `RELAY_PUBLIC_HOST` 是否写成了真实公网地址
- 宿主机防火墙 / 云厂商安全组是否同时放行

### 2. 容器日志一直增长怎么办

当前 compose 已经开启日志轮转：

- 单个容器最多保留 5 个日志文件
- 每个文件最大 10MB

一般不需要额外清理；如果镜像层积累过多，可执行：

```bash
./scripts/maintain-server-stack-docker.sh --install-dir /opt/sts2-server-stack-docker prune-images
```

### 3. 想恢复旧版怎么办

直接使用备份目录还原：

- `deploy/*.env`
- `deploy/data/`
- 旧 systemd 安装目录
- 旧 PostgreSQL 备份

再执行旧的 systemd 安装脚本即可回滚。

### 4. 一启动 `lobby-service`，`8787` 空响应、`18787` 超时，SSH 也开始异常

这是已经在线上复现过的问题，不是理论风险。

典型症状：

- `docker start sts2-lobby-service` 后，`http://127.0.0.1:8787/health` 连得上但返回空响应
- 原本健康的 `server-registry` 也会一起变慢，`18787` 超时
- 宿主机 `22` 端口还能建立 TCP，但 SSH banner 不返回，表现为 `Connection timed out during banner exchange`

排查结论：

- 旧 `systemd` 服务不是根因
- `server-registry` 容器本身通常是健康的
- 真正的问题在于某些云主机上，`lobby-service` 通过 Docker bridge 发布一整段 relay UDP 端口时，会把宿主机网络层一起拖坏

当前仓库里的公共栈已经按这个结论修正：

- `lobby-service` 使用 `network_mode: host`
- `SERVER_REGISTRY_BASE_URL` 改为 `http://127.0.0.1:18787`
- relay 端口默认范围调整为 `39000-39149`

如果你在线上遇到这类现象，不要继续反复 `restart` 或盲目重启容器；优先确认：

1. `docker-compose.public-stack.yml` 中 `lobby-service` 是否仍是宿主机网络
2. `deploy/lobby-service.env` 中 `SERVER_REGISTRY_BASE_URL` 是否写成了 `http://127.0.0.1:18787`
3. `RELAY_PORT_START` / `RELAY_PORT_END` 是否和实际放行范围一致
