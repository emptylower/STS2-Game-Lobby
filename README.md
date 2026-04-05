# STS2 LAN Connect

`STS2 LAN Connect` 是一个《Slay the Spire 2》联机大厅方案，包含：

- `sts2-lan-connect/`
  游戏内客户端 MOD，负责大厅 UI、建房/加房流程、续局绑定、调试报告和与官方联机流程的桥接。
- `lobby-service/`
  `Node.js / TypeScript` 大厅服务，负责房间目录、密码校验、加入票据、房主心跳、控制通道与 relay fallback。

当前公开客户端版本：`0.2.2`
当前公开子服务版本：`0.2.2`

说明：

- 公开仓库只保留客户端和子服务源码
- 官方公共服务器母面板为私有服务，不再随 GitHub 仓库公开
- 子服务仍可通过 `SERVER_REGISTRY_BASE_URL` 对接官方母面板

## 主要特性

- 大厅 UI 采用暖色羊皮纸像素风格，配色基于 oklch 色彩空间精确定义，与 TypeScript 参考 UI 保持一致
- 大厅布局采用 75%/25% 锚点比例分割（房间列表 : 侧栏），在任意分辨率下自动缩放
- 房间列表固定每页 5 个卡片槽位，等分容器高度，不足时用空占位符填充，无滚动条
- 所有按钮采用像素风按下动效：通过 ExpandMargin 实现按钮整体向阴影方向滑入，悬停/按下变为绿底白字（CREATE 除外）
- 筛选功能改为弹窗面板，包含房间类型和游戏模式两组切换按钮，右上角红色关闭按钮
- 切换服务器弹窗已放大至近全屏，服务器卡片信息字号增大
- 大厅顶部支持公告轮播，公告由当前子服 `/server-admin` 面板配置并下发，桌面端显示点状页码与左到右 6 秒进度条
- 房间内新增可拖动聊天面板，同房间玩家可直接收发文字消息，并带未读提醒
- 房间管理功能：房主可在暂停菜单中打开房间管理面板，支持开关聊天室、移出不当玩家；踢人通过服务端强制执行，被踢玩家无法重新加入同一房间
- 准备页面中远程玩家名旁显示踢出按钮，房主可在开局前直接移除玩家
- 踢人同时走 WebSocket 控制通道（服务端关闭连接并阻止重连）和 ENet 直连断开（局域网场景），确保双通道生效
- 大厅内支持关键词搜索、分页和可叠加筛选
- 筛选支持 `公开`、`上锁`、`可加入`、`标准模式`、`多人每日挑战`、`自定义模式`
- 大厅内支持从中心服务器拉取可用大厅列表，并可一键快速切换服务器
- 建房时可直接选择 `标准模式`、`多人每日挑战`、`自定义模式`
- 原生支持 5-8 人联机（建房时可选最大人数），包含难度缩放、营地座位、商店布局和宝箱分配的适配补丁；检测到 RMP 等外部扩展人数 MOD 时自动跳过以避免冲突
- 建房、续局回挂和本地 LAN Host 会自动对齐扩展人数补丁的 `maxPlayers`，服务端同步放宽房间人数元数据校验
- 房间列表显示真实游戏版本、真实 MOD 版本、房间状态和 `relay` 状态
- 大厅延迟显示改为独立探测，不再受房间列表体量影响
- 加入失败会细分为版本不一致、MOD 不一致、房间已开局、房间已满等原因
- 多人续局存档会绑定大厅房间，房主重新进入续局时自动重新发布
- 续局接管弹窗同时显示角色名和玩家名（如"铁甲战士（小明）"），方便多人选同角色时准确找回自己的存档槽位
- 大厅内可一键复制本地调试报告，方便和服务端日志对照
- Windows / macOS 客户端支持一键安装 / 卸载
- Linux 服务端支持 systemd 和单服务 Docker 部署
- 子服管理面板支持带宽限制、公开列表配置和大厅公告维护
- 子服管理面板已补齐响应式布局，移动端和桌面端窄窗口下不会再出现 header、登录状态和退出按钮互相挤压
- 子服管理面板自动轮询状态时不会再覆盖未保存的设置和公告草稿；手动重新加载配置前也会先提示确认
- 打开“公开列表申请”后，可自动向官方母面板发起申请并持续同步心跳

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

### 4. 安装服务端

```bash
sudo ./scripts/install-lobby-service-linux.sh --install-dir /opt/sts2-lobby
```

默认需要放行：

- `8787/TCP`
- `39000-39149/UDP`

如果你希望这台子服务进入官方公开列表，保持或填写：

- `SERVER_REGISTRY_BASE_URL=http://47.111.146.69:18787`

然后在 `/server-admin` 面板里打开“公开列表申请”。

注意这里还要再满足一条：

- 这台子服务必须把 `RELAY_PUBLIC_HOST` 或 `SERVER_REGISTRY_PUBLIC_*` 配成公网 IP / 域名

因为 `SERVER_REGISTRY_BASE_URL` 只表示“申请发给哪台母面板”，不表示母面板就能反向探测到你的子服务。当前逻辑会自动创建申请、自动 claim 审核结果，并持续同步心跳；但如果上报出去的是 `127.0.0.1`、`0.0.0.0`、`localhost` 这种本机地址，子面板会直接显示公网地址配置错误。

### 5. 同步公开仓库

如果你本地已经 clone 了公开仓库 `STS-Game-Lobby`：

```bash
./scripts/sync-release-repo.sh --repo-dir ~/Desktop/STS-Game-Lobby
```

同步内容包括：

- 根 README、许可证、`.gitignore`
- `docs/`、`research/`、`scripts/`
- `sts2-lan-connect/`、`lobby-service/` 源码
- `releases/` 下的客户端和子服务发布产物

## 环境变量

客户端打包支持这些环境变量：

- `STS2_LOBBY_DEFAULT_BASE_URL`
- `STS2_LOBBY_DEFAULT_WS_URL`
- `STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL`
- `STS2_LOBBY_COMPATIBILITY_PROFILE`
- `STS2_LOBBY_CONNECTION_STRATEGY`

如果只设置了 `STS2_LOBBY_DEFAULT_BASE_URL`，打包脚本会自动推导 WS 地址。

如果不额外覆盖，仓库内默认的 [`lobby-defaults.json`](./sts2-lan-connect/lobby-defaults.json) 会直接指向当前官方阿里云大厅 `47.111.146.69:8787`。

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
