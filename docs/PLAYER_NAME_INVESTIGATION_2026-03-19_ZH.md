# 2026-03-19 玩家名异常排查记录

## 范围

- 客户端版本：`0.2.1`
- 游戏版本：`v0.99.1`
- 自部署服务端：`118.25.157.52:8787`
- 排查时间：`2026-03-19`

## 结论

- `118.25.157.52` 上运行中的大厅服务在玩家名链路上与当前仓库源码一致。
- 服务端不会把 `鬼神易`、`GuiShenYi` 之类的玩家名改写成 `u0_a***`。
- 出现 `u0_a***` 时，异常值已经存在于客户端发往服务端的 `hostPlayerName` / `playerName` 请求体中。
- Android 端在玩家名配置为空时会回退到系统用户名，而系统用户名通常表现为 `u0_a***`。

## 关键证据

### 1. 服务端源码与线上运行文件一致

- 线上 `src/server.ts`、`src/store.ts`、`dist/server.js`、`dist/store.js` 与本地仓库同名文件 hash 一致。
- 服务端建房接口直接读取 `hostPlayerName`。
- 服务端加房接口直接读取 `playerName`。
- 服务端连接事件日志也只是原样打印 `playerName`。

### 2. 线上日志显示异常值来自客户端

- `journalctl -u sts2-lobby.service` 中可见多条如下日志：
  - `hostPlayer="u0_a270"`
  - `player="u0_a412"`
  - `player="u0_a229"`
- 同一时段也存在大量正常中文或英文玩家名，说明服务端并未统一改写所有玩家名。

### 3. 客户端当前回退逻辑会使用系统用户名

- `LanConnectConfig.GetEffectivePlayerDisplayName()` 在未配置玩家名时回退到 `Environment.UserName`。
- Android 上该值常见格式即 `u0_a***`。

## 额外发现

- 线上部署目录 `/opt/sts2-lobby/lobby-service` 中存在 `dist/server.js.bak`。
- 线上 `package.json` / `package-lock.json` 版本号仍为 `0.1.1`，但实际 `src/` 与 `dist/` 代码已经是当前逻辑，说明部署目录是混合状态。
- 该混合状态不是这次玩家名异常的直接原因，但建议后续做一次干净重装，避免再次误判。

## 本次修复

### 客户端加固

- 玩家名输入框不再只依赖 `FocusExited` 持久化。
- 新增 `TextChanged` 持久化，降低 Android 软键盘/焦点切换导致“界面已改名但请求仍用旧值或空值”的概率。

### 调试报告增强

- 新增以下字段，便于下次快速判断名字来源：
  - `player_name_source`
  - `configured_player_name`
  - `fallback_system_user_name`
  - `fallback_user_name_looks_like_android_uid`

## 建议验证步骤

1. 在 Android 端安装包含本次修复的客户端版本。
2. 打开大厅设置，输入玩家名后直接建房或加房。
3. 重新导出客户端 debug report。
4. 确认以下字段：
   - `player_name_source=configured`
   - `configured_player_name` 为期望名字
   - `fallback_user_name_looks_like_android_uid=true` 仅用于说明系统回退值形态，不代表当前请求一定使用了该值
5. 再对照服务端日志核对是否仍出现 `u0_a***`。
