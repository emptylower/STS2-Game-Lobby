# STS2 LAN Connect v0.5.0 发布说明

发布日期：2026-07-17

v0.5.0 同时发布客户端 MOD 与 lobby-service。两者都必须升级到 `0.5.0`，才能使用服务器频道和完整房间富聊天；只升级其中一端会自动落入旧文本兼容路径或无法取得服务器频道 ticket。

## 玩家可见变化

- 大厅右侧新增 `频道聊天`，连接当前大厅节点后即可聊天，无需先加入房间。
- 房间聊天支持 Emoji、卡牌 / 遗物 / 药水引用，以及安全降级的 power / player 战斗引用。
- 大厅频道改为与大厅一致的浅色侧栏；房间内继续使用深色浮层。
- 修复大厅与房间输入框越界、房间输入框缩成窄条、预算小字、消息深色块、空白 Emoji 面板和异步按钮日志错误。
- 房间内仍可手动查看大厅频道，但大厅频道新消息不再触发房间角标、唤醒淡出面板或自动切页。
- 同一个 v0.5.0 客户端包兼容游戏 `0.107.1`、`0.108.0` 与 `0.109.0`；无需按游戏版本下载不同客户端。
- Android 输入或删除富聊天草稿时不再反复重建输入控件，避免系统键盘重启和闪烁。
- 宝箱跳过投票会按运行时版本选择原生 nullable 协议或 legacy `-1` 协议，单项补丁变化不再阻断整组 Treasure 补丁。
- 旧客户端仍可通过 legacy 文本参与兼容房间，但不会显示 v0.5.0 富内容。

## 服务端变化

- 新增 server chat ticket、WebSocket upgrade 路由、消息限流、有界历史与慢客户端保护。
- 房间聊天增加 generation authority、能力协商、去重和旧协议投影。
- `/server-admin` 可管理六个开关：服务器频道、rich、Emoji、item refs、room-v2、combat refs。
- 开关写入既有 `SERVER_ADMIN_STATE_FILE`；消息、历史和指标不会持久化。
- 服务器频道历史只属于当前节点进程，重启清空，也不会在 peer 节点间复制。

## 升级步骤

1. 停止游戏和旧 lobby-service。
2. 客户端使用 `sts2_lan_connect-release.zip` 完整覆盖安装，确认 `sts2_lan_connect.json` 为 `0.5.0`。
3. 服务端使用 `sts2_lobby_service.zip` 完整替换源码并重新执行 `npm ci && npm run build`，或重新构建 Docker 镜像。
4. 对照新包中的 env 示例补齐 `SERVER_CHAT_*`、`ROOM_CHAT_*` 配置。
5. 如需大厅频道，在 env 设置 `SERVER_CHAT_ENABLED=true`，或登录 `/server-admin` 启用并保存。
6. 重启服务后检查 `/health`、`/probe`、`/server-admin`、`/rooms`、`/peers/health`，再用 v0.5.0 客户端测试频道和房间消息。

## 回滚顺序

先关闭 combat refs，再关闭 Emoji/item refs 与 rich；只有需要回退房间富协议时才关闭 room-v2。服务器频道独立控制。回退二进制时客户端与 lobby-service 应一起回退到同一发布批次。

## 发布资产

- `sts2_lan_connect-release.zip`：Windows / macOS 客户端 MOD 与安装脚本。
- `sts2_lobby_service.zip`：Linux systemd / Docker 服务端源码与安装材料。

完整改动记录见 [`../CHANGELOG.md`](../CHANGELOG.md)。
