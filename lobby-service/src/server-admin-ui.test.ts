import assert from "node:assert/strict";
import test from "node:test";
import vm from "node:vm";
import {
  beginServerAdminSingleFlight,
  buildServerAdminAiConfigPatch,
  buildServerAdminAiTestRequest,
  buildServerAdminRequestInit,
  calculateServerAdminRejectionRate,
  isCurrentServerAdminRequest,
  mergeServerAdminPollSnapshot,
  releaseServerAdminSingleFlight,
  renderServerAdminPage,
  resolveServerAdminAiCredentialAlert,
  resolveServerAdminChatControlState,
} from "./server-admin-ui.js";

test("server admin page inline script parses without syntax errors", () => {
  const html = renderServerAdminPage("0.5.2");
  const match = html.match(/<script>([\s\S]*?)<\/script>/);
  assert.ok(match, "inline bootstrap script should be present");
  new vm.Script(match[1] as string);
});

test("server admin request options attach CSRF only to unsafe same-origin requests", () => {
  assert.deepEqual(buildServerAdminRequestInit("GET", "csrf-token"), {
    method: "GET",
    credentials: "same-origin",
    headers: {},
  });
  assert.deepEqual(buildServerAdminRequestInit("PATCH", "csrf-token", {
    headers: { "content-type": "application/json" },
    body: "{}",
  }), {
    method: "PATCH",
    credentials: "same-origin",
    headers: {
      "content-type": "application/json",
      "x-csrf-token": "csrf-token",
    },
    body: "{}",
  });
  assert.deepEqual(buildServerAdminRequestInit("POST", null), {
    method: "POST",
    credentials: "same-origin",
    headers: {},
  });
});

test("chat feature dependencies disable controls without mutating persisted values", () => {
  const persisted = {
    serverChatEnabled: false,
    richContentEnabled: false,
    emojiEnabled: true,
    itemRefsEnabled: true,
    roomChatV2Enabled: false,
    roomCombatRefsEnabled: true,
  };
  const before = structuredClone(persisted);
  assert.deepEqual(resolveServerAdminChatControlState(persisted), {
    serverChatEnabled: false,
    richContentEnabled: false,
    emojiEnabled: true,
    itemRefsEnabled: true,
    roomChatV2Enabled: false,
    roomCombatRefsEnabled: true,
  });
  assert.deepEqual(persisted, before);

  assert.deepEqual(resolveServerAdminChatControlState({
    ...persisted,
    richContentEnabled: true,
    roomChatV2Enabled: true,
  }), {
    serverChatEnabled: false,
    richContentEnabled: false,
    emojiEnabled: false,
    itemRefsEnabled: false,
    roomChatV2Enabled: false,
    roomCombatRefsEnabled: false,
  });
});

test("dirty poll merge refreshes runtime fields while preserving every editable draft", () => {
  const current = {
    displayName: "draft name",
    publicListingEnabled: false,
    modSyncEnabled: false,
    bandwidthCapacityMbps: 12,
    announcements: [{ id: "draft" }],
    chatFeatures: { richContentEnabled: false, emojiEnabled: true },
    metrics: { historyEpoch: 1 },
    serverFeatures: { richContentVersion: 0 },
  };
  const next = {
    displayName: "remote name",
    publicListingEnabled: true,
    modSyncEnabled: true,
    bandwidthCapacityMbps: 99,
    announcements: [{ id: "remote" }],
    chatFeatures: { richContentEnabled: true, emojiEnabled: false },
    metrics: { historyEpoch: 2 },
    serverFeatures: { richContentVersion: 1 },
    roomFeatures: { combatRefVersion: 1 },
  };
  assert.deepEqual(mergeServerAdminPollSnapshot(current, next, true), {
    ...next,
    displayName: current.displayName,
    publicListingEnabled: current.publicListingEnabled,
    modSyncEnabled: current.modSyncEnabled,
    bandwidthCapacityMbps: current.bandwidthCapacityMbps,
    announcements: current.announcements,
    chatFeatures: current.chatFeatures,
  });
  assert.deepEqual(mergeServerAdminPollSnapshot(current, next, false), next);
});

test("single-flight guard rejects a second synchronous trigger", () => {
  const ref = { current: false };
  assert.equal(beginServerAdminSingleFlight(ref), true);
  assert.equal(beginServerAdminSingleFlight(ref), false);
  ref.current = false;
  assert.equal(beginServerAdminSingleFlight(ref), true);
});

test("shared mutation gate excludes interleaved save clear and logout operations", async () => {
  const ref = { current: false };
  let releaseFirst!: () => void;
  const firstBarrier = new Promise<void>((resolve) => {
    releaseFirst = resolve;
  });
  const calls: string[] = [];

  async function runMutation(name: string, barrier: Promise<void>) {
    if (!beginServerAdminSingleFlight(ref)) return false;
    calls.push(name);
    try {
      await barrier;
      return true;
    } finally {
      releaseServerAdminSingleFlight(ref);
    }
  }

  const save = runMutation("save", firstBarrier);
  assert.equal(await runMutation("clear", Promise.resolve()), false);
  assert.equal(await runMutation("logout", Promise.resolve()), false);
  assert.deepEqual(calls, ["save"]);

  releaseFirst();
  assert.equal(await save, true);
  assert.equal(await runMutation("clear", Promise.resolve()), true);
  assert.deepEqual(calls, ["save", "clear"]);
});

test("request generation and sequence reject stale poll and pre-logout responses", () => {
  assert.equal(isCurrentServerAdminRequest(2, 2, 8, 8), true);
  assert.equal(isCurrentServerAdminRequest(1, 2, 8, 8), false);
  assert.equal(isCurrentServerAdminRequest(2, 2, 7, 8), false);
  assert.equal(isCurrentServerAdminRequest(2, 2), true);
});

test("chat rejection rates include accepted and rejected messages", () => {
  assert.equal(calculateServerAdminRejectionRate(8, 2), 0.2);
  assert.equal(calculateServerAdminRejectionRate(0, 0), 0);
  assert.equal(calculateServerAdminRejectionRate(undefined, 3), 1);
});

test("server admin page preserves unsaved drafts during poll refresh", () => {
  const html = renderServerAdminPage("0.5.0");

  assert.match(html, /if \(source === "poll" && draftDirtyRef\.current\)/);
  assert.match(html, /自动刷新不会覆盖当前草稿/);
  assert.match(html, /重新加载会覆盖左侧设置和公告草稿/);
  assert.match(html, /Lobby Service v0\.5\.0/);
  assert.match(html, /mergePollSnapshot\(previous, next, draftDirtyRef\.current\)/);
  assert.match(html, /settingsRequestSeqRef/);
  assert.match(html, /sessionGenerationRef/);
});

test("server admin page renders six nested persisted switches and effective versions", () => {
  const html = renderServerAdminPage("0.5.0");
  for (const key of [
    "serverChatEnabled",
    "richContentEnabled",
    "emojiEnabled",
    "itemRefsEnabled",
    "roomChatV2Enabled",
    "roomCombatRefsEnabled",
  ]) {
    assert.match(html, new RegExp(`name: \\["chatFeatures", "${key}"\\]`));
  }
  assert.match(html, /disabled: chatControlState\.emojiEnabled/);
  assert.match(html, /disabled: chatControlState\.itemRefsEnabled/);
  assert.match(html, /disabled: chatControlState\.roomCombatRefsEnabled/);
  assert.match(html, /服务器频道有效版本/);
  assert.match(html, /房间聊天有效版本/);
  assert.match(html, /settings\.serverFeatures/);
  assert.match(html, /settings\.roomFeatures/);
});

test("server admin page renders a persisted mod sync switch enabled by default", () => {
  const html = renderServerAdminPage("0.5.1");
  assert.match(html, /name: "modSyncEnabled"/);
  assert.match(html, /label: "加入前 MOD 兼容预检与 Workshop 自动同步"/);
  assert.match(html, /modSyncEnabled: next\.modSyncEnabled !== false/);
});

test("server admin page renders all chat metrics, rejection rates, and guarded clear action", () => {
  const html = renderServerAdminPage("0.5.0");
  for (const key of [
    "serverConnectionCount",
    "roomConnectionCount",
    "serverRetainedHistoryCount",
    "historyEpoch",
    "serverAcceptedMessages",
    "serverRejectedMessages",
    "roomAcceptedMessages",
    "roomRejectedMessages",
  ]) {
    assert.match(html, new RegExp(`settings\\.metrics\\.${key}`));
  }
  assert.match(html, /服务器拒绝率/);
  assert.match(html, /房间拒绝率/);
  assert.match(html, /Modal\.confirm/);
  assert.match(html, /\/server-admin\/chat\/clear-history/);
  assert.match(html, /async function executeClearHistory\(\) \{[\s\S]*if \(!beginAdminMutation\(\)\) return/);
  assert.match(html, /async function handleSave\(values\) \{[\s\S]*if \(!beginAdminMutation\(\)\) return/);
  assert.match(html, /async function handleLogout\(\) \{[\s\S]*if \(!beginAdminMutation\(\)\) return/);
  assert.match(html, /disabled: adminMutationInFlight/);
  assert.match(html, /clearLoading/);
  assert.match(html, /聊天历史已清空/);
  assert.match(html, /清空聊天历史失败/);
});

test("server admin page centralizes CSRF requests and 401/403 handling", () => {
  const html = renderServerAdminPage("0.5.0");
  assert.match(html, /credentials: "same-origin"/);
  assert.match(html, /headers\["x-csrf-token"\] = csrfToken/);
  assert.match(html, /response\.status === 204 \? null : payload/);
  assert.match(html, /this\.status = status/);
  assert.match(html, /this\.code = code/);
  assert.match(html, /error\.status === 401[\s\S]*invalidateSession\(\)/);
  assert.match(html, /error\.status === 403[\s\S]*安全令牌已失效/);
  assert.match(html, /performAdminRequest\("\/server-admin\/login"/);
  assert.match(html, /message\.error\(error\.message \|\| "登录失败"\)/);
  assert.match(html, /退出登录失败，本地登录状态已清除/);
  assert.match(
    html,
    /async function handleLogout\(\)[\s\S]*await performAdminRequest\([\s\S]*finally \{[\s\S]*clearLocalAdminState\(\)/,
  );
});

test("server admin page renders the service update center and protected update actions", () => {
  const html = renderServerAdminPage("0.5.0");

  assert.match(html, /服务端版本/);
  assert.match(html, /Lobby Service v0\.5\.0/);
  assert.match(html, /\/server-admin\/update\/check/);
  assert.match(html, /\/server-admin\/update\/install/);
  assert.match(html, /更新预检查未通过/);
  assert.match(html, /一键更新/);
  assert.match(html, /watchServiceRestart/);
});

test("server admin page uses dashboard metrics and button toggles instead of a status table", () => {
  const html = renderServerAdminPage("0.5.2");
  assert.match(html, /className: "metric-grid"/);
  assert.match(html, /className: "detail-panels"/);
  assert.match(html, /aria-label": "服务器运行仪表盘"/);
  assert.match(html, /function ToggleButton/);
  assert.match(html, /"aria-pressed": enabled/);
  assert.doesNotMatch(html, /h\(Descriptions/);
});

test("server admin page maps node-network status from peerRuntimeState", () => {
  const html = renderServerAdminPage("0.5.0");

  assert.match(html, /switch \(settings\.peerRuntimeState\)/);
  assert.match(html, /节点网络未启用/);
  assert.match(html, /节点网络未配置/);
  assert.match(html, /仅私有可见/);
  assert.match(html, /正在加入节点网络/);
  assert.match(html, /已加入节点网络/);
});

test("server admin page mobile layout wraps actions and collapses dashboard grids", () => {
  const html = renderServerAdminPage("0.5.0");

  assert.match(html, /className: "settings-actions"/);
  assert.match(html, /className: "announcement-actions"/);
  assert.match(html, /@media \(max-width: 640px\)[\s\S]*\.console-card \.ant-card-body \{[\s\S]*padding: 16px/);
  assert.match(html, /\.console-card \.ant-card-head-wrapper \{[\s\S]*flex-wrap: wrap/);
  assert.match(html, /\.console-card \.ant-card-extra \{[\s\S]*margin-inline-start: 0/);
  assert.match(html, /\.settings-actions,[\s\S]*\.announcement-actions \{[\s\S]*flex-wrap: wrap[\s\S]*width: 100%/);
  assert.match(html, /\.settings-actions \.ant-btn,[\s\S]*white-space: normal[\s\S]*height: auto/);
  assert.match(html, /@media \(max-width: 640px\)[\s\S]*\.metric-grid,[\s\S]*\.detail-panels \{[\s\S]*grid-template-columns: 1fr/);
  assert.match(html, /\.update-layout \{[\s\S]*grid-template-columns: 1fr/);
  assert.match(html, /\.toggle-button \{[\s\S]*width: 100%/);
});

test("admin page renders the sensitive filter toggle", () => {
  const html = renderServerAdminPage("0.5.4");
  assert.ok(html.includes("敏感词过滤"));
  assert.ok(html.includes("sensitiveFilterEnabled"));
});

test("ai config patch keeps the saved key unless a new draft is provided", () => {
  const values = {
    enabled: true,
    protocol: "anthropic_messages",
    endpoint: "  https://api.anthropic.com/v1/messages  ",
    model: "  claude-example  ",
    allowPrivateNetwork: true,
    jsonFallbackEnabled: false,
  };
  assert.deepEqual(buildServerAdminAiConfigPatch(values, ""), {
    enabled: true,
    protocol: "anthropic_messages",
    endpoint: "https://api.anthropic.com/v1/messages",
    model: "claude-example",
    allowPrivateNetwork: true,
    jsonFallbackEnabled: false,
    apiKeyAction: "keep",
  });
  assert.deepEqual(buildServerAdminAiConfigPatch(values, "   "), {
    ...buildServerAdminAiConfigPatch(values, ""),
    apiKeyAction: "keep",
  });
  assert.deepEqual(buildServerAdminAiConfigPatch(values, "  new-secret  "), {
    ...buildServerAdminAiConfigPatch(values, ""),
    apiKeyAction: "replace",
    apiKey: "new-secret",
  });
  // The patch builder never emits a clear action or an empty-string key;
  // clearing is only possible through the dedicated { apiKeyAction: "clear" } request.
  const patch = buildServerAdminAiConfigPatch(values, "");
  assert.notEqual(patch.apiKeyAction, "clear");
  assert.equal(Object.hasOwn(patch, "apiKey"), false);
});

test("ai test request mirrors the form without persisting fields and omits a blank key", () => {
  const values = {
    enabled: true,
    protocol: "openai_chat_completions",
    endpoint: "https://api.openai.com/v1/chat/completions",
    model: "gpt-example",
    allowPrivateNetwork: false,
    jsonFallbackEnabled: true,
  };
  const blank = buildServerAdminAiTestRequest(values, "");
  assert.deepEqual(blank, {
    protocol: "openai_chat_completions",
    endpoint: "https://api.openai.com/v1/chat/completions",
    model: "gpt-example",
    allowPrivateNetwork: false,
    jsonFallbackEnabled: true,
  });
  assert.equal(Object.hasOwn(blank, "apiKey"), false);
  assert.equal(Object.hasOwn(blank, "enabled"), false);
  assert.equal(Object.hasOwn(blank, "apiKeyAction"), false);

  const withKey = buildServerAdminAiTestRequest(values, " temporary-key ");
  assert.equal(withKey.apiKey, "temporary-key");
});

test("ai credential alert maps each non-ready credential status", () => {
  assert.equal(resolveServerAdminAiCredentialAlert({ credentialStatus: "ready" }), null);
  assert.equal(
    resolveServerAdminAiCredentialAlert({ credentialStatus: "missing_master_key" })?.type,
    "warning",
  );
  assert.match(
    resolveServerAdminAiCredentialAlert({ credentialStatus: "missing_master_key" })?.description ?? "",
    /AI_MODERATION_CREDENTIAL_KEY/,
  );
  assert.equal(
    resolveServerAdminAiCredentialAlert({ credentialStatus: "decrypt_error" })?.type,
    "error",
  );
  assert.match(
    resolveServerAdminAiCredentialAlert({ credentialStatus: "decrypt_error" })?.description ?? "",
    /重新填写 API Key/,
  );
  assert.equal(
    resolveServerAdminAiCredentialAlert({ credentialStatus: "missing_api_key" })?.type,
    "info",
  );
  assert.equal(resolveServerAdminAiCredentialAlert(null)?.type, "info");
});

test("server admin page renders the AI moderation console tab and config controls", () => {
  const html = renderServerAdminPage("0.5.4");
  assert.match(html, /\{ key: "moderation", label: "AI 审核", icon: ICONS\.shield/);
  assert.match(html, /AI 语义审核/);
  assert.match(html, /启用 AI 语义审核/);
  assert.match(html, /name: "protocol"/);
  assert.match(html, /name: "endpoint"/);
  assert.match(html, /name: "model"/);
  assert.match(html, /name: "allowPrivateNetwork"/);
  assert.match(html, /name: "jsonFallbackEnabled"/);
  assert.match(html, /openai_responses/);
  assert.match(html, /openai_chat_completions/);
  assert.match(html, /anthropic_messages/);
  assert.match(html, /Input\.Password/);
  assert.match(html, /保存配置/);
  assert.match(html, /测试连接/);
  assert.match(html, /清除 API Key/);
  assert.match(html, /允许访问私网模型/);
  assert.match(html, /兼容提示词 JSON 回退/);
});

test("server admin page exposes permanent blocklist as a top-level management tab", () => {
  const html = renderServerAdminPage("0.5.4");
  assert.match(html, /\{ key: "blocklist", label: "永久黑名单", icon: ICONS\.ban/);
  assert.match(html, /activeTab === "blocklist"/);
  assert.match(html, /h\(PermanentBlocklistPanel, \{/);
  assert.match(html, /data-admin-panel": "permanent-blocklist"/);
  assert.match(html, /永久黑名单立即生效/);
  assert.match(html, /当前没有永久黑名单规则。请在 AI 审核页拒绝候选后再查看。/);
  assert.match(html, /语境签名/);
  assert.match(html, /作用范围 全部信息流/);
  assert.match(html, /撤销这条永久黑名单规则？/);
  assert.match(html, /moderationStatus\.reviews\.blockRules/);
  assert.match(html, /activeTab !== "moderation" && activeTab !== "blocklist"/);
});

test("server admin page wires AI moderation endpoints through the CSRF request wrapper", () => {
  const html = renderServerAdminPage("0.5.4");
  assert.match(html, /request\("\/server-admin\/moderation\/ai", undefined, generation\)/);
  assert.match(html, /request\("\/server-admin\/moderation\/ai", \{\s*method: "PATCH"/);
  assert.match(html, /request\("\/server-admin\/moderation\/ai\/test", \{\s*method: "POST"/);
  assert.match(html, /"\/server-admin\/moderation\/reviews\?" \+ params\.toString\(\)/);
  assert.match(html, /"\/server-admin\/moderation\/reviews\/" \+ encodeURIComponent\(id\)/);
  assert.match(html, /"\/server-admin\/moderation\/allowlist\?" \+ params\.toString\(\)/);
  assert.match(html, /"\/server-admin\/moderation\/allowlist\/" \+ encodeURIComponent\(item\.id\), \{\s*method: "DELETE"/);
  assert.match(html, /"\/server-admin\/moderation\/blocklist\?" \+ params\.toString\(\)/);
  assert.match(html, /"\/server-admin\/moderation\/blocklist\/" \+ encodeURIComponent\(item\.id\), \{\s*method: "DELETE"/);
  // All AI moderation mutations share the admin mutation gate.
  assert.match(html, /async function handleAiSave\(values\) \{\s*if \(!beginAdminMutation\(\)\) return/);
  assert.match(html, /async function handleAiTest\(\) \{\s*if \(!beginAdminMutation\(\)\) return/);
  assert.match(html, /async function executeClearApiKey\(\) \{\s*if \(!beginAdminMutation\(\)\) return/);
  assert.match(html, /async function executeApprove\(\) \{\s*if \(!approveTarget \|\| !beginAdminMutation\(\)\) return/);
  assert.match(html, /async function executeReject\(\) \{\s*if \(!rejectTarget \|\| !beginAdminMutation\(\)\) return/);
  assert.match(html, /async function executeRevokeAllowRule\(item\) \{\s*if \(!item \|\| !beginAdminMutation\(\)\) return/);
  assert.match(html, /async function executeRevokeBlockRule\(item\) \{\s*if \(!item \|\| !beginAdminMutation\(\)\) return/);
});

test("server admin page never reads back a saved API key and keeps keep/replace/clear semantics", () => {
  const html = renderServerAdminPage("0.5.4");
  // The key field is fed only from the local draft state, never from GET data.
  assert.match(html, /value: apiKeyDraft/);
  assert.doesNotMatch(html, /config\.apiKey\b/);
  assert.match(html, /apiKeyAction: "keep"/);
  assert.match(html, /apiKeyAction = "replace"/);
  assert.match(html, /body: JSON\.stringify\(\{ enabled: false, apiKeyAction: "clear" \}\)/);
  assert.match(html, /留空保存将保持现有 Key/);
  assert.match(html, /清除后会自动停用 AI 审核/);
  // Blank drafts must not overwrite the saved key.
  assert.match(html, /if \(draft\) \{\s*patch\.apiKeyAction = "replace";\s*patch\.apiKey = draft;\s*\}/);
});

test("server admin page keeps unsaved AI form drafts during polling", () => {
  const html = renderServerAdminPage("0.5.4");
  assert.match(html, /if \(!status \|\| aiDirtyRef\.current\) return/);
  assert.match(html, /onValuesChange: function \(\) \{ markAiDirty\(\); \}/);
  assert.match(html, /自动刷新不会覆盖当前表单内容/);
  assert.match(html, /if \(source === "poll" && adminMutationInFlightRef\.current\)/);
  assert.match(html, /request\("\/server-admin\/moderation\/ai", undefined, generation\)/);
});

test("server admin page renders AI runtime health, caches and sanitized recent errors", () => {
  const html = renderServerAdminPage("0.5.4");
  for (const marker of [
    "health.active",
    "health.queued",
    "health.averageLatencyMs",
    "health.started",
    "health.allowed",
    "health.blocked",
    "health.degraded",
    "health.exactCacheHits",
    "health.contextCacheHits",
    "health.permanentAllowHits",
    "health.permanentBlockHits",
    "health.circuitState",
    "health.circuitOpenUntil",
    "health.recentErrors",
    "cache.exactEntries",
    "cache.contextEntries",
    "reviewStats.pendingReviews",
    "reviewStats.allowRules",
    "reviewStats.blockRules",
  ]) {
    assert.ok(html.includes(marker), `missing marker ${marker}`);
  }
  assert.match(html, /熔断中/);
  assert.match(html, /最近错误（不含聊天原文与密钥）/);
  // Recent errors render only time, code and optional HTTP status.
  assert.match(html, /item\.code \|\| "unknown"/);
  assert.match(html, /item\.statusCode/);
  assert.match(html, /formatDateTime\(item\.at\)/);
});

test("server admin page renders review moderation flows with explicit scope confirmation", () => {
  const html = renderServerAdminPage("0.5.4");
  assert.match(html, /人工复审候选/);
  assert.match(html, /永久白名单/);
  assert.match(html, /永久黑名单/);
  assert.match(html, /批准复审候选/);
  assert.match(html, /该规范化词将在聊天、房间名、用户名和角色名中全局永久放行/);
  assert.match(html, /只放行相同信息类型中，相同规范化词与相同规范化语境的内容/);
  // Context scope is the default recommendation, term is never preselected.
  assert.match(html, /setApproveScope\("context"\)/);
  assert.match(html, /h\(Radio, \{ value: "context" \}, "仅此语境（推荐）"\)/);
  assert.match(html, /h\(Radio, \{ value: "term" \}, "整个词"\)/);
  assert.match(html, /body: JSON\.stringify\(\{\s*scope: approveScope/);
  assert.match(html, /拒绝复审候选/);
  assert.match(html, /整个词：全局永久拦截/);
  assert.match(html, /仅此语境：限定永久拦截/);
  assert.match(html, /scope: rejectScope/);
  assert.match(html, /已拒绝并将当前语境加入永久黑名单/);
  assert.match(html, /撤销这条白名单规则？/);
  assert.match(html, /撤销这条黑名单规则？/);
  assert.match(html, /证据已处理或已过期，无法再查看/);
  assert.match(html, /reviewDetail\.data\.evidence\.message/);
  assert.match(html, /reviewDetail\.data\.evidence\.context/);
  // Cursor pagination is wired for both lists.
  assert.match(html, /params\.set\("cursor", cursor\)/);
  assert.match(html, /setReviewNextCursor\(data && data\.nextCursor/);
  assert.match(html, /setAllowNextCursor\(data && data\.nextCursor/);
  assert.match(html, /setBlockNextCursor\(data && data\.nextCursor/);
  assert.match(html, /setReviewCursor\(cursor \|\| null\)/);
  assert.match(html, /reviewCursorStack\.concat\(\[reviewCursor \|\| ""\]\)/);
  assert.match(html, /setAllowCursor\(cursor \|\| null\)/);
  assert.match(html, /allowCursorStack\.concat\(\[allowCursor \|\| ""\]\)/);
  assert.match(html, /blockCursorStack\.concat\(\[blockCursor \|\| ""\]\)/);
});

test("server admin page distinguishes test success and provider failure without persisting", () => {
  const html = renderServerAdminPage("0.5.4");
  assert.match(html, /测试连接成功/);
  assert.match(html, /测试连接失败/);
  assert.match(html, /structuredModeLabel\(testResult\.structuredMode\)/);
  assert.match(html, /testResult\.latencyMs/);
  assert.match(html, /测试不会保存配置或临时 Key/);
  // The test path never issues a PATCH.
  const testFn = html.match(/async function handleAiTest\(\) \{[\s\S]*?\n          \}/);
  assert.ok(testFn, "handleAiTest should be present");
  assert.doesNotMatch(testFn[0], /method: "PATCH"/);
});
