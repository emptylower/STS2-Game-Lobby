# STS2 LAN Connect v0.6.2-alpha.1 发布说明（内部预览）

- 日期：2026-09-08
- 版本：`0.6.2-alpha.1`（客户端与服务端同步；tail 房间 `minimumClientVersion` 同值）
- **本版为 pre-release 预发布：未经真机双实例 E2E 验收前不对外发布，不更新 Steam 创意工坊；当前正式版仍为 `0.6.1`。**

## 一、修的是什么：跨端（PC ↔ 安卓）无法加入新协议房间

0.6.1 起用户反馈"手机只能和手机连、电脑只能和电脑连"，双向复现 HTTP 409
`lan_registry_fingerprint_mismatch`（提示"双方的联机消息注册表不一致"）。两端客户端版本、游戏版本、
游戏程序集、BaseLib / RitsuLib、wire_cache 签名完全一致，仍被拒绝。

根因：`native_bus_v1` 的 `typeId` 是**接收方本地消息表的下标**，由游戏按本机 MOD 集合排序分配。
0.6.1 发送时写的是自己的下标，隐含"两端表必须相同"，并用 registry fingerprint 全表哈希门禁强制这一点。
任何一端多装一个注册 `INetMessage` 的第三方 MOD（本次事故为安卓端 `Map Enhance Mod`，其 ID 归位补丁在
安卓上失败）即令全表不一致——而该 MOD `affects_gameplay:false`，对既有三项 MOD 检查完全隐形。

## 二、方案：按对端寻址（peer-addressed type id）

`typeId` 是接收方的下标而非全局身份，因此**发送时写接收方声明的下标**，两表从此无需任何关系：

- 发送端线头字节与帧内 `localTypeId` 均写对端 id；外层帧版本 `ver` 由 `1` 升为 `2`
  （`ver != 2` ⇒ `lan_native_frame_invalid` 结构化拒绝，不会误读）。
- 对端 id 经服务端透传：建房 offer / join 请求 / join 响应（`hostNativeBusTypeId`）/ 控制通道
  envelope（`peerNativeBusTypeId`），轨道与已验证的 `protocolFlowNonce` 完全一致；中继保留字段防伪造。
- **撤除 fingerprint 门禁**：`lan_registry_fingerprint_mismatch` 不再拒绝加入；指纹降级为诊断值
  （仍必须携带且格式合法，`lan_registry_fingerprint_required` 保留）。
- 新增必填 `nativeBusTypeId`（整数 0-255）：tail_v1 创建与加入缺失 / 越界复用
  `lan_registry_fingerprint_required`（文案区分）。
- `minimumClientVersion` 升至 `0.6.2-alpha.1`：0.6.1 客户端加入新协议房间得到明确的 426 升级提示，
  而非在 `ver` 校验处才失败。
- 启动自检得出终局裁决后补打一次 `native_bus: ready local_type_id=… registry_fingerprint=…`
  诊断行（此前该行从不出现）。

## 三、兼容性

| 组合 | 结果 |
|------|------|
| 0.6.2 ↔ 0.6.2，MOD 集合不同（含跨平台） | **正常**（本候选的目标） |
| 0.6.1 客户端加入 0.6.2 房间 | 服务端 426 `lan_client_version_too_old`，明确提示升级 |
| 0.6.1 帧误达 0.6.2 客户端 | `lan_native_frame_invalid` 结构化拒绝 |
| 0.6.2 客户端 ↔ 0.6.1 服务端 | 行为与 0.6.1 相同（不回归） |
| `compat_4_5_v1` 兼容房间 | 完全不受影响 |

## 四、升级与回滚

- 客户端覆盖安装即可；服务端部署 `lobby-service` 0.6.2-alpha.1 并重启（自动更新不接受 pre-release，需手动升级）。
- 回滚：卸载 0.6.2-alpha.1 客户端、重装 0.6.1；0.6.2 客户端加入 0.6.1 服务端的 tail 房间会被旧指纹门禁拒绝（与 0.6.1 行为一致）。

## 五、发布前验收（进行中）

- ✅ 客户端/服务端全量单测与契约测试（含"指纹不一致不再拒绝"核心回归行）；
- ⏳ 真机双实例 E2E（两端人为装不同 `INetMessage` MOD 集合建房 + 加入；0.6.1 客户端加入 0.6.2 房间得到升级提示；compat 房间全流程回归）——**发布阻断**。

## 校验和

- `<待打包后填写>`
