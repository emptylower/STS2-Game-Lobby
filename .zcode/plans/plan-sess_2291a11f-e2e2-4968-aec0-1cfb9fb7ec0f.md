# 客户端 v0.5.3 正式版发布计划

## 背景
- 当前 main 包含 0.5.2 之后的全部客户端变更：LAN/大厅续局通道拆分（issue #40）、BaseLib 毁档修复、安卓自动加入修复、局内聊天 HUD 改造
- GitHub 上 `v0.5.3` Release 已存在（lobby-service 单独发布），**复用该 Release**，客户端 zip 作为附件上传
- 构建环境已验证：dotnet 9.0.311 + godot451-mono (`~/.local/bin/godot451-mono`) 均可用

## 第一步：版本号提升（0.5.2 → 0.5.3）
1. `sts2-lan-connect/sts2_lan_connect.csproj` 第 10-13 行：Version/AssemblyVersion/FileVersion/InformationalVersion → 0.5.3
2. `sts2-lan-connect/sts2_lan_connect.json` 第 5 行：`"version": "0.5.3"`
3. 版本钉扎测试同步：
   - `sts2-lan-connect.Tests/Packaging/LanConnectPackageContentTests.cs`（第 50、53、54、85、101 行附近的版本断言与发布说明文件名引用 → 0.5.3 / `RELEASE_NOTES_V0.5.3_CLIENT_ZH.md`）
   - `lobby-service/src/package-content.test.ts`（第 297-313 行 client 钉扎 0.5.2 → 0.5.3；service 钉扎保持 0.5.3 不变）

## 第二步：文档更新（0.5.2 → 0.5.3 差异部分）
4. **新建 `docs/RELEASE_NOTES_V0.5.3_CLIENT_ZH.md`**（`V0.5.3_ZH` 已被服务端占用），按 0.5.2 结构组织：
   - LAN 与大厅续局通道拆分（HostChannel，纯 LAN 存档不再自动发布大厅）
   - LAN 续局身份码（`STS2LANRESUME:`）与安装级 LAN 身份
   - 放弃存档前自动备份（`user://sts2_lan_connect/save-backups/`）
   - 局内聊天 HUD 化改造（扁平半透明外壳、单行富文本、昵称配色、44px 触达、指针模式自适应、新消息自动浮现）
   - 修复：BaseLib 共存毁档、安卓加入页调试直连超时、大厅存档续局丢失发布、聊天不贴底等
   - 兼容范围：与 lobby-service 0.5.1/0.5.2/0.5.3 及 v0.5.1+ 客户端互通；无线协议变更；旧存档按 lobby 通道处理；支持游戏 v0.109.1
   - 安装与回滚
5. **`CHANGELOG.md`** 扩充现有 `[0.5.3]` 条目：引言改为"服务端 + 客户端同步发布"；将 `[Unreleased]` 的 5 条 Fixed 移入，并补充通道拆分、LAN 续局码、存档备份、聊天 HUD 条目（标注客户端归属与 issue #40）；清空 Unreleased；底部补 `[0.5.3]:` 链接引用
6. **`README.md`**：badge → client-v0.5.3；「当前正式客户端为 v0.5.3」；客户端亮点/下载段落（ZH+EN）；文档索引；顺带修正 EN 侧已过时的 `Lobby Service: 0.5.2` 行
7. **`docs/CLIENT_RELEASE_README_ZH.md`**：版本表述与功能亮点（ZH+EN 两半）——此文件会被打入包内 README，必须在打包前更新
8. **`docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md`**：顶部版本段落（ZH 13/15/21 行、EN 246/248/254 行附近）→ 0.5.3；续局身份码内容此前已随开发写入，补充 v0.5.3 小节要点（通道拆分 + 聊天 HUD 简述）
9. **Steam Workshop**：`docs/STEAM_WORKSHOP_DESCRIPTION_ZH.txt` 当前版本行 → 0.5.3 + 功能文案；新建 `docs/STEAM_WORKSHOP_UPDATE_V0.5.3_ZH.txt` 更新公告

## 第三步：验证与构建
10. 运行客户端测试套件 `dotnet test`（预期 791+ 通过，版本钉扎测试随第一步更新后保持绿）
11. 运行 lobby-service 测试（`npm run check && npm run test`，验证 package-content.test.ts 钉扎）
12. `./scripts/package-sts2-lan-connect.sh`（自动执行 dotnet build + headless Godot PCK 导出），产出 `sts2-lan-connect/release/sts2_lan_connect-release.zip`，记录 SHA-256 到发布说明文档

## 第四步：提交与发布
13. 提交全部改动（release commit，参照 `d19b943` 模式）并推送 main
14. `gh release view v0.5.3` 确认后，`gh release upload v0.5.3 sts2-lan-connect/release/sts2_lan_connect-release.zip` 上传客户端包，并在 Release 正文中补充客户端 0.5.3 说明段落
15. 不新建标签、不触碰 `releases/` 镜像目录（公共仓库同步属另一流程）

## 明确不做
- 不修改 lobby-service / server-registry 版本号（服务端 0.5.3 已发布）
- 不创建 v0.5.3-client 等新标签
- 不同步 Steam Workshop 后台（仅准备文案文件）