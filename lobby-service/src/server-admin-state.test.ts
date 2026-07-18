import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { ServerAdminStateStore } from "./server-admin-state.js";
import type { ChatFeatureGovernance } from "./chat/feature-resolver.js";

const chatDefaults: ChatFeatureGovernance = {
  serverChatEnabled: false,
  richContentEnabled: true,
  emojiEnabled: true,
  itemRefsEnabled: true,
  roomChatV2Enabled: true,
  roomCombatRefsEnabled: true,
};

function createStore(
  path: string,
  features: ChatFeatureGovernance = chatDefaults,
  modSyncEnabledDefault = true,
) {
  return new ServerAdminStateStore(path, {
    publicListingEnabledDefault: true,
    chatFeaturesDefault: features,
    modSyncEnabledDefault,
  });
}

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

    const store = createStore(temp.path);
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
    const store = createStore(temp.path);
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

    const reloaded = createStore(temp.path);
    const settings = reloaded.getSettingsView();

    assert.equal(settings.announcements.length, 2);
    assert.equal(settings.announcements[0]?.id, "update-1");
    assert.equal(settings.announcements[1]?.type, "event");
    assert.deepEqual(reloaded.getPublicAnnouncements(), [settings.announcements[0]]);
  } finally {
    rmSync(temp.directory, { recursive: true, force: true });
  }
});

test("no state file uses all environment chat defaults", () => {
  const temp = createTempStatePath();
  try {
    assert.deepEqual(createStore(temp.path).getState().chatFeatures, chatDefaults);
  } finally {
    rmSync(temp.directory, { recursive: true, force: true });
  }
});

test("no state file and legacy state inherit the configured mod sync default", () => {
  const temp = createTempStatePath();
  try {
    assert.equal(createStore(temp.path, chatDefaults, true).getState().modSyncEnabled, true);
    writeFileSync(temp.path, JSON.stringify({ displayName: "Legacy" }), "utf8");
    assert.equal(createStore(temp.path, chatDefaults, false).getState().modSyncEnabled, false);
  } finally {
    rmSync(temp.directory, { recursive: true, force: true });
  }
});

test("server admin state persists mod sync updates across reloads", () => {
  const temp = createTempStatePath();
  try {
    createStore(temp.path).updateSettings({ modSyncEnabled: false });
    assert.equal(createStore(temp.path).getSettingsView().modSyncEnabled, false);
  } finally {
    rmSync(temp.directory, { recursive: true, force: true });
  }
});

test("legacy and partial chat state inherit defaults per key without losing existing fields", () => {
  const temp = createTempStatePath();
  try {
    writeFileSync(temp.path, JSON.stringify({
      displayName: "Legacy",
      publicListingEnabled: false,
      bandwidthCapacityMbps: 88,
      announcements: [{ title: "Keep", body: "Keep body" }],
      extraMetadata: { region: "test" },
      chatFeatures: {
        serverChatEnabled: true,
        richContentEnabled: "wrong",
        emojiEnabled: false,
      },
    }), "utf8");
    const state = createStore(temp.path).getState();

    assert.equal(state.displayName, "Legacy");
    assert.equal(state.publicListingEnabled, false);
    assert.equal(state.bandwidthCapacityMbps, 88);
    assert.equal(state.announcements.length, 1);
    assert.deepEqual(state.extraMetadata, { region: "test" });
    assert.deepEqual(state.chatFeatures, {
      ...chatDefaults,
      serverChatEnabled: true,
      emojiEnabled: false,
    });
  } finally {
    rmSync(temp.directory, { recursive: true, force: true });
  }
});

test("wrong-type chat feature object falls back to every environment default", () => {
  const temp = createTempStatePath();
  const defaults: ChatFeatureGovernance = {
    serverChatEnabled: true,
    richContentEnabled: false,
    emojiEnabled: false,
    itemRefsEnabled: false,
    roomChatV2Enabled: false,
    roomCombatRefsEnabled: false,
  };
  try {
    writeFileSync(temp.path, JSON.stringify({
      displayName: "Keep",
      chatFeatures: "wrong",
    }), "utf8");

    const state = createStore(temp.path, defaults).getState();
    assert.equal(state.displayName, "Keep");
    assert.deepEqual(state.chatFeatures, defaults);
  } finally {
    rmSync(temp.directory, { recursive: true, force: true });
  }
});

test("complete persisted chat state wins over opposite defaults after restart", () => {
  const temp = createTempStatePath();
  const persisted: ChatFeatureGovernance = {
    serverChatEnabled: true,
    richContentEnabled: false,
    emojiEnabled: true,
    itemRefsEnabled: false,
    roomChatV2Enabled: true,
    roomCombatRefsEnabled: false,
  };
  const opposite = Object.fromEntries(
    Object.entries(persisted).map(([key, value]) => [key, !value]),
  ) as unknown as ChatFeatureGovernance;
  try {
    createStore(temp.path, opposite).updateSettings({ chatFeatures: persisted });
    assert.deepEqual(createStore(temp.path, opposite).getState().chatFeatures, persisted);
  } finally {
    rmSync(temp.directory, { recursive: true, force: true });
  }
});

test("chat feature reads are defensive clones and partial patches preserve siblings", () => {
  const temp = createTempStatePath();
  try {
    const store = createStore(temp.path);
    const leaked = store.getState();
    leaked.chatFeatures.richContentEnabled = false;
    assert.equal(store.getState().chatFeatures.richContentEnabled, true);

    store.patch({ chatFeatures: { serverChatEnabled: true } });
    assert.deepEqual(store.getState().chatFeatures, {
      ...chatDefaults,
      serverChatEnabled: true,
    });
  } finally {
    rmSync(temp.directory, { recursive: true, force: true });
  }
});

test("write failure leaves memory disk and prior settings unchanged", () => {
  const temp = createTempStatePath();
  try {
    const store = createStore(temp.path);
    store.updateSettings({ displayName: "Before", chatFeatures: { serverChatEnabled: true } });
    const memoryBefore = store.getState();
    const diskBefore = readFileSync(temp.path, "utf8");
    mkdirSync(`${temp.path}.tmp`);

    assert.throws(() => store.updateSettings({
      displayName: "After",
      chatFeatures: { richContentEnabled: false },
    }));

    assert.deepEqual(store.getState(), memoryBefore);
    assert.equal(readFileSync(temp.path, "utf8"), diskBefore);
  } finally {
    rmSync(temp.directory, { recursive: true, force: true });
  }
});
