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

test("server never enables combat and room intersects both peers", () => {
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
    combatRefVersion: 0,
  });
  assert.deepEqual(resolve({
    channel: "room",
    sender: { ...allVersions, richContentVersion: 0 },
    receiver: allVersions,
  }), allOff);
  assert.equal(resolve({ channel: "room" }).combatRefVersion, 0);
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
