# STS2 LAN Connect v0.6.0-alpha.8 Android gshared 诊断候选说明

发布日期：2026-08-20

> 状态：**GitHub-only Pre-release / PENDING（原失败设备待验证）**

`0.6.0-alpha.8` 是 Android gshared 修复与启动取证诊断候选，不是正式版。它只通过 GitHub Pre-release 分发，**不更新 Steam 创意工坊**；Workshop 二进制与描述继续停留在 alpha.7。客户端升级到 `0.6.0-alpha.8`，lobby-service 继续使用 `0.6.0-alpha.6`，不改变服务端 API、DTO 或协议版本。

本候选已实现针对 Android 的非泛型补丁路径，但原报告失败的真实设备尚未完成复测。因此当前只能写“**修复待验证**”，不能宣称 Android gshared 问题已经解决。

## 本轮候选改动

- 非 Android 环境继续使用既有 30 项 `desktop_generic_v1` Tail 补丁计划，保持已经验证的桌面行为。
- Android 改用固定 15 项 `android_non_generic_v2` 计划：9 个具体消息 serializer、`PacketWriter.Reset`、反序列化入口、两个接收入口，以及 host/client ENet 发送入口。Android 计划中的 target 与 hook 在应用前必须全部验证为非泛型。
- 9 类 concrete serializer 继续复用既有 Tail 投影和 standalone 字节格式；官方 RitsuLib v0.5.13 仍只走公开 typed-sidecar API，不引入私有桥接或协议变更。
- Ritsu sidecar 与 vanilla 发送按真实 peer 配对，保持 sidecar-before-vanilla。匹配异常、重复消费、sidecar 失败，或 sidecar 成功后 vanilla transport 抛错时会 fail-closed 并终止活动 Tail binding，避免房间成员进入不同协议状态。
- 补丁计划在应用前完整解析并使用稳定 ID 和顺序；缺失目标、泛型目标或 hook 校验失败都会在打补丁前停止，不通过跳过目标、吞掉异常或关闭 Android Tail 换取启动。
- 原子补丁失败时保留首个产品异常；即使回滚自身失败，也不会用回滚异常覆盖首因，并会记录剩余本 MOD patch 与未被移除的外部 Harmony owner。

## 启动诊断

- `Entry.Init` 从第一行开始建立诊断 session，记录 10 个初始化阶段的 `begin`、`success`、`failure` 与 `elapsed_ms`。
- 每个补丁记录稳定 plan ID、ordinal/total、类别、消息类型、完整 target/hook、泛型标记、metadata token、module MVID、Harmony owner/priority、耗时与脱敏异常指纹。
- 事件同步写入 `user://sts2_lan_connect/diagnostics/<utc>-<session>/startup.jsonl`，并以 `sts2_lan_connect patch_diag:` 镜像到普通游戏日志和 logcat。
- `init-sentinel.json` 会原子记录当前进度；下一次启动可报告上一次未完成的 stage、patch ID 与 sequence。
- 诊断 session 默认保留最近 3 个，并在完成启动后按 64 MiB 总量清理旧目录；当前启动证据不会在本次启动中删除。
- 诊断不会记录玩家名或平台 ID、机器名、房名、room/ticket/control/save ID、IP/MAC、密码、token、配置正文、聊天、包内容或完整 URL/query。

## 兼容性与更新要求

- 同一房间所有成员必须使用完全相同的游戏版本和客户端 `0.6.0-alpha.8`；不要把 alpha.8 与 alpha.7 或更早客户端混用。
- Ritsu 房间的 macOS 与 Android 成员必须统一使用官方 RitsuLib v0.5.13；官方 v0.5.12 不用于本轮验证。
- 有 RitsuLib 与无 RitsuLib 的混合组合继续在 ticket 和 transport 前明确拒绝。
- lobby-service 保持 `0.6.0-alpha.6`；已部署 alpha.6 的服主无需升级或修改配置。
- 更新 DLL、PCK 与 `sts2_lan_connect.json` 后必须完整退出并重新启动游戏，不能依赖 MOD 热重载。

## 原失败设备验收：PENDING

在原失败 Android 环境中关闭 SpeedX 后，连续执行 3 次完整冷启动。每次成功都必须同时看到：

- `android_non_generic_v2`
- `applied=15/15`
- `generic_target_count=0`
- 最终初始化哨兵完成

三次均满足后，才能把该设备的 gshared 验证从 PENDING 改为通过。若仍失败，三次日志应稳定指向同一个精确 stage/patch ID，并包含 MethodInfo、程序集指纹、回滚结果和实际生成的可用 DMD。未完成该步骤前，发布说明必须继续保留“修复待验证”。

## Android 证据采集

先保存公开日志，再尝试导出 app-private 诊断目录；不要为了取私有文件而遗漏 launcher 和 logcat。

### 1. Launcher 日志

从 launcher 导出本次启动产生的完整 `sts2*.log`。alpha.8 会把每条诊断事件同步镜像到普通日志，因此即使私有 diagnostics 无法导出，最后一个 stage 和 patch ID 仍应可见。

### 2. ADB logcat

在冷启动前清空旧记录，复现后保存完整输出：

```bash
adb logcat -c
# 冷启动游戏并等待成功或失败
adb logcat -d > sts2-alpha8-logcat.txt
```

不要只截取原生崩溃末尾几行；证据需要覆盖本次启动从 `Entry.Init` 开始的完整过程。

### 3. App-private diagnostics session

设备允许时，按以下优先级导出整个 `user://sts2_lan_connect/diagnostics/` session，而不是只取单个 JSONL：

1. 使用 `run-as com.megacrit.sts2re` 读取应用私有目录。
2. `run-as` 不可用时，在已 root 的设备上读取同一目录。
3. 无 root 时，使用 launcher 提供的文件提供器导出 diagnostics session。

不同 launcher 的实际 app data 路径可能不同，应以其文件提供器或 `run-as` 返回的应用目录为准，不在公开文档中硬编码绝对路径。

### 私有目录不可导出时

如果 `run-as`、root 与 launcher 文件提供器都不可用，仍提交以下两份证据：

- launcher 导出的完整 `sts2*.log`
- 同一次冷启动的完整 `adb logcat`

这是受支持的降级路径。`sts2_lan_connect patch_diag:` 镜像应仍能定位最后 stage、patch ID、MethodInfo、程序集指纹与回滚结果。提交前检查日志，不要附带配置正文、聊天内容、密码、token 或完整 URL/query。

## 发布门禁

- 使用官方 RitsuLib v0.5.13 DLL 执行 `RITSULIB_ASSEMBLY=<official-v0.5.13-dll> ./scripts/verify-release.sh`。
- 回归 macOS `desktop_generic_v1`、Android standalone、Android 全员 Ritsu、混合 Ritsu 拒绝和 compat profile。
- 门禁通过后记录 ZIP、DLL 与 PCK 的 SHA-256，再创建 `v0.6.0-alpha.8` GitHub Pre-release。
- 不直接编辑 `releases/` 镜像，不发布或更新 Steam Workshop。

## 下载校验

ZIP、DLL 与 PCK 的 SHA-256 在最终构建和自动门禁完成后填写。任何未由本次 alpha.8 构建产出的旧 hash 都不得沿用。
