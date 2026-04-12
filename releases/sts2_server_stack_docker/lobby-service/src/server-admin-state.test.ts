import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { ServerAdminStateStore } from "./server-admin-state.js";

function createTempStatePath() {
  const directory = mkdtempSync(join(tmpdir(), "sts2-server-admin-state-"));
  return {
    directory,
    path: join(directory, "server-admin.json"),
  };
}

test("server admin state normalizes announcement records on load", () => {
  const temp = createTempStatePath();
  try {
    writeFileSync(
      temp.path,
      JSON.stringify({
        displayName: "测试服务器",
        announcements: [
          {
            id: "  notice-1  ",
            type: "warning",
            title: "  维护通知  ",
            dateLabel: " 2026-03-22 ",
            body: "  服务器将在今晚进行维护。  ",
            enabled: true,
          },
          {
            id: "notice-1",
            type: "unknown",
            title: "",
            body: "只有正文时也应保留",
            enabled: false,
          },
          {
            id: "empty",
            type: "info",
            title: "   ",
            body: "   ",
            enabled: true,
          },
        ],
      }),
      "utf8",
    );

    const store = new ServerAdminStateStore(temp.path);
    const settings = store.getSettingsView();

    assert.equal(settings.announcements.length, 2);
    assert.deepEqual(settings.announcements[0], {
      id: "notice-1",
      type: "warning",
      title: "维护通知",
      dateLabel: "2026-03-22",
      body: "服务器将在今晚进行维护。",
      enabled: true,
    });
    assert.equal(settings.announcements[1]?.id, "notice-1-1");
    assert.equal(settings.announcements[1]?.type, "info");
    assert.equal(settings.announcements[1]?.title, "只有正文时也应保留");
    assert.equal(settings.announcements[1]?.enabled, false);
    assert.deepEqual(store.getPublicAnnouncements(), [settings.announcements[0]]);
  } finally {
    rmSync(temp.directory, { recursive: true, force: true });
  }
});

test("server admin state persists announcement updates across reloads", () => {
  const temp = createTempStatePath();
  try {
    const store = new ServerAdminStateStore(temp.path);
    store.updateSettings({
      displayName: "公开服",
      publicListingEnabled: true,
      announcements: [
        {
          id: "update-1",
          type: "update",
          title: "版本更新",
          dateLabel: "2026-03-22",
          body: "大厅已支持顶部公告轮播。",
          enabled: true,
        },
        {
          id: "event-1",
          type: "event",
          title: "周末活动",
          dateLabel: "2026-03-23",
          body: "今晚 8 点开启多人速通活动。",
          enabled: false,
        },
      ],
    });

    const reloaded = new ServerAdminStateStore(temp.path);
    const settings = reloaded.getSettingsView();

    assert.equal(settings.announcements.length, 2);
    assert.equal(settings.announcements[0]?.id, "update-1");
    assert.equal(settings.announcements[1]?.type, "event");
    assert.deepEqual(reloaded.getPublicAnnouncements(), [settings.announcements[0]]);
  } finally {
    rmSync(temp.directory, { recursive: true, force: true });
  }
});
