# STS2 Server Registry

> ⚠️ **v0.4.0 起本服务为可选组件**。新部署的 `lobby-service` 不再向任何"母面板"上报；节点列表聚合由 Cloudflare discovery worker（`https://sts2-gamelobby-register.xyz`）完成。  
> 仅在以下情况下你才需要看这份文档：
> - 你想完全自托管节点列表服务（替代 CF discovery worker）
> - 你正在维护一个 v0.3.x 时代的双服务部署，并希望继续运行它  
>
> 想跑 v0.4.0 标准部署的运维请回到 [`../docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md`](../docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md)。

`server-registry/` 是独立部署的母面板服务，负责：

- 子服务器申请公开展示
- 审核与令牌签发
- 3 分钟心跳接收
- 轻探针与低频带宽探针
- 公共服务器列表 API
- 管理后台

注意：v0.4.0 的 `lobby-service` 内部已经移除全部 `SERVER_REGISTRY_*` 相关代码，**不会向 `server-registry` 上报**。同机部署只是把两个独立服务跑在一起，并不会自动联动。

## Docker 部署

如果你确实想跑 v0.3.x 的双服务公共栈（lobby + registry + postgres），仓库根目录提供安装脚本：

```bash
sudo ./scripts/install-server-stack-docker-linux.sh --install-dir /opt/sts2-server-stack-docker
```

详细的双服务栈运维说明见 [`../docs/STS2_SERVER_DOCKER_OPERATION_GUIDE_ZH.md`](../docs/STS2_SERVER_DOCKER_OPERATION_GUIDE_ZH.md) 第二节（"可选：自建公开列表 stack"）。

只想单独容器化 `server-registry` 本身：

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

## 源码运行

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

打包结果包含：

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

## 和 v0.4.0 lobby-service 的关系

| 问题 | 答案 |
|------|------|
| v0.4.0 lobby-service 会主动向 `server-registry` 上报吗？ | 否。v0.4.0 已经移除全部 `SERVER_REGISTRY_*` 上报逻辑。 |
| 我把它们部署在一起，会自动联动吗？ | 不会，它们是两个独立服务。 |
| v0.4.0 客户端会读 `server-registry` 的 `/servers/` 接口吗？ | 否，只会读 CF discovery worker。 |
| 我可以自己写客户端 / 工具去消费 `/servers/`API 吗？ | 可以，它仍是一个完整的 REST API。 |
| 这个目录还会继续维护吗？ | 作为可选自托管参考实现保留；新功能优先加在 `lobby-service` 的 peer 网络代码里。 |
