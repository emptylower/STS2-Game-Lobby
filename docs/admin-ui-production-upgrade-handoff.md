# 管理员面板 Dashboard 升级：生产部署交接

更新日期：2026-07-23

面向接手 Agent：管理员面板页面升级**已完成并提交**，本文件给出验证与升级生产的全部事实。页面结构细节见 `docs/admin-ui-agent-handoff.md`。

## 当前状态

- 代码已提交：`main` 分支 commit `6a7d5c7`（`feat: server admin console with dashboard UI and self-update center`）。
- 本地验证结果：`npm run check` 通过；`npm test` 441 passed / 0 failed。
- 本轮为纯前端页面重构 + 已合并的管理面板/自动更新特性，**没有 API 变更**，`GET/PATCH /server-admin/*` 全部接口与鉴权、CSRF 流程不变。
- 管理端凭据仍在服务器 `.env`（`SERVER_ADMIN_USERNAME` / `SERVER_ADMIN_PASSWORD_HASH` / `SERVER_ADMIN_SESSION_SECRET`），本仓库不记录其值。

## 页面变更摘要（相对 0.5.2 已部署版本）

- 深色运维监控台风格（antd `darkAlgorithm`，主题常量 `ADMIN_THEME`）。
- 登录后改为标签导航：`概览`（默认）/ `公开设置` / `大厅公告` / `服务更新`。
  - 未保存草稿 → `公开设置` 标签显示琥珀色提示点。
  - 有可用更新 → `服务更新` 标签显示提示点，概览页出现可点击的更新预告横幅。
- 原 4 张明细表压缩为 2 张可视化面板：容量利用率环形仪表、渐变计量条、relay Host/Client 环形占比图、消息接受率计量条、带宽迷你趋势线（每次轮询采样，保留 60 点）。
- 新增页脚：版本、部署模式、「每 15 秒自动刷新 · 最近同步时间」。
- 服务更新页新增 Release 说明阅读区（`update.releaseNotes` 存在时展示）。
- 移动端：992/640/480 三档断点；`.page-content`/`.page-footer`/`.page-header` 的 `width: 100%` + `min-width: 0` 是修复横向溢出的关键，不要移除。
- 页面仍依赖 unpkg CDN（dayjs / React / ReactDOM / antd），目标环境需能访问 unpkg。

## 部署前验证（本地或 CI）

```bash
cd lobby-service
npm ci
npm run check
npm test        # 期望 441 passed / 0 failed
npm run build   # 仅验证可构建；dist 不需要随包提交
```

关键测试（任一失败都不要部署）：

- `server admin page inline script parses without syntax errors`（`server-admin-ui.test.ts`，用 `node:vm` 编译生成的页面脚本，防止模板字符串内 JS 语法错误导致白屏）。
- `server admin page mobile layout wraps actions and collapses dashboard grids`（移动端布局锚点）。
- `app.integration.test.ts` 中 `/server-admin` 的登录/CSRF/设置/更新路由用例。

## 打包

```bash
# 在仓库根目录 STS2-Game-Lobby/ 下执行
bash scripts/package-lobby-service.sh
# 产物：lobby-service/release/sts2_lobby_service/ 与 lobby-service/release/sts2_lobby_service.zip
```

## 部署步骤

### 1. 先上测试服

- SSH 别名：`sub2api-tencent`；服务：`sts2-lobby-test.service`。
- 服务目录与 systemd 工作目录：`/opt/sts2-lobby-test/lobby-service`；环境文件：同目录 `.env`；进程用户 `sts2:sts2`；监听 `127.0.0.1:8787`。
- 将新包解压到服务目录（按 `scripts/install-lobby-service-linux.sh` 的既有布局），重启：`systemctl restart sts2-lobby-test.service`。
- 验证：
  - `journalctl -u sts2-lobby-test.service --since '10 minutes ago' --no-pager` 无异常。
  - `ssh -L 18787:127.0.0.1:8787 sub2api-tencent` 后访问 `http://127.0.0.1:18787/server-admin`。
  - 页面验收清单见下节。

### 2. 页面验收清单（测试服通过后再上生产）

- 登录页：深色品牌卡片，登录成功进入 `概览`。
- 四个标签均可切换且无白屏；浏览器控制台无 `Unexpected token` 等脚本错误（401/favicon 404 属正常）。
- 概览：4 张 KPI 卡、节点状态告警、`带宽与容量`（环形仪表 + 计量条 + 容量 chips）、`连接与消息`（relay 环图 + 接受率计量条）正常渲染；15 秒轮询后页脚「最近同步」时间更新。
- `服务更新`：版本号、阶段标签、部署模式标签正常；`检查更新` 按钮可触发；预检未通过时显示原因（属预期行为，取决于部署模式识别）。
- `公开设置`：表单回显当前设置；修改任意开关后标签出现提示点；`保存设置` 成功。
- `大厅公告`：数量徽标与实际公告数一致；新增/保存/删除正常。
- 手机端（约 390px 宽）：无横向滚动条，导航条可横向滑动，KPI 与面板单列堆叠，页脚堆叠显示。

### 3. 生产部署

- 生产服务：`sts2-lobby.service`；主机：`47.111.146.69`（user: `admin`）。
- 步骤同测试服：传包 → 解压到服务目录 → `systemctl restart sts2-lobby.service` → 按上节清单验证（生产用 `journalctl -u sts2-lobby.service` 与生产的管理员入口）。
- 注意：服务端会话保存在进程内存，重启后所有管理员需要重新登录，属预期。

## 回滚

- 代码层面：`git revert 6a7d5c7` 或重新部署上一版 `sts2_lobby_service` 包（v0.5.2 发布包）。
- 页面为服务端模板字符串直出，无独立前端构建产物，回滚即恢复旧页面。

## 相关文件

- 页面源码：`lobby-service/src/server-admin-ui.ts`（页面结构/行号索引见 `docs/admin-ui-agent-handoff.md`）。
- 页面测试：`lobby-service/src/server-admin-ui.test.ts`。
- 更新管理器：`lobby-service/src/service-update.ts`（测试 `service-update.test.ts`）。
- 运行入口：`lobby-service/scripts/service-runtime.mjs`。
- 打包：`scripts/package-lobby-service.sh`；安装：`scripts/install-lobby-service-linux.sh`。
