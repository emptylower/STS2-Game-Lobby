# 管理员面板 Agent 交接事实清单

更新日期：2026-07-23

本文只记录当前代码、接口、测试和部署事实。

## 任务边界

- 仓库：`/Users/mac/Desktop/STS2-Game-Lobby`
- 当前分支：`main`
- 当前 HEAD：`6a7d5c7`（管理面板 + 自动更新 + Dashboard 重构已提交）
- 服务端目标版本：`0.5.2`
- 管理员面板属于 `lobby-service/`。
- 本轮没有修改客户端页面。
- `releases/` 是打包产物镜像目录，本轮没有直接修改。
- 生产部署交接见 `docs/admin-ui-production-upgrade-handoff.md`。

## 页面实现

- 页面生成入口：`lobby-service/src/server-admin-ui.ts:83` 的 `renderServerAdminPage(serviceVersion)`。
- 页面由一个 TypeScript 模板字符串生成完整 HTML；当前文件共 2570 行。
- 页面 CSS 内联在同一 HTML 中，从 `lobby-service/src/server-admin-ui.ts:94` 开始。
- 页面脚本使用 `React.createElement`，别名为 `h`，位置为 `lobby-service/src/server-admin-ui.ts:944`。
- 页面没有独立前端构建入口、JSX 文件或前端路由器。
- 当前页面为深色运维监控台风格（antd `darkAlgorithm`），主题常量为 `ADMIN_THEME`（`server-admin-ui.ts:986`），SVG 图标表为 `ICONS`（`server-admin-ui.ts:1002`），图标渲染辅助为 `svgIcon`（`server-admin-ui.ts:1015`）。
- 登录后主界面按标签导航切换四个功能区，标签定义 `navTabs` 位于 `server-admin-ui.ts:2038`：
  - `概览`（默认）：KPI 指标卡、节点状态告警、两张可视化面板（`server-admin-ui.ts:2422`）。
  - `公开设置`：设置表单（`server-admin-ui.ts:2173`）。
  - `大厅公告`：公告编辑器（`server-admin-ui.ts:2301`），标签上显示公告数量徽标。
  - `服务更新`：更新卡片与 Release 说明（`server-admin-ui.ts:2100`），有可用更新时标签显示琥珀色提示点，概览页同时显示可点击的更新预告横幅。
- 有未保存草稿时 `公开设置` 标签显示提示点；页面底部页脚（`server-admin-ui.ts:2541`）显示版本、部署模式、自动刷新说明与最近同步时间。
- 可视化组件（全部为无依赖 SVG/纯 CSS 实现）：
  - `usageRing` 容量利用率环形仪表（conic-gradient）：`server-admin-ui.ts:1312`
  - `utilizationMeter` 利用率渐变计量条：`server-admin-ui.ts:1338`
  - `relayDonut` relay Host/Client 环形占比图：`server-admin-ui.ts:1353`
  - `chatQualityMeter` 消息接受率计量条：`server-admin-ui.ts:1416`
  - `Sparkline` 带宽迷你趋势线（每次轮询采样，保留最近 60 点）：`server-admin-ui.ts:1442`
  - `factChip` 容量事实 chip：`server-admin-ui.ts:1407`
- CDN 依赖：
  - Ant Design reset CSS `antd@5.22.6`
  - dayjs `1.11.13`
  - React 18 / ReactDOM 18
  - Ant Design `5.22.6`
- Ant Design 解构组件位于 `lobby-service/src/server-admin-ui.ts:967`。
- `ToggleButton` 位于 `lobby-service/src/server-admin-ui.ts:1276`，使用 `aria-pressed`。
- `metricTile` 位于 `lobby-service/src/server-admin-ui.ts:1288`。
- `detailRow` 位于 `lobby-service/src/server-admin-ui.ts:1303`。
- 更新阶段文本映射位于 `lobby-service/src/server-admin-ui.ts:1473`。
- 根组件 `App` 位于 `lobby-service/src/server-admin-ui.ts:1494`。
- 移动端适配断点为 992px / 640px / 480px；`.page-content`、`.page-footer`、`.page-header` 需保留 `width: 100%` 与 `min-width: 0`，否则 antd Layout 的 flex 布局会让内容 min-content 撑破移动端视口。

## 页面请求封装

- `buildServerAdminRequestInit` 定义于 `lobby-service/src/server-admin-ui.ts:50`。
- unsafe same-origin 请求会带 `x-csrf-token`，实现位于 `lobby-service/src/server-admin-ui.ts:61`。
- `performAdminRequest` 位于 `lobby-service/src/server-admin-ui.ts:1039`。
- 所有页面请求使用 `credentials: same-origin`。
- 页面轮询与未保存草稿合并辅助函数位于 `lobby-service/src/server-admin-ui.ts:1`。
- 页面会话、设置、公告、loading 和 mutation 状态从 `lobby-service/src/server-admin-ui.ts:1495` 开始集中声明。

## 管理员 HTTP 接口

路由实现在 `lobby-service/src/app.ts`：

| 方法 | 路径 | 鉴权 | 行号 | 返回或动作 |
|---|---|---|---|---|
| GET | `/server-admin` | 无 | 740 | 返回完整 HTML |
| POST | `/server-admin/login` | 用户名、密码 | 744 | 写会话 cookie，返回会话和 CSRF token |
| POST | `/server-admin/logout` | 会话 + CSRF | 767 | 删除服务端会话并清 cookie |
| GET | `/server-admin/session` | 会话 | 779 | 返回 `id`、`username`、`expiresAt`、`csrfToken` |
| GET | `/server-admin/settings` | 会话 | 793 | 返回设置、运行指标和更新状态 |
| PATCH | `/server-admin/settings` | 会话 + CSRF | 802 | 持久化设置并同步聊天治理 |
| POST | `/server-admin/update/check` | 会话 + CSRF | 855 | 执行更新检查并返回更新状态 |
| POST | `/server-admin/update/install` | 会话 + CSRF | 863 | 异步启动安装，返回 HTTP 202 |
| POST | `/server-admin/chat/clear-history` | 会话 + CSRF | 876 | 清空聊天历史并返回 metrics |

- `/server-admin` 相关响应带 `Cache-Control: no-store`，位置为 `lobby-service/src/app.ts:308`。
- 页面 HTML 由 `renderServerAdminPage(lobbyServiceVersion)` 返回，位置为 `lobby-service/src/app.ts:741`。

## 鉴权与 CSRF

- 实现文件：`lobby-service/src/server-admin-auth.ts`。
- 密码格式是 `salt:scrypt(password, salt, 64)`，位置为 `server-admin-auth.ts:5`。
- 会话签名使用 HMAC-SHA256，token 形如 `sessionId.signature`，位置为 `server-admin-auth.ts:26`。
- CSRF token 是 32 字节随机值的 base64url 表示，位置为 `server-admin-auth.ts:54`。
- 服务端保存 CSRF token 的 SHA-256 digest，校验使用 timing-safe compare，位置为 `server-admin-auth.ts:58`。
- cookie 名称为 `sts2_server_admin_session`，写入逻辑位于 `lobby-service/src/app.ts:1772`。
- 服务端会话保存在进程内的 `Map`，声明位于 `lobby-service/src/app.ts:245`。
- 管理操作的 CSRF 校验位于 `lobby-service/src/app.ts:1763`。

## 设置和状态数据

- 持久化实现：`lobby-service/src/server-admin-state.ts:49` 的 `ServerAdminStateStore`。
- 默认状态文件：`${process.cwd()}/data/server-admin.json`，配置位置为 `lobby-service/src/config.ts:130`。
- 持久字段定义于 `lobby-service/src/server-admin-state.ts:17`：
  - `displayName`
  - `publicListingEnabled`
  - `modSyncEnabled`
  - `bandwidthCapacityMbps`
  - `probePeak7dCapacityMbps`
  - `resolvedCapacityMbps`
  - `capacitySource`
  - `announcements`
  - `extraMetadata`
  - `chatFeatures`
- 公告字段定义于 `lobby-service/src/server-admin-state.ts:8`：`id`、`type`、`title`、`dateLabel`、`body`、`enabled`。
- `GET /server-admin/settings` 的运行数据组装位于 `lobby-service/src/app.ts:1815`。
- 返回数据包含 peer network 状态、带宽、容量、建房保护、relay 流量、relay 活跃数量、聊天 feature、聊天 metrics 和 `update`。
- 聊天 metrics 字段定义于 `lobby-service/src/app.ts:1855`。
- 更新状态类型定义于 `lobby-service/src/service-update.ts:23`，字段为：
  - `currentVersion`、`latestVersion`、`updateAvailable`、`phase`
  - `deploymentMode`、`enabled`、`canUpdate`、`preflight`
  - `releaseUrl`、`releaseNotes`
  - `checkedAt`、`startedAt`、`completedAt`、`error`

## 环境变量

加载位置为 `lobby-service/src/config.ts:126` 至 `lobby-service/src/config.ts:134`；示例位于 `lobby-service/.env.example:58`：

- `SERVER_ADMIN_USERNAME`
- `SERVER_ADMIN_PASSWORD_HASH`
- `SERVER_ADMIN_SESSION_SECRET`
- `SERVER_ADMIN_SESSION_TTL_HOURS`
- `SERVER_ADMIN_STATE_FILE`
- `SERVER_UPDATE_ENABLED`
- `SERVER_UPDATE_DATA_DIR`
- `SERVER_UPDATE_CHECK_INTERVAL_MINUTES`
- `SERVER_UPDATE_RELEASES_API_URL`
- `SERVER_UPDATE_DEPLOYMENT_MODE`

## 测试资料

- 页面单元测试：`lobby-service/src/server-admin-ui.test.ts`
- 页面内联脚本语法校验（`node:vm` 编译检查，防止模板字符串内 JS 括号失衡）：`server-admin-ui.test.ts` 的 `server admin page inline script parses without syntax errors`
- 认证测试：`lobby-service/src/server-admin-auth.test.ts`
- 状态存储测试：`lobby-service/src/server-admin-state.test.ts`
- 运行态测试：`lobby-service/src/server-admin-runtime-state.test.ts`
- HTTP/CSRF 集成测试：`lobby-service/src/app.integration.test.ts:2417`
- 发行包内容测试：`lobby-service/src/package-content.test.ts`
- 更新管理器测试：`lobby-service/src/service-update.test.ts`
- 当前完整测试结果：441 tests passed，0 failed。
- 测试后生成的 `lobby-service/dist` 已按清理要求移除。
- 命令定义于 `lobby-service/package.json:18`：
  - `npm run check`
  - `npm run test`
  - `npm run build`
  - `npm start`

## 部署资料

- SSH 别名：`sub2api-tencent`
- 正式测试服务：`sts2-lobby-test.service`
- 服务目录：`/opt/sts2-lobby-test/lobby-service`
- systemd 工作目录：`/opt/sts2-lobby-test/lobby-service`
- systemd 环境文件：`/opt/sts2-lobby-test/lobby-service/.env`
- systemd 启动脚本：`/opt/sts2-lobby-test/start-lobby-service.sh`
- 进程用户和组：`sts2:sts2`
- 服务监听：`127.0.0.1:8787`
- 当前部署版本：`0.5.2`
- 当前服务状态：`active`
- 当前管理状态文件：`/opt/sts2-lobby-test/lobby-service/data/server-admin.json`
- 当前更新数据目录：`/opt/sts2-lobby-test/lobby-service/data/service-update`
- 当前部署模式：`systemd`
- Nginx 主机：`sts2-test.43.133.192.249.nip.io`
- Nginx 对 `/server-admin` 配置了 localhost allow 和外部 deny。
- SSH 本地端口转发命令：`ssh -L 18787:127.0.0.1:8787 sub2api-tencent`
- 转发后的页面地址：`http://127.0.0.1:18787/server-admin`
- 管理员密码哈希和会话 secret 位于服务器 `.env`，本文不记录其值。
- 之前的 `sts2-admin-demo.43.133.192.249.nip.io` 演示站点、证书和模拟 `0.5.3` 环境均已删除。

## 部署与打包文件

- 运行入口：`lobby-service/scripts/service-runtime.mjs`
- Dockerfile：`lobby-service/Dockerfile`
- Docker 环境示例：`lobby-service/deploy/lobby-service.docker.env.example`
- Docker Compose：`lobby-service/deploy/docker-compose.lobby-service.yml`
- systemd 示例：`lobby-service/deploy/sts2-lobby.service.example`
- Linux 安装脚本：`scripts/install-lobby-service-linux.sh`
- 打包脚本：`scripts/package-lobby-service.sh`
- 服务文档：`lobby-service/README.md`

## 当前相关工作树文件

管理员面板、自动更新与 Dashboard 重构已随 commit `6a7d5c7` 提交，工作树干净。生产部署交接见 `docs/admin-ui-production-upgrade-handoff.md`。
