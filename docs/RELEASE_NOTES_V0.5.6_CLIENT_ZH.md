# STS2 LAN Connect v0.5.6-rc4 客户端测试候选说明

> 状态：客户端 `0.5.6-rc4` 测试候选，不是正式版。lobby-service 继续使用 `0.5.6-rc1`；本次仅修复客户端与 RitsuLib 的开始游戏消息兼容，不改变服务端协议。

本候选版继续处理安装 RitsuLib 后出现的“房主黑屏、客机等待”问题。RC3 用户日志确认客户端实际在启动阶段失败，尚未进入联机流程；RC4 修复这一确定性 Harmony 补丁冲突。请优先在可回滚的测试存档上验证。

## RC3 启动失败根因

- 三次 RC3 启动都在给 `NetMessageBus.SerializeMessage<LobbyBeginRunMessage>` 安装第 7 个必需补丁时抛出 `InvalidProgramException`。
- RitsuLib `0.5.8` 已先在这个闭合泛型方法上安装 postfix；RC3 再增加 LAN prefix 时，Harmony 2.4.2 重建组合 wrapper 失败。
- RC3 随即记录 `applied=6/7, failed=1`，回滚全部 6 个已安装线协议补丁并中止客户端初始化。日志中没有成功初始化、加入大厅或开局记录，因此 RC3 实际无法验证原计划中的联机修复。

## RC4 RitsuLib 补丁桥

- RC4 在安装 LAN begin-run prefix 前，精确识别并卸载 RitsuLib 的对应 postfix；其他 Mod 的无关 postfix 保持不变。
- LAN prefix 在消息总线边界按当前协议位宽写入 `LobbyBeginRunMessage`。5-bit 玩家列表消息体完成后，客户端直接调用已经验证的 RitsuLib postfix 委托，继续追加运行数据尾部并更新长度。
- 该方式保留 RitsuLib 功能，但不再要求 Harmony 把冲突的闭合泛型 prefix/postfix 组合进同一个 wrapper。
- 如果任何必需补丁安装失败，客户端会先移除 LAN 补丁，再按原 owner、优先级及排序约束恢复 RitsuLib postfix，避免留下半补丁状态。
- 未安装 RitsuLib 时桥接保持关闭，LAN begin-run prefix 单独工作。所有同房玩家应统一使用客户端 `0.5.6-rc4`。

## 加入前线协议签名

- 新增 `WireCacheSignatureV1`，对四张 ModelId net-id 表及其四个编码位宽生成指纹。`affects_gameplay: false` 的 MOD 虽不进入原版 `idDatabaseHash`，仍可能占用 net-id 并改变线上编码；过去双方能通过原版握手，却从第一个 ModelId 起错误解码，表现为一方卡在等待页、另一方黑屏。
- 签名在两处检查：lobby-service 签发 join ticket 之前，以及游戏握手发送 join request 之前。发现真实签名不一致时，即使使用 relaxed 配置也会拒绝加入。
- 缺失、格式不合法或读取失败的签名始终 fail-open，只记录警告并允许加入。诊断失败不能把原本兼容的玩家锁在房间外。
- 默认兼容配置已从 `test_relaxed` 改为 `strict`，不再吞掉原版 gameplay MOD / ID 数据库不一致检查。relaxed 仅保留为显式测试选项。
- 玩家可见结果是明确且有意的：两台机器的内容 MOD 改变了线上编码时，现在会提示不一致并拒绝，而不是先连接再黑屏或卡死。

## 续局房间恢复

- safe-load 不再给缺失绑定的存档隐式写入 `hostChannel=lan`，修复大厅续局回到主菜单后无法重新发布房间的问题。
- 存档修复不再删除大厅绑定。通道改为明确的三态决策：`lan`、`lobby`、未知；未知时只弹出一次选择，成功保存后再继续。
- 绑定 schema 已迁移。被旧版本错误写成 LAN 的存档会重新询问一次，不会继续永久卡在“不能发布房间”的状态。
- 同一修复也覆盖房主重开后队友看不到房间的现场报告。

## 踢出与身份绑定

- 大厅身份不再等同于存档槽位。安装级 256-bit credential 用于回答“当前是哪位玩家”，槽位 id 继续只承担游戏与存档语义；credential 由 lobby-service 保留在服务端，不会作为玩家身份转发给其他客户端。
- 房主列表每一行都会保存绘制当时的 binding handle。点击踢出时服务端校验该绑定仍属于同一占用者，避免列表绘制后发生槽位接管而把操作转向新人。
- 踢出当前占用者不会再永久拉黑槽位原主人，通知也不会按槽位误发给旧连接。
- 旧 lobby-service 无法返回 binding-aware 结果时，客户端不会发送可能误封槽位的服务端踢出请求；只执行受当前占用者校验保护的本地移出，并明确告知房主该玩家没有被封禁、可以再次加入。

## 测试与日志

- 先确认所有玩家客户端均显示 `0.5.6-rc4`、lobby-service 显示 `0.5.6-rc1`，再测试安装 RitsuLib 后的建房、加入、双方准备和正式开局。
- 正常启动应记录 `patches applied=7, failed=0` 与 `ritsuTailBridge=True`，不再出现 `InvalidProgramException` 或 `required wire patches incomplete`。
- 房主发送开始游戏消息时应记录 `lobby begin-run forced at message-bus boundary`、`lobbyListBits=5` 与 `ritsuTail=True`；双方随后都应进入战斗状态同步。
- 回归测试覆盖真实 Harmony 闭合泛型组合、精确卸载、无关 postfix 保留和失败恢复；另使用实际 RitsuLib 私有 `SerializePatch<LobbyBeginRunMessage>.Postfix` 完成跨程序集绑定验证。
- 早先“同一存档在不同 MOD 组合下线上编码不同”的证据来自玩家自己的日志。本版会把签名、四个位宽、四张表的条目数以及每个 MOD 的 `affects_gameplay` 标记写入调试报告和 `godot.log`。
- 遇到加入失败、黑屏、等待页卡住或误判 MOD 时，请提交两台机器各自的完整 `godot.log`；只提供单边日志无法可靠比较线上编码。

## 已知限制

- 旧版房主客户端在执行踢出后的 1.5 秒内，仍可能短暂断开刚接管槽位的替代玩家。
- 房主控制 WebSocket 丢失后，该房间会禁用踢出。
- 踢出某位玩家会使另一位玩家针对同一槽位、尚未兑换的 join ticket 失效，需要重新加入。
- 新建存档开始游戏后的局中重连仍不可用；本候选版只修复已有续局和房主重开路径。

## 安装与回滚

1. 完整退出游戏，并备份重要多人存档。
2. 客户端使用候选包覆盖安装完整 `sts2_lan_connect` 目录，不能混用旧 DLL、PCK 或 manifest。
3. lobby-service 保持 `0.5.6-rc1`，无需因本次客户端修复重复部署。
4. 通过 Steam 启动游戏，确认模组列表显示 `0.5.6-rc4`。
5. 回滚时完整移除候选客户端目录并恢复上一正式版，同时把 lobby-service 回滚到配套的正式部署。

本文件对应发布候选准备阶段；下载地址与 SHA-256 校验值以 GitHub Release `v0.5.6-rc4` 页面为准。
