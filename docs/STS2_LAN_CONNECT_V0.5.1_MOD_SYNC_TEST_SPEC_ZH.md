# STS2 LAN Connect v0.5.1 MOD 同步测试规格

> 执行计划：`docs/STS2_LAN_CONNECT_V0.5.1_MOD_SYNC_PLAN_ZH.md`
>
> 规格版本：1
>
> 基线提交：`66ceb7201277bfbe36fb9a91c9992e114972413f`
>
> 功能分支：`feat/mod-sync-0.5.1`

本文把 Phase 0 锁定的 DTO、限制、错误契约、平台矩阵和验收条件转成可直接实现的测试名称。后续 Phase 若改变这些契约，必须先更新本文并说明兼容影响。

## 1. 不可放宽的全局断言

- 游戏版本使用规范化后的完整版本字符串做精确比较；不同版本始终硬拦截，`test_relaxed` 不能绕过。
- 仅加载中的 `affects_gameplay=true` MOD 和从这些根节点可达的必要 dependency 进入 inventory。
- 非 gameplay 且不是必要 dependency 的 MOD 不进入差异、不显示、不禁用、不阻止加入。
- 自动操作仅限 Steam Workshop 订阅/下载，以及用户二次确认后的本机选择性禁用。
- 不接受房主、lobby-service 或任意 URL 提供的 DLL、PCK、ZIP 或其他 MOD 二进制。
- MOD 启用状态或安装内容变化后必须重启。pending join 永不保存密码或任何 token。
- v0.5.0 对端缺少能力字段或 inventory 时回退原加入流程；v0.5.1 不改变现有 `/join` 契约。

## 2. 协议冻结

### 2.1 `LobbyModDescriptor`

字段顺序用于 canonical JSON：

1. `id: string`
2. `version: string`
3. `role: "gameplay" | "dependency"`
4. `source: "steam_workshop" | "mods_directory" | "unknown"`
5. `workshopFileId?: string`
6. `dependencies: string[]`

Canonical 规则：descriptor 按清理后的 `id` 做 Ordinal 升序；dependency ID 去空白、去重后做 Ordinal 升序；省略不存在的 `workshopFileId`；UTF-8、无额外空白。共享 fixture 为 `test-fixtures/mod-sync/canonical-diff-v1.json`。

### 2.2 限制

| 项目 | 限制 |
|---|---:|
| descriptor 数量 | 64 |
| `id` 长度 | 128 字符 |
| `version` 长度 | 64 字符 |
| 单项 dependency 数量 | 16 |
| dependency ID 长度 | 128 字符 |
| `workshopFileId` | 1-20 位十进制且不为 0 |
| canonical inventory | 65536 UTF-8 bytes |
| pending join TTL | 15 分钟 |
| Workshop job 总超时 | 5 分钟 |
| AppID | `2868840` |

禁止空 ID、ASCII/C1 控制字符、重复 ID，以及保留前缀 `sts2_lan_connect`、`lan_connect.`、`sts2-lan-connect.`。`sts2_lan_connect` 本身永不进入 inventory 或禁用集合。

### 2.3 预检响应与 HTTP 错误

成功响应固定返回 `enabled`、`protocolVersion`、`gameVersion { host, local, exactMatch }`、四类差异、`canContinueRelaxed` 和 `hostInventoryAvailable`。Feature disabled 或旧 host 是 HTTP 200 降级响应，不是错误。

预检只复用既有错误码，不新增可探测私有 inventory 的旁路：

| HTTP | code | 条件 |
|---:|---|---|
| 400 | `invalid_request` | JSON/DTO/limit/protocol 非法 |
| 401 | `invalid_password` | 密码错误；不得返回 inventory 或差异 |
| 404 | `room_not_found` | 房间不存在或已过期 |
| 409 | `room_started` | 普通房间已开始 |
| 409 | `room_full` | 普通房间已满 |
| 410 | `room_closed` | 房间已关闭 |
| 429 | `rate_limited` | 命中 create/join 共用限流 |

游戏版本不一致返回 HTTP 200、`exactMatch=false`、`canContinueRelaxed=false`，并且四类 MOD 差异为空。密码校验和限流先于私有 inventory 差异计算。

## 3. 平台矩阵

| 平台/运行环境 | 自动 Workshop | 显示差异 | 手动入口 | 禁止行为 |
|---|---|---|---|---|
| Windows + SteamAPI | 是 | 是 | 是 | 任意 URL 下载、静默禁用 |
| macOS + SteamAPI | 是 | 是 | 是 | 任意 URL 下载、静默禁用 |
| Linux + SteamAPI | 是 | 是 | 是 | 任意 URL 下载、静默禁用 |
| Android | 否 | 是 | 是 | 虚假进度、调用 SteamAPI |
| 桌面非 Steam 启动 | 否 | 是 | 是 | 虚假进度、任意下载器 |
| SteamAPI 初始化失败 | 否 | 是 | 是 | 启动异常、虚假完成 |

所有平台在游戏版本不一致时优先进入版本硬拦截，不展示同步按钮。自动同步可用也不能把 `canContinueRelaxed=false` 改为 true。

## 4. 自动化测试名称清单

### 4.1 C# inventory 与纯差异（Phase 1）

- `Build_includes_loaded_gameplay_roots`
- `Build_excludes_unloaded_gameplay_mods`
- `Build_excludes_unrelated_non_gameplay_mods`
- `Build_includes_transitive_required_dependencies_only`
- `Build_terminates_dependency_cycles`
- `Build_deduplicates_diamond_dependencies`
- `Build_excludes_lan_connect_and_reserved_ids`
- `Build_reads_manifest_fields_without_parsing_id_version_display_names`
- `Resolver_supports_0_107_1_manifest_shape`
- `Resolver_supports_0_108_0_manifest_shape`
- `Resolver_supports_0_109_0_manifest_shape`
- `Resolver_rejects_ambiguous_runtime_members`
- `Canonicalize_trims_deduplicates_and_ordinal_sorts_ids`
- `Canonicalize_ordinal_sorts_dependency_ids`
- `Validate_accepts_exact_descriptor_and_payload_limits`
- `Validate_rejects_65_descriptors`
- `Validate_rejects_id_over_128_characters`
- `Validate_rejects_version_over_64_characters`
- `Validate_rejects_17_dependencies`
- `Validate_rejects_dependency_id_over_128_characters`
- `Validate_rejects_empty_duplicate_control_and_reserved_ids`
- `Validate_rejects_zero_signed_nondigit_and_21_digit_workshop_ids`
- `Validate_rejects_canonical_payload_over_65536_utf8_bytes`
- `Diff_matches_shared_canonical_fixture`
- `Diff_ignores_local_dependency_only_extras`
- `Diff_reports_only_local_gameplay_extras`
- `Diff_separates_missing_workshop_and_manual_mods`
- `Diff_reports_version_mismatch_without_claiming_install_success`
- `Diff_blocks_cross_game_version_before_mod_comparison`

### 4.2 TypeScript validator 与纯差异（Phase 1）

- `validator accepts exact descriptor count and canonical byte limits`
- `validator rejects non-array and 65-descriptor inventories`
- `validator rejects unknown descriptor fields`
- `validator rejects invalid role and source values`
- `validator rejects empty duplicate control and reserved ids`
- `validator rejects id and version length overflow`
- `validator rejects dependency count and id length overflow`
- `validator rejects dependency cycles without hanging`
- `validator rejects zero signed nondigit and 21-digit workshop ids`
- `validator preserves workshop ids as decimal strings without number coercion`
- `validator rejects canonical payload over 65536 UTF-8 bytes`
- `canonicalizer matches C# property and ordinal ordering`
- `diff matches shared canonical fixture`
- `diff computes even when STRICT_MOD_VERSION_CHECK is false`
- `diff excludes dependency-only local extras`
- `diff returns no mod details for cross-game-version preflight`

### 4.3 服务端私有预检（Phase 2）

- `createRoom stores host inventory privately`
- `createRoom accepts legacy hosts without inventory`
- `listRooms never exposes host inventory`
- `room create response never exposes host inventory`
- `peer snapshots gossip health metrics and chat never expose host inventory`
- `GET probe advertises mod sync protocol version 1 when enabled`
- `GET probe advertises disabled capability when feature is off`
- `POST mod-preflight does not increment players issue ticket or mutate room state`
- `POST mod-preflight validates password before returning differences`
- `POST mod-preflight shares create-join rate limiting`
- `POST mod-preflight rejects closed started full missing and expired rooms`
- `POST mod-preflight preserves saved-run reconnect joinability rules`
- `POST mod-preflight returns hostInventoryAvailable false for v0.5.0 host`
- `POST mod-preflight returns enabled false when feature is disabled`
- `POST mod-preflight hard-blocks different game versions in relaxed mode`
- `POST mod-preflight computes differences when strict mod checking is disabled`
- `POST mod-preflight logs counts and hash without inventory password or tokens`
- `legacy POST join works without preflight against v0.5.1 service`
- `v0.5.1 POST join keeps ticket relay and control behavior unchanged`

### 4.4 客户端预检协调（Phase 3）

- `Capabilities_missing_fields_resolve_to_protocol_zero_disabled`
- `Capabilities_v1_enabled_resolves_to_supported`
- `JoinCoordinator_preflights_before_requesting_join_ticket`
- `JoinCoordinator_falls_back_for_v0_5_0_service`
- `JoinCoordinator_falls_back_when_feature_is_disabled`
- `JoinCoordinator_hard_blocks_game_version_mismatch_before_mod_dialog`
- `JoinCoordinator_allows_confirmed_relaxed_continue_for_mod_differences`
- `JoinCoordinator_never_defaults_relaxed_continue_as_primary_action`
- `Invite_normal_join_and_saved_run_use_the_same_preflight_coordinator`
- `Password_room_restart_prompts_again_without_persisting_password`

### 4.5 Workshop provider 与禁用（Phase 4）

- `Job_transitions_pending_validating_subscribing_downloading_waiting_install_installed`
- `Job_waits_for_terminal_state_beyond_five_seconds`
- `Job_times_out_at_five_minutes`
- `Job_cancel_is_idempotent_and_reaches_canceled`
- `Job_retry_creates_a_fresh_attempt_after_failure`
- `Provider_holds_callbacks_for_the_full_job_lifetime`
- `Provider_rejects_metadata_for_app_id_other_than_2868840`
- `Provider_surfaces_real_title_and_publisher_before_consent`
- `Provider_never_downloads_from_host_service_or_arbitrary_url`
- `Provider_verifies_installed_manifest_id_matches_expected_id`
- `Provider_requires_repreflight_when_installed_version_differs_from_host`
- `Provider_returns_structured_unsupported_when_SteamAPI_is_unavailable`
- `Disable_selection_defaults_to_empty`
- `Disable_applier_requires_second_confirmation`
- `Disable_applier_never_disables_lan_connect_dependency_or_non_gameplay_mods`
- `Disable_applier_saves_settings_exactly_once_after_all_changes_succeed`
- `Disable_applier_rolls_back_partial_failure_and_surfaces_recovery`

### 4.6 UI 与 pending join（Phase 5）

- `Dialog_renders_all_nine_view_states`
- `Dialog_hides_sync_actions_for_game_version_mismatch`
- `Dialog_shows_manual_only_on_android_nonSteam_and_SteamAPI_failure`
- `Dialog_extra_gameplay_rows_start_unchecked`
- `Dialog_supports_cancel_retry_escape_keyboard_and_controller_focus`
- `Dialog_announces_progress_errors_and_restart_requirement`
- `Dialog_bounds_long_names_and_64_rows_without_overlap`
- `Dialog_matches_pixel_bounds_at_1280x720_1920x1080_2560x1440_and_4k`
- `Dialog_matches_android_portrait_and_landscape_bounds`
- `Pending_store_writes_atomically_without_password_or_tokens`
- `Pending_store_expires_after_fifteen_minutes`
- `Pending_store_ignores_and_clears_unknown_versions`
- `Pending_resume_restores_server_then_room_and_repreflights`
- `Pending_resume_prompts_password_rooms_again`
- `Pending_resume_clears_on_success_cancel_missing_room_expiry_and_server_change`
- `Pending_resume_reuses_single_flight_and_submenu_debounce`

### 4.7 安全、兼容与发布（Phase 6-7）

- `Ams_decision_audit_rejects_host_sidecar_map_as_local_preflight_inventory_source`
- `Native_provider_has_no_AutoModSubscriber_runtime_dependency`
- `Production_does_not_register_or_overwrite_AMS_ExternalDialogHandler`
- `Fuzz_validator_never_leaks_inventory_or_crashes_on_untrusted_json`
- `Concurrent_preflights_do_not_mutate_room_or_issue_tickets`
- `Package_contains_no_game_dll_steamworks_dll_or_unapproved_binary`
- `Package_contains_exact_v0_5_1_client_and_service_versions`
- `Package_preserves_v0_5_0_v0_4_0_and_v0_2_2_history_fixtures`
- `Release_outputs_are_reproducible_with_checksum_rsync_and_diff_qr`

## 5. 实机夹具与来源

### 5.1 Steam 与 Workshop

- AppID：`2868840`。
- 本机 Steam 配置确认有 2 个账号；账号 ID 不进入日志、文档或提交。
- Provider 的无害真实下载夹具：Steam Workshop `3747497501`（Regent FX Omnistar）。公开说明仅增加 Regent 视觉/音效，不改卡牌、遗物或数值；它仅用于 provider 的订阅、取消、失败、重试和 manifest 验证，不用于证明 gameplay inventory 过滤。
- 非 gameplay 忽略场景使用同一视觉夹具，并以其实际 manifest 的 `affects_gameplay` 值为准；若实际标记为 true，则改用另一个经 manifest 验证的纯外观条目，不能靠标题判断。
- 自动化差异使用共享 synthetic fixture；真实双客户端 gameplay 场景必须在执行前记录所选 Workshop 条目的实际 manifest、依赖和分支版本，不允许使用 LAN Connect 自身作为测试 MOD。

### 5.2 游戏版本

| 版本 | 来源 | Phase 0 证据 |
|---|---|---|
| 0.107.1 | Steam `public`，build `23811903`，macOS manifest `8653035385353091849` | Phase 8 官方 depot 下载完成；`release_info.json` 为 `v0.107.1` |
| 0.108.0 | 历史 Steam `public-beta`，build `24032229`，macOS manifest `1977841934321910790` | 2026-07-17 10:15 曾由官方 CDN 安装并启动，但早于本分支且文件已被覆盖；当前 appinfo 无分支指向该 build，两个已保存账号的精确重试均返回 manifest ACL `Access Denied`，仍待可复核的合法产物 |
| 0.109.0 | Steam `public-beta`，build `24251656`，macOS manifest `7169427731078769081` | 当前真实安装 `release_info.json` 为 `v0.109.0`，已完成最终候选加载 |

切换分支后先复制完整游戏目录到独立只读测试目录，并记录 `release_info.json`、`sts2.dll` SHA-256 和 Steam build ID。不得把游戏文件提交或放入发布包。

### 5.3 双客户端

- 本机账号条件已满足（2 个 Steam 账号），但同机不能证明双客户端并发网络场景。
- Phase 8 需要第二台 Steam 桌面客户端；每个场景保存双方 `release_info.json` 摘要、LAN Connect 版本、MOD manifest 摘要和脱敏日志。
- 在第二客户端可用前允许完成纯函数、服务端、fake provider、UI 和打包 gate，但不得宣称 Phase 8、v0.5.1 或正式发布完成。

## 6. Phase 0 基线证据

- `origin/main` 与本地基线均为 `66ceb7201277bfbe36fb9a91c9992e114972413f`。
- 分支为 `feat/mod-sync-0.5.1`，受保护未跟踪目录/文件未加入索引。
- lobby-service：`npm run check` 通过；`npm run test` 395/395 通过。
- xUnit：576 通过，1 个既有双客户端原型测试跳过。
- GdUnit：219/219 通过。
- PR #38 保持开放，审查结论为 `CHANGES_REQUESTED`；未 merge、未 cherry-pick。

Phase 0 gate 只证明范围、协议、自动化 fixture 和可获得的实机来源已明确。它不替代 Phase 4 的真实 Workshop 操作，也不替代 Phase 8 的三版本、双客户端和 Android 验收。
