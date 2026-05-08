# STS2 Peer Sidecar 部署指南

> 此 sidecar 让旧版 lobby-service（v0.2.x）也能加入 v0.3 的去中心化 peer 发现网络，
> 无需立即升级 lobby 主进程。新搭建的 lobby v0.3 不需要 sidecar（peer 协议已内置）。

## 1. 适用场景

- 你正在运行 lobby-service v0.2.x，暂时不想停机升级到 v0.3
- 你希望让该 lobby 出现在客户端 v0.3 的服务器选择列表中

## 2. 系统要求

- Linux x86_64 / aarch64
- Node.js 20.11+ 已安装在 `/usr/bin/node`（或调整 systemd unit 中的路径）
- systemd
- 一个公网可达的端口（默认 18800），与 lobby-service 共享同一台主机或独立部署均可

## 3. 一次性安装

```bash
# 在打包机器上生成 tarball
./scripts/package-sts2-peer-sidecar.sh
# 产物: releases/sts2_peer_sidecar/sts2-peer-sidecar.tar.gz

# 上传到目标服务器后，以 root 执行：
sudo bash sts2-peer-sidecar/deploy/install.sh sts2-peer-sidecar.tar.gz
```

安装脚本会：

- 创建系统用户 `sts2sidecar`（无 home、无登录 shell）
- 解压 tarball 到 `/opt/sts2-peer-sidecar`
- 写入默认配置 `/etc/sts2-peer-sidecar/sidecar.env`（首次安装时）
- 创建持久状态目录 `/var/lib/sts2-peer-sidecar`
- 注册 systemd unit `sts2-peer-sidecar.service`

## 4. 配置环境变量

编辑 `/etc/sts2-peer-sidecar/sidecar.env`：

```env
LOBBY_PUBLIC_BASE_URL=https://your-lobby.example.com
PEER_LISTEN_PORT=18800
PEER_CF_DISCOVERY_BASE_URL=https://your-cf-domain.example.com
PEER_STATE_DIR=/var/lib/sts2-peer-sidecar
```

字段说明：

- `LOBBY_PUBLIC_BASE_URL`（必填）：你 lobby-service 对外暴露的 URL；sidecar 会以这个地址在 peer 网络中代表它
- `PEER_LISTEN_PORT`（默认 18800）：sidecar 自身监听端口
- `PEER_CF_DISCOVERY_BASE_URL`（推荐）：CF Worker 发现服务的公网域名，用于启动时拉取种子列表
- `PEER_STATE_DIR`（默认 `/var/lib/sts2-peer-sidecar`）：节点身份与 known-peers 状态目录

## 5. 启动与开机自启

```bash
sudo systemctl enable --now sts2-peer-sidecar
sudo systemctl status sts2-peer-sidecar
journalctl -u sts2-peer-sidecar -f
```

成功标志：日志出现

```
[sidecar] listening on 18800; representing https://your-lobby.example.com
```

## 6. 验证可达性

```bash
# 健康端点（带 ed25519 签名）
curl "http://127.0.0.1:18800/peers/health?challenge=hi"

# 已知 peer 列表（启动时从 CF Worker seeds 拉取后入库）
curl "http://127.0.0.1:18800/peers"
```

## 7. 升级到 lobby-service v0.3

升级到 v0.3 后，lobby 主进程已内置 peer 协议，sidecar 可下线：

```bash
sudo systemctl disable --now sts2-peer-sidecar
sudo rm -rf /opt/sts2-peer-sidecar /etc/sts2-peer-sidecar /var/lib/sts2-peer-sidecar
sudo userdel sts2sidecar
```

升级 lobby-service 时记得同步设置 `PEER_SELF_ADDRESS`、`PEER_CF_DISCOVERY_BASE_URL`，
具体见 [STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md](./STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md) v0.3 升级章节。

## 8. 故障排查

| 现象 | 排查 |
|------|------|
| `systemctl status` 显示反复重启 | `journalctl -u sts2-peer-sidecar -n 100`；检查 env 文件中 `LOBBY_PUBLIC_BASE_URL` 是否正确 |
| `/peers/health` 返回 400 | 缺 `?challenge=...` query 参数。这是健康检查端点而非 ping |
| `/peers` 列表为空 | sidecar 启动时未拉到 CF seeds（CF 不可达或 `PEER_CF_DISCOVERY_BASE_URL` 错误）。检查日志 |
| 防火墙问题 | 确保 `LOBBY_PUBLIC_BASE_URL` 指向的端口对外开放；其它 peer 的 prober 需要从公网访问 sidecar `/peers/health` |
