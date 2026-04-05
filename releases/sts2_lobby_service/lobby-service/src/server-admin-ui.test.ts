import assert from "node:assert/strict";
import test from "node:test";
import { renderServerAdminPage } from "./server-admin-ui.js";

test("server admin page preserves unsaved drafts during poll refresh", () => {
  const html = renderServerAdminPage();

  assert.match(html, /if \(source === "poll" && draftDirtyRef\.current\)/);
  assert.match(html, /自动刷新不会覆盖当前草稿/);
  assert.match(html, /重新加载会覆盖左侧设置和公告草稿/);
});
