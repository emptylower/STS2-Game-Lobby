# STS2 Server Registry

`server-registry/` 是独立部署的母面板服务，负责：

- 子服务器申请公开展示
- 审核与令牌签发
- 3 分钟心跳接收
- 轻探针与低频带宽探针
- 公共服务器列表 API
- 管理后台

## Docker 部署

当前更推荐和 `lobby-service` 一起使用仓库根目录的双服务 Docker 栈：

```bash
sudo ./scripts/install-server-stack-docker-linux.sh --install-dir /opt/sts2-server-stack-docker
```

如果只想单独容器化当前 `server-registry`，可以在本目录执行：

```bash
cp deploy/postgres.docker.env.example deploy/postgres.docker.env
cp deploy/server-registry.docker.env.example deploy/server-registry.docker.env
$EDITOR deploy/postgres.docker.env
$EDITOR deploy/server-registry.docker.env

docker compose -f deploy/docker-compose.server-registry.yml build
docker compose -f deploy/docker-compose.server-registry.yml up -d
```

当前单服务 compose 会同时拉起：

- `postgres:16-alpine`
- `server-registry`

如果部署机器拉 Docker Hub 很慢，可以先复制 `deploy/.env.example` 为 `deploy/.env`，再把：

- `STS2_NODE_IMAGE`
- `STS2_POSTGRES_IMAGE`

切到国内镜像地址。

日志轮转默认启用：

- `10MB` 单文件上限
- `5` 个历史文件

## systemd / 源码部署

## 开发

```bash
cd server-registry
npm install
npm run build
npm start
```

默认会连接 `DATABASE_URL` 指定的 PostgreSQL，并在启动时自动创建所需表结构。

## 打包分发

```bash
./scripts/package-server-registry.sh
```

产物：

- `server-registry/release/sts2_server_registry/`
- `server-registry/release/sts2_server_registry.zip`

打包结果现在也会包含：

- `Dockerfile`
- `.dockerignore`
- `deploy/docker-compose.server-registry.yml`
- `deploy/server-registry.docker.env.example`
- `deploy/postgres.docker.env.example`

## 主要接口

- `GET /servers/`
- `POST /api/submissions`
- `POST /api/submissions/:id/claim`
- `POST /api/servers/heartbeat`
- `GET /admin`

## 环境变量

示例见 [`.env.example`](./.env.example)。

Docker 路线下，建议把 `DATABASE_URL` 写成：

```text
postgres://postgres:<password>@postgres:5432/sts2_server_registry
```
