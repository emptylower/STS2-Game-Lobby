# STS2 公共大厅 — 升级到 v0.3.2 运维手册

> 这份文档面向**社区大厅运维**（也就是公共服务器列表里那几台 lobby
> 的服主）。读完跟着做完，你的服务器就会出现在新版客户端 picker 里、
> 显示你设置的服务器名而不是 IP，并且和老版本玩家继续兼容。
>
> 时间预算：**首次升级 ≈ 15 分钟，后续滚动 ≈ 2 分钟**。
> 全程**不需要新开端口**，仍然只用 `8787/TCP`。

---

## 0. 一句话介绍 v0.3 / v0.3.1 / v0.3.2 是什么

我们把 server-registry 这个"中心目录"换成了**去中心化 peer 网络**。
新协议：
- 每台 lobby 自己签 ed25519 身份，互相 gossip 谁在线
- 一个跑在 Cloudflare Workers 上的轻量级聚合器（`sts2-discovery`）每 10 分钟从
  网格里抓一次活跃节点列表，客户端启动时拉这份列表
- v0.3.1 在此基础上把"服务器名"这条数据流也打通了：你在
  `/server-admin` 面板里设的 `displayName` 60 秒内会传到玩家端 picker，
  替代原来的"sts2-lobby-service (1.2.3.4)"
- **v0.3.2 修复了"新部署的 lobby 永远不会自动加入公网格"这个 federation
  死锁**：bootstrap 完成后会主动向每个 probed peer POST `/peers/announce`，
  把自己塞进对方的 PeerStore，下一次 CF cron 周期内就会出现在公共列表里，
  完全无需人工干预。

**协议没变，v0.3.0/0.3.1 跑得动的 v0.3.2 也跑得动。** 升级是无缝增量。

---

## 1. 升级前的兼容性须知（**必读**）

v0.3 把两个开关默认改成了 `true`：

```
ENFORCE_LOBBY_ACCESS_TOKEN=true
ENFORCE_CREATE_ROOM_TOKEN=true
```

但 **v0.2.x 客户端发布包里的 `lobby-defaults.json` 不带这些 token**。
所以如果你直接升级、env 不动，所有 v0.2.x 玩家会立刻挂掉：

| 客户端动作 | 结果 |
|---|---|
| 拉房间列表 `GET /rooms` | **403** `room_list_disabled` |
| 创建房间 `POST /rooms` | **503** `create_room_token_not_configured` |

**只要还有 v0.2.x 玩家会连你，升级时就一定要在 `lobby-service.env`
里加这三行**：

```dotenv
# 兼容仍在用 v0.2.x 客户端的玩家
ENFORCE_LOBBY_ACCESS_TOKEN=false
ENFORCE_CREATE_ROOM_TOKEN=false
PUBLIC_ROOM_LIST_ENABLED=true
```

> 等所有玩家都升级到 v0.3+ 自带令牌后，再把这三行删掉、并设
> `LOBBY_ACCESS_TOKEN=...` / `CREATE_ROOM_TOKEN=...` 才是最终安全态。
> 当前阶段先确保不掉人。

---

## 2. 升级步骤（Docker Compose 部署，**主流路径**）

如果你按官方一键脚本部署的，目录大概是 `/opt/sts2-server-stack-docker/`。
路径不同的请自行替换。

### 2.1 备份当前部署（**强烈建议**）

```bash
TS=$(date +%Y%m%d-%H%M%S)
sudo mkdir -p /tmp/sts2-lobby-backup-$TS
sudo cp -a /opt/sts2-server-stack-docker/lobby-service \
          /opt/sts2-server-stack-docker/deploy/lobby-service.env \
          /opt/sts2-server-stack-docker/deploy/data/lobby-service \
          /tmp/sts2-lobby-backup-$TS/
echo "backup at /tmp/sts2-lobby-backup-$TS"
```

### 2.2 拉新源码（任选其一）

**方式 A：从 git 拉（推荐）**

```bash
cd /opt/sts2-server-stack-docker
sudo git pull
```

**方式 B：用 GitHub Release 的 tarball**

在
[Releases v0.3.2](https://github.com/emptylower/STS2-Game-Lobby/releases/tag/v0.3.2)
下载 `sts2_lobby_service.zip`，解压：

```bash
cd /tmp
wget https://github.com/emptylower/STS2-Game-Lobby/releases/download/v0.3.2/sts2_lobby_service.zip
sudo unzip -o sts2_lobby_service.zip -d /tmp/sts2-lobby-v0.3.2
sudo rsync -a --delete \
  --exclude=node_modules --exclude=dist --exclude=data \
  /tmp/sts2-lobby-v0.3.2/sts2_lobby_service/lobby-service/ \
  /opt/sts2-server-stack-docker/lobby-service/
```

### 2.3 配置 env

编辑 `/opt/sts2-server-stack-docker/deploy/lobby-service.env`：

```bash
sudo nano /opt/sts2-server-stack-docker/deploy/lobby-service.env
```

确保至少有这些字段（**复制贴上时把 `<...>` 换成你的实际值**）：

```dotenv
# ── peer 网络（v0.3） ──
PEER_SELF_ADDRESS=http://<你的公网IP或域名>:8787
PEER_CF_DISCOVERY_BASE_URL=https://sts2-gamelobby-register.xyz
PEER_STATE_DIR=/app/data/peer

# ── 服务器名（v0.3.1+，二选一）──
# 路径 A：通过环境变量强制设置
PEER_DISPLAY_NAME=<你想要的服务器名，例如：上海社区服>
# 路径 B：留空 PEER_DISPLAY_NAME，等容器起来后在 admin 面板里改

# ── v0.2 客户端兼容（参见第 1 章）──
ENFORCE_LOBBY_ACCESS_TOKEN=false
ENFORCE_CREATE_ROOM_TOKEN=false
PUBLIC_ROOM_LIST_ENABLED=true
```

### 2.4 重建镜像 + 重启容器

**注意**：`docker restart` 不会重读 env 文件，必须用 `up -d --force-recreate`。

```bash
cd /opt/sts2-server-stack-docker/deploy
sudo docker compose -f docker-compose.public-stack.yml build lobby-service
sudo docker compose -f docker-compose.public-stack.yml \
  up -d --no-deps --force-recreate lobby-service
```

### 2.5 验证

容器启动后看日志，应该有这几行：

```bash
sudo docker logs sts2-lobby-service 2>&1 | grep -E '\[peer\]|\[lobby\]'
```

期望输出：

```
[peer] mounted; self=http://你的IP:8787 displayName="你设置的名字" cf=https://sts2-gamelobby-register.xyz
[peer] announced self to N bootstrapped peer(s)
[lobby] listening on http://0.0.0.0:8787 (ws path /control)
```

`[peer] announced self to N ...` 这一行是 **v0.3.2 新增**——表示自动加入
公网格成功，N 是 bootstrap 阶段 probe 通过的对端数量（通常是 2，即默认大厅
+ 微雨的香港大厅）。如果是 0，说明所有 seed 都 probe 失败，请检查
`PEER_CF_DISCOVERY_BASE_URL` 是否指向 `https://sts2-gamelobby-register.xyz`
（**不是** workers.dev 的旧 URL）。

外部 curl 验证：

```bash
# /peers/health 应返回带 displayName 的 JSON
curl -sS "http://<你的IP>:8787/peers/health?challenge=test" | python3 -m json.tool

# /peers 应至少包含你自己（source: self）
curl -sS "http://<你的IP>:8787/peers" | python3 -m json.tool

# /rooms 应返回 200 而不是 403（兼容 v0.2 客户端）
curl -sSI "http://<你的IP>:8787/rooms" | head -1
```

---

## 3. 更友好地改服务器名（不用动 env）

如果你不想每次改名都改 env + 重启容器，推荐 **走 admin 面板**：

1. 浏览器打开 `http://<你的IP>:8787/server-admin`
2. 用 `lobby-service.env` 里 `SERVER_ADMIN_USERNAME` / `SERVER_ADMIN_PASSWORD_HASH` 对应的账号登录
3. 在"服务器信息"里把 `displayName` 改成你想要的，保存
4. 60 秒内 `/peers/health` 和 `/peers` 自动反映新名，不用重启
5. 客户端 picker 下次刷新就能看到新名

> `PEER_DISPLAY_NAME` env 优先级 > admin 面板的 `displayName` > 自动回退
> `社区服务器 <host>`。三选一，最常见用法是清空 `PEER_DISPLAY_NAME` 让
> admin 面板说了算。

---

## 4. 让 CF 网格"认识"你的服务器

### 4.1 v0.3.2+：自动加入，零操作

v0.3.2 在 bootstrap 之后会主动 POST `/peers/announce` 到所有 probed
peer，所以容器起来后**最长 10 分钟内自动出现在公共列表**。日志里会有：

```
[peer] announced self to N bootstrapped peer(s)
```

只需要等 ≤10 分钟（CF Worker cron 周期）后查：

```bash
curl -sS https://sts2-gamelobby-register.xyz/v1/servers | python3 -m json.tool
```

输出里出现你的 `address` 和你设置的 `displayName`，就 ✅，不需要任何
手工动作。

### 4.2 v0.3.1 及更老：必须手动 announce 一次

v0.3.1 和更老版本**没有**自动 announce 逻辑——bootstrap 只能拉别人，
push（heartbeat）只能 refresh **已经认识**的对端。所以 v0.3.1 部署完后，
默认大厅、CF 聚合器都**不知道你存在**，自己客户端能看到别人，但别人
看不到你。

要让自己出现在公网格，从你的服务器手动 POST 一次到任意一台已活跃的
公开大厅（推荐对默认大厅 + 微雨的香港大厅各发一次，多一个备份不强求）：

```bash
# Step 1: 拿自己的 publicKey（任选一种）
# 方法 A：直接读 identity 文件（docker 默认部署路径）
sudo cat /opt/sts2-server-stack-docker/deploy/data/lobby-service/peer/identity.json | jq -r .publicKey

# 方法 B：从自己的 /peers self-entry 取
curl -s http://127.0.0.1:8787/peers | jq -r '.peers[] | select(.source=="self") | .publicKey'

# Step 2: 替换三个字段后发出去
curl -X POST http://47.111.146.69:8787/peers/announce \
  -H 'content-type: application/json' \
  -d '{
    "address": "<你的 PEER_SELF_ADDRESS，例如 https://your-lobby.example.com>",
    "publicKey": "<上一步拿到的 publicKey>",
    "displayName": "<想显示的服务器名>"
  }'
```

成功响应是 `HTTP 202 {"accepted":true}`。常见错误：

| 响应 | 意思 | 修复 |
|---|---|---|
| `400 address_and_publicKey_required` | body 字段写错 | 检查 JSON 格式 |
| `422 probe_failed` | 对方反向连不上你 / publicKey 不匹配 | 99% 是 `address` 写错或 lobby 不可公网访问 |
| `429 rate_limited` | 同一个公网 IP 一小时内已经 announce 过 5 次 | 等 1 小时再来；通常只在频繁重启时才会触发 |

发完之后等 ≤10 分钟看 `/v1/servers`。

> **强烈建议**：与其每次重启都手动 announce，直接升到 v0.3.2 一劳永逸。
> 升级路径就是本文档剩下的步骤——只要把 release zip 换成 v0.3.2 那个
> 即可，不用动 env、不用动协议。

---

## 5. 如果出问题怎么回滚

### 容器起不来 / 报错

```bash
# 看完整日志
sudo docker logs sts2-lobby-service 2>&1 | tail -80
# 回到上一份 env
sudo cp /tmp/sts2-lobby-backup-$TS/lobby-service.env \
        /opt/sts2-server-stack-docker/deploy/lobby-service.env
# 重启
cd /opt/sts2-server-stack-docker/deploy && \
  sudo docker compose -f docker-compose.public-stack.yml \
    up -d --no-deps --force-recreate lobby-service
```

### 完全回滚到 v0.2.x

```bash
# 恢复源码
sudo rsync -a --delete \
  /tmp/sts2-lobby-backup-$TS/lobby-service/ \
  /opt/sts2-server-stack-docker/lobby-service/
# 恢复 env
sudo cp /tmp/sts2-lobby-backup-$TS/lobby-service.env \
        /opt/sts2-server-stack-docker/deploy/lobby-service.env
# 恢复持久数据（房间状态等）
sudo rsync -a --delete \
  /tmp/sts2-lobby-backup-$TS/lobby-service/ \
  /opt/sts2-server-stack-docker/deploy/data/lobby-service/
# 重建并重启
cd /opt/sts2-server-stack-docker/deploy && \
  sudo docker compose -f docker-compose.public-stack.yml \
    build lobby-service && \
  sudo docker compose -f docker-compose.public-stack.yml \
    up -d --no-deps --force-recreate lobby-service
```

### 不想升级整个 lobby、只想加入 peer 网络

可以装 [peer sidecar](./STS2_PEER_SIDECAR_GUIDE_ZH.md)（v0.1.0）作为过渡：
它是个独立的小服务，代表你的 v0.2 大厅向 peer 网格注册一个 ed25519
身份，不动你的 lobby-service 主进程。下一阶段你可以无压力升 v0.3。

---

## 6. 升级清单（Checklist）

- [ ] 备份 lobby-service 源码 + env + data 目录
- [ ] 拉 v0.3.2 源码（git pull 或下 release zip）
- [ ] 在 env 里加 `PEER_SELF_ADDRESS`、`PEER_CF_DISCOVERY_BASE_URL`、`PEER_STATE_DIR`
- [ ] 在 env 里加 `ENFORCE_LOBBY_ACCESS_TOKEN=false` 等三行兼容开关（除非你确定没有 v0.2.x 玩家）
- [ ] 设 `PEER_DISPLAY_NAME` 或登 admin 面板设 `displayName`
- [ ] `docker compose ... build lobby-service`
- [ ] `docker compose ... up -d --no-deps --force-recreate lobby-service`
- [ ] 日志里看到 `[peer] mounted ... displayName="<名字>"`
- [ ] 日志里看到 `[peer] announced self to N bootstrapped peer(s)`（v0.3.2 新增；N>0 才算自动加入网格成功）
- [ ] `curl /peers/health?challenge=...` 返回 200 + `displayName` 字段
- [ ] `curl /rooms` 返回 200（不是 403）
- [ ] 等 ≤10 分钟，`curl https://sts2-gamelobby-register.xyz/v1/servers` 里能看到你

---

## 联系

升级有问题、看不到自己出现在客户端 picker 列表里、或者想反馈 bug：

- GitHub Issues：<https://github.com/emptylower/STS2-Game-Lobby/issues>
- 当前公共聚合 CF 域名：`https://sts2-gamelobby-register.xyz`
- 默认大厅参考实现：`http://47.111.146.69:8787`（"默认大厅（华东）"）

谢谢你保持服务器在线，让大家能玩 STS2 联机 ✨
