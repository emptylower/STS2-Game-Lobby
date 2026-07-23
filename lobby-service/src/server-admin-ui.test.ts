import assert from "node:assert/strict";
import test from "node:test";
import vm from "node:vm";
import {
  beginServerAdminSingleFlight,
  buildServerAdminRequestInit,
  calculateServerAdminRejectionRate,
  isCurrentServerAdminRequest,
  mergeServerAdminPollSnapshot,
  releaseServerAdminSingleFlight,
  renderServerAdminPage,
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
