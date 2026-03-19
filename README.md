# STS2 LAN Connect

`STS2 LAN Connect` 是一个《Slay the Spire 2》联机大厅方案，包含：

- `sts2-lan-connect/`
  游戏内客户端 MOD，负责大厅 UI、建房/加房流程、续局绑定、调试报告和与官方联机流程的桥接。
- `lobby-service/`
  `Node.js / TypeScript` 大厅服务，负责房间目录、中心服务器注册表、后台审核、加入票据、房主心跳、控制通道与 relay fallback。

当前公开客户端版本：`0.2.1`

## 主要特性

- 大厅内支持关键词搜索、分页和可叠加筛选
- 筛选支持 `公开`、`上锁`、`可加入`
- 房间列表显示真实游戏版本、真实 MOD 版本、房间状态和 `relay` 状态
- 大厅延迟显示改为独立探测，不再受房间列表体量影响
- 加入失败会细分为版本不一致、MOD 不一致、房间已开局、房间已满等原因
- 多人续局存档会绑定大厅房间，房主重新进入续局时自动重新发布
- 大厅内可一键复制本地调试报告，方便和服务端日志对照
- 大厅侧栏新增“当前线路”，支持查看官方/社区服务器目录、切服和提交社区服务器
- Windows / macOS 客户端支持一键安装 / 卸载
- Linux 服务端支持一键部署

## 目录结构

- `docs/`
  项目文档、玩家安装说明、使用说明、部署说明
- `research/`
  研究资料与重建笔记
- `scripts/`
  构建、打包、安装、部署、公开仓库同步脚本
- `sts2-lan-connect/`
  客户端 MOD 源码
- `lobby-service/`
  大厅服务源码

说明：

- 本地构建产物仍写入各模块自己的 `release/` 目录
- 通过 `scripts/sync-release-repo.sh` 同步到公开仓库后，会额外生成统一的 `releases/` 目录

## 快速开始

### 1. 构建客户端

```bash
./scripts/build-sts2-lan-connect.sh
```

如果需要构建后直接安装到本机游戏：

```bash
./scripts/build-sts2-lan-connect.sh --install
```

### 2. 打包客户端

```bash
./scripts/package-sts2-lan-connect.sh
```

产物位于：

- `sts2-lan-connect/release/sts2_lan_connect/`
- `sts2-lan-connect/release/sts2_lan_connect-release.zip`

### 3. 打包服务端

```bash
./scripts/package-lobby-service.sh
```

产物位于：

- `lobby-service/release/sts2_lobby_service/`
- `lobby-service/release/sts2_lobby_service.zip`

### 4. 部署服务端

```bash
sudo ./scripts/install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

默认需要放行：

- `8787/TCP`
- `39000-39511/UDP`

### 5. 同步公开仓库

如果你本地已经 clone 了公开仓库 `STS-Game-Lobby`：

```bash
./scripts/sync-release-repo.sh --repo-dir ~/Desktop/STS-Game-Lobby
```

同步内容包括：

- 根 README、许可证、`.gitignore`
- `docs/`、`research/`、`scripts/`
- `sts2-lan-connect/`、`lobby-service/` 源码
- `releases/` 下的客户端和服务端发布产物

## 环境变量

客户端打包支持这些环境变量：

- `STS2_LOBBY_DEFAULT_BASE_URL`
- `STS2_LOBBY_DEFAULT_WS_URL`
- `STS2_LOBBY_REGISTRY_BASE_URL`
- `STS2_LOBBY_COMPATIBILITY_PROFILE`
- `STS2_LOBBY_CONNECTION_STRATEGY`

如果只设置了 `STS2_LOBBY_DEFAULT_BASE_URL`，打包脚本会自动推导 WS 地址。`STS2_LOBBY_REGISTRY_BASE_URL` 留空时，会默认跟随大厅默认地址。

## 文档

- [客户端发布包安装说明](./docs/CLIENT_RELEASE_README_ZH.md)
- [客户端使用说明](./docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md)
- [双端部署指南](./docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md)
- [服务端说明](./lobby-service/README.md)
- [研究资料索引](./research/README.md)

## 版权与说明

- 本仓库源码以 `GPL-3.0-only` 发布，详见 `LICENSE`
- 本项目仅用于学习、研究和 MOD 开发测试
- 《Slay the Spire 2》及相关版权归 Mega Crit 所有
- 本项目与 Mega Crit 无官方关联
