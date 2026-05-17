import assert from "node:assert/strict";
import test from "node:test";
import { renderServerAdminPage } from "./server-admin-ui.js";

test("server admin page preserves unsaved drafts during poll refresh", () => {
  const html = renderServerAdminPage("0.4.0");

  assert.match(html, /if \(source === "poll" && draftDirtyRef\.current\)/);
  assert.match(html, /自动刷新不会覆盖当前草稿/);
  assert.match(html, /重新加载会覆盖左侧设置和公告草稿/);
  assert.match(html, /Lobby Service v0\.4\.0/);
});

test("server admin page shows server version in current status block", () => {
  const html = renderServerAdminPage("0.4.0");

  assert.match(html, /title: "当前状态"[\s\S]*label: "服务器版本"[\s\S]*serviceVersionLabel/);
  assert.match(html, /Lobby Service v0\.4\.0/);
});

test("server admin page maps node-network status from peerRuntimeState", () => {
  const html = renderServerAdminPage("0.4.0");

  assert.match(html, /switch \(settings\.peerRuntimeState\)/);
  assert.match(html, /节点网络未启用/);
  assert.match(html, /节点网络未配置/);
  assert.match(html, /仅私有可见/);
  assert.match(html, /正在加入节点网络/);
  assert.match(html, /已加入节点网络/);
});
