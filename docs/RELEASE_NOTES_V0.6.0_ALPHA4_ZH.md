# STS2 LAN Connect v0.6.0-alpha.4 测试版说明

发布日期：2026-08-16

这是客户端与 lobby-service 的 SL 协议身份修复测试版。所有玩家应统一升级客户端并完整重启游戏；自建服务建议同步升级到 `0.6.0-alpha.4`。alpha.4 客户端仍兼容尚未升级的旧服务。

## 本轮修复

- 修复房间绑定写入配置时只复制房间名、玩家和来源等基础字段，遗漏 profile、carrier、RitsuLib presence、WireCache 签名与 capability digest 的问题。该遗漏会让 SL/读档后的房间重新协商协议，并显示“联机协议不支持”。
- 续局重新发布现在明确携带存档冻结的协议选择。没有已保存选择时，无 RitsuLib 使用兼容模式，有 RitsuLib 使用 `tail_v1`，不会先冻结一个与实际建房响应不同的本地选择。
- 修复 lobby-service 对 WireCache 签名调用小写转换的问题。`wcv1:` 后的 Base64URL 内容区分大小写，改变大小写会改变 capability digest。
- alpha.4 客户端对旧服务曾返回的小写签名进行兼容比较，因此当前公共服务尚未更新时也能正常 SL；客户端仍保留存档中的原始签名用于后续预检。
- 保留 alpha.3 的 RitsuLib 修复：客户端在 ENet 连接成功并获得真实玩家 ID 后才激活 sidecar 会话，避免永久绑定 `netId=0`。用户本次提交的 Ritsu 失败日志显示客户端仍为 `0.6.0-alpha.2`，该版本不包含此修复。

## 本地验证

- macOS 游戏通过 `--force-steam=off` 启动，读取先前出现协议错误的多人存档后成功重新发布房间；Android 无 Ritsu 客户端成功看到房间并按原保存槽位加入，服务端确认两个已连接槽位。
- macOS 安装官方 RitsuLib v0.5.12 后完整启动：核心补丁 415/415、动态补丁 3/3 成功；LAN Connect 成功创建 `tail_v1` 房间，Ritsu sidecar 绑定为 `Host, netId=1`。
- Android 上官方 RitsuLib v0.5.12 仍在其自身 `ApplySerializePatches` 动态补丁初始化阶段失败并黑屏，这一外部限制未被计为 alpha.4 的可用路径；Android 无 Ritsu 启动与加入正常。

## 复测要求

- 删除旧版客户端 DLL/PCK 后安装 alpha.4；不要只覆盖其中一个文件。所有参与者完整退出并重启游戏。
- Ritsu 房间必须由所有参与者统一安装并启用 RitsuLib。请确认日志中的 LAN Connect 版本是 `0.6.0-alpha.4`，不要继续使用本次反馈里的 `0.6.0-alpha.2`。
- SL/读档后检查房间是否重新出现、队友能否按原角色槽位加入。若仍失败，请同时提交房主与加入者从启动开始的完整 `godot.log`。
- 自建 lobby-service 建议升级 alpha.4 并重启以清除内存中的旧房间；服务端升级不是 alpha.4 客户端修复公共旧服务 SL 的前置条件。
