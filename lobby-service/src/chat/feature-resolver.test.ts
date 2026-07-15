import assert from "node:assert/strict";
import test from "node:test";
import type { ChatContent } from "./protocol.js";
import {
  resolveEnabledFeatures,
  supportsContent,
  type ChatFeatureVersions,
} from "./feature-resolver.js";

const allVersions: ChatFeatureVersions = {
  richContentVersion: 1,
  emojiSetVersion: 1,
  itemRefVersion: 1,
  combatRefVersion: 1,
};

const allOff: ChatFeatureVersions = {
  richContentVersion: 0,
  emojiSetVersion: 0,
  itemRefVersion: 0,
  combatRefVersion: 0,
};

function resolve(overrides: Partial<Parameters<typeof resolveEnabledFeatures>[0]> = {}) {
  return resolveEnabledFeatures({
    channel: "server",
    compiled: allVersions,
    configured: allVersions,
    admin: allVersions,
    channelEnabled: true,
    roomV2Enabled: true,
    sender: allVersions,
    receiver: allVersions,
    ...overrides,
  });
}

test("rich zero forces dependent features to zero", () => {
  assert.deepEqual(resolve({
    compiled: { ...allVersions, richContentVersion: 0 },
  }), allOff);
});

test("admin overrides configured values while compiled support remains authoritative", () => {
  assert.deepEqual(resolve({
    configured: allOff,
    admin: allVersions,
  }), {
    richContentVersion: 1,
    emojiSetVersion: 1,
    itemRefVersion: 1,
    combatRefVersion: 0,
  });
  assert.deepEqual(resolve({
    compiled: { ...allVersions, emojiSetVersion: 0 },
    configured: allVersions,
    admin: allVersions,
  }), {
    richContentVersion: 1,
    emojiSetVersion: 0,
    itemRefVersion: 1,
    combatRefVersion: 0,
  });
  assert.deepEqual(resolveEnabledFeatures({
    channel: "server",
    compiled: allVersions,
    configured: { ...allVersions, itemRefVersion: 0 },
    channelEnabled: true,
  }), {
    richContentVersion: 1,
    emojiSetVersion: 1,
    itemRefVersion: 0,
    combatRefVersion: 0,
  });
});

test("channel and room-v2 gates disable every rich feature", () => {
  assert.deepEqual(resolve({ channelEnabled: false }), allOff);
  assert.deepEqual(resolve({ channel: "room", roomV2Enabled: false }), allOff);
});

test("server never enables combat and room enables it through the full feature intersection", () => {
  assert.deepEqual(resolve(), {
    richContentVersion: 1,
    emojiSetVersion: 1,
    itemRefVersion: 1,
    combatRefVersion: 0,
  });
  assert.deepEqual(resolve({
    channel: "room",
    sender: allVersions,
    receiver: { ...allVersions, itemRefVersion: 0 },
  }), {
    richContentVersion: 1,
    emojiSetVersion: 1,
    itemRefVersion: 0,
    combatRefVersion: 1,
  });
  assert.deepEqual(resolve({
    channel: "room",
    sender: { ...allVersions, richContentVersion: 0 },
    receiver: allVersions,
  }), allOff);
  assert.equal(resolve({ channel: "room" }).combatRefVersion, 1);

  for (const [label, overrides] of [
    ["compiled", { compiled: { ...allVersions, combatRefVersion: 0 } }],
    ["configured", { configured: { ...allVersions, combatRefVersion: 0 }, admin: {} }],
    ["admin", { admin: { combatRefVersion: 0 } }],
    ["sender", { sender: { ...allVersions, combatRefVersion: 0 } }],
    ["receiver", { receiver: { ...allVersions, combatRefVersion: 0 } }],
  ] as const) {
    assert.equal(resolve({ channel: "room", ...overrides }).combatRefVersion, 0, label);
  }
});

test("supportsContent checks the whole message without stripping segments", () => {
  const content: ChatContent = {
    formatVersion: 1,
    segments: [
      { kind: "text", text: "look " },
      { kind: "emoji", emojiId: "heart" },
      { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", upgradeLevel: 1 },
    ],
  };
  const before = JSON.stringify(content);

  assert.equal(supportsContent(content, resolve()), true);
  assert.equal(supportsContent(content, resolve({
    receiver: { ...allVersions, emojiSetVersion: 0 },
  })), false);
  assert.equal(supportsContent(content, resolve({
    receiver: { ...allVersions, itemRefVersion: 0 },
  })), false);
  assert.equal(supportsContent({
    formatVersion: 1,
    segments: [{ kind: "text", text: "legacy-safe" }],
  }, allOff), true);
  assert.equal(JSON.stringify(content), before);
  assert.equal(content.segments.length, 3);
});

test("supportsContent routes power and player targets by combat while monster stays disabled", () => {
  const power: ChatContent = {
    formatVersion: 1,
    segments: [{
      kind: "power_state",
      modelId: "MegaCrit.Strength",
      amount: 2,
      roomSessionId: "session-1",
      ownerPlayerNetId: "net:alice",
    }],
  };
  const player: ChatContent = {
    formatVersion: 1,
    segments: [{
      kind: "target_ref",
      targetKind: "player",
      targetKey: "net:bob",
      roomSessionId: "session-1",
    }],
  };
  const monster: ChatContent = {
    formatVersion: 1,
    segments: [{
      kind: "target_ref",
      targetKind: "monster",
      targetKey: "unstable-monster-id",
      roomSessionId: "session-1",
    }],
  };
  const combatEnabled = resolve({ channel: "room" });
  const combatDisabled = { ...combatEnabled, combatRefVersion: 0 as const };

  assert.equal(supportsContent(power, combatEnabled), true);
  assert.equal(supportsContent(player, combatEnabled), true);
  assert.equal(supportsContent(power, combatDisabled), false);
  assert.equal(supportsContent(player, combatDisabled), false);
  assert.equal(supportsContent(power, { ...combatEnabled, richContentVersion: 0 }), false);
  assert.equal(supportsContent(monster, combatEnabled), false);
});

test("supportsContent requires every capability used by a mixed message without mutation", () => {
  const content: ChatContent = {
    formatVersion: 1,
    segments: [
      { kind: "text", text: "card power " },
      { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" },
      {
        kind: "power_state",
        modelId: "MegaCrit.Strength",
        amount: 1,
        roomSessionId: "session-1",
      },
    ],
  };
  const before = structuredClone(content);
  const enabled = resolve({ channel: "room" });

  assert.equal(supportsContent(content, enabled), true);
  assert.equal(supportsContent(content, { ...enabled, itemRefVersion: 0 }), false);
  assert.equal(supportsContent(content, { ...enabled, combatRefVersion: 0 }), false);
  assert.deepEqual(content, before);
});

test("supportsContent rejects unknown runtime segment kinds", () => {
  const content = {
    formatVersion: 1,
    segments: [{ kind: "future_ref", opaqueId: "future-1" }],
  } as unknown as ChatContent;

  assert.equal(supportsContent(content, resolve({ channel: "room" })), false);
});
