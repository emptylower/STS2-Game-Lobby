import assert from "node:assert/strict";
import test from "node:test";
import type { ChatFeatureVersions } from "./feature-resolver.js";
import { RoomChatGateway, type RoomChatPeerRegistration } from "./room-gateway.js";

const allVersions: ChatFeatureVersions = {
  richContentVersion: 1,
  emojiSetVersion: 1,
  itemRefVersion: 1,
  combatRefVersion: 1,
};

const noItemVersions: ChatFeatureVersions = {
  ...allVersions,
  itemRefVersion: 0,
};

interface TestPeer {
  registration: RoomChatPeerRegistration;
  frames: Array<Record<string, unknown>>;
}

function peer(
  connectionSessionId: string,
  role: "host" | "client" = "client",
): TestPeer {
  const frames: Array<Record<string, unknown>> = [];
  return {
    registration: {
      connectionSessionId,
      roomId: "room-1",
      roomSessionId: "session-1",
      controlChannelId: "control-1",
      role,
      send: (frame) => frames.push(frame),
    },
    frames,
  };
}

function hello(
  playerName: string,
  playerNetId: string,
  roomChatVersions: ChatFeatureVersions = allVersions,
) {
  return {
    type: "client_hello",
    roomId: "room-1",
    controlChannelId: "control-1",
    role: "client",
    playerName,
    playerNetId,
    roomChatVersions,
  };
}

function hostHello(
  playerName: string,
  playerNetId: string,
  roomChatVersions: ChatFeatureVersions = allVersions,
) {
  return {
    type: "host_hello",
    roomId: "room-1",
    controlChannelId: "control-1",
    role: "host",
    playerName,
    playerNetId,
    roomChatVersions,
  };
}

function roomSend(clientMessageId: string, content: unknown, overrides: Record<string, unknown> = {}) {
  return {
    type: "room_chat_v2",
    protocolVersion: 1,
    clientMessageId,
    roomId: "room-1",
    roomSessionId: "session-1",
    content,
    ...overrides,
  };
}

function textContent(text: string) {
  return { formatVersion: 1, segments: [{ kind: "text", text }] };
}

function createGateway(options: {
  now?: () => number;
  chatEnabled?: () => boolean;
} = {}) {
  let nextId = 0;
  return new RoomChatGateway({
    compiledFeatures: allVersions,
    configuredFeatures: allVersions,
    roomV2Enabled: true,
    getChatEnabled: options.chatEnabled ?? (() => true),
    now: options.now ?? (() => Date.parse("2026-07-15T00:00:00.000Z")),
    randomUuid: () => `00000000-0000-4000-8000-${String(++nextId).padStart(12, "0")}`,
  });
}

test("room v2 rejects sends before hello then locks normalized client and host identity", () => {
  const gateway = createGateway();
  const client = peer("client-connection");
  const host = peer("host-connection", "host");
  gateway.registerPeer(client.registration);
  gateway.registerPeer(host.registration);

  assert.equal(gateway.handleControlEnvelope(
    "client-connection",
    roomSend("11111111-1111-4111-8111-111111111111", textContent("too early")),
  ), true);
  assert.equal(client.frames.at(-1)?.type, "room_chat_error");
  assert.equal(client.frames.at(-1)?.code, "protocol_mismatch");

  assert.equal(gateway.handleControlEnvelope(
    "client-connection",
    hello("  A\u0301lice  ", "  net:alice  "),
  ), true);
  assert.deepEqual(client.frames.at(-1), {
    type: "room_chat_ready",
    protocolVersion: 1,
    roomId: "room-1",
    roomSessionId: "session-1",
    enabledFeatures: {
      richContentVersion: 1,
      emojiSetVersion: 1,
      itemRefVersion: 1,
      combatRefVersion: 0,
    },
  });
  assert.deepEqual(gateway.getLockedIdentity("client-connection"), {
    playerName: "Álice",
    playerNetId: "net:alice",
  });

  gateway.handleControlEnvelope("client-connection", hello("Mallory", "net:mallory", noItemVersions));
  assert.equal(client.frames.at(-1)?.type, "room_chat_error");
  assert.equal(client.frames.at(-1)?.code, "protocol_mismatch");
  assert.deepEqual(gateway.getLockedIdentity("client-connection"), {
    playerName: "Álice",
    playerNetId: "net:alice",
  });

  assert.equal(gateway.handleControlEnvelope("host-connection", hostHello("  Host  ", " net:host ")), true);
  assert.equal(host.frames.at(-1)?.type, "room_chat_ready");
  assert.deepEqual(gateway.getLockedIdentity("host-connection"), {
    playerName: "Host",
    playerNetId: "net:host",
  });
});

test("room v2 validates protocol room session and chat-enabled context", () => {
  let chatEnabled = true;
  const gateway = createGateway({ chatEnabled: () => chatEnabled });
  const client = peer("context-client");
  gateway.registerPeer(client.registration);
  gateway.handleControlEnvelope("context-client", hello("Alice", "net:alice"));
  client.frames.length = 0;

  for (const envelope of [
    roomSend("12121212-1212-4212-8212-121212121212", textContent("bad protocol"), { protocolVersion: 2 }),
    roomSend("13131313-1313-4313-8313-131313131313", textContent("bad room"), { roomId: "room-2" }),
    roomSend("14141414-1414-4414-8414-141414141414", textContent("bad session"), { roomSessionId: "session-2" }),
  ]) {
    gateway.handleControlEnvelope("context-client", envelope);
    assert.equal(client.frames.at(-1)?.code, "protocol_mismatch");
  }

  chatEnabled = false;
  gateway.handleControlEnvelope(
    "context-client",
    roomSend("15151515-1515-4515-8515-151515151515", textContent("disabled")),
  );
  assert.equal(client.frames.at(-1)?.code, "chat_disabled");
});

test("room v2 sends ACK and whole rich or exact legacy fallback per recipient", () => {
  const gateway = createGateway();
  const sender = peer("sender");
  const richRecipient = peer("rich-recipient");
  const legacyRecipient = peer("legacy-recipient");
  const notReady = peer("not-ready");
  for (const candidate of [sender, richRecipient, legacyRecipient, notReady]) {
    gateway.registerPeer(candidate.registration);
  }
  gateway.handleControlEnvelope("sender", hello("Alice", "net:alice"));
  gateway.handleControlEnvelope("rich-recipient", hello("Bob", "net:bob"));
  gateway.handleControlEnvelope("legacy-recipient", hello("Carol", "net:carol", noItemVersions));
  sender.frames.length = 0;
  richRecipient.frames.length = 0;
  legacyRecipient.frames.length = 0;

  const content = {
    formatVersion: 1,
    segments: [
      { kind: "text", text: "look " },
      { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", upgradeLevel: 1 },
      { kind: "emoji", emojiId: "heart" },
    ],
  };
  gateway.handleControlEnvelope(
    "sender",
    roomSend("16161616-1616-4616-8616-161616161616", content),
  );

  assert.deepEqual(sender.frames.map((frame) => frame.type), ["room_chat_ack", "room_chat_message"]);
  assert.deepEqual(sender.frames[0]?.message, sender.frames[1]?.message);
  const message = sender.frames[0]?.message as Record<string, unknown>;
  assert.equal(message.roomId, "room-1");
  assert.equal(message.roomSessionId, "session-1");
  assert.equal(message.senderId, "net:alice");
  assert.equal(message.senderName, "Alice");
  assert.equal(message.plainTextFallback, "look [Card][Emoji]");
  assert.deepEqual(message.content, content);
  assert.deepEqual(richRecipient.frames, [{
    type: "room_chat_message",
    protocolVersion: 1,
    message,
  }]);

  assert.equal(legacyRecipient.frames.length, 1);
  assert.deepEqual(legacyRecipient.frames[0], {
    type: "room_chat",
    roomId: "room-1",
    playerName: "Alice",
    playerNetId: "net:alice",
    messageId: message.messageId,
    messageText: "look [Card][Emoji]",
    sentAtUnixMs: Date.parse("2026-07-15T00:00:00.000Z"),
  });
  assert.equal("modelId" in legacyRecipient.frames[0]!, false);
  assert.equal(JSON.stringify(legacyRecipient.frames).includes("MegaCrit.Strike"), false);
  assert.equal(legacyRecipient.frames.some((frame) => frame.type === "room_chat_message"), false);
  assert.equal(notReady.frames.length, 0);
});

test("room v2 dedupes canonical mixed content within one connection session", () => {
  const gateway = createGateway();
  const sender = peer("dedupe-sender");
  const recipient = peer("dedupe-recipient");
  gateway.registerPeer(sender.registration);
  gateway.registerPeer(recipient.registration);
  gateway.handleControlEnvelope("dedupe-sender", hello("Alice", "net:alice"));
  gateway.handleControlEnvelope("dedupe-recipient", hello("Bob", "net:bob"));
  sender.frames.length = 0;
  recipient.frames.length = 0;
  const id = "17171717-1717-4717-8717-171717171717";
  const first = {
    formatVersion: 1,
    segments: [
      { kind: "text", text: "  look" },
      { kind: "text", text: " " },
      { kind: "emoji", emojiId: "heart" },
    ],
  };
  const same = {
    segments: [
      { text: "look ", kind: "text" },
      { emojiId: "heart", kind: "emoji" },
    ],
    formatVersion: 1,
  };

  gateway.handleControlEnvelope("dedupe-sender", roomSend(id, first));
  const originalAck = sender.frames[0];
  gateway.handleControlEnvelope("dedupe-sender", roomSend(id, same));
  assert.deepEqual(sender.frames.at(-1), originalAck);
  assert.equal(recipient.frames.length, 1);

  gateway.handleControlEnvelope("dedupe-sender", roomSend(id, {
    formatVersion: 1,
    segments: [{ kind: "emoji", emojiId: "check" }],
  }));
  assert.equal(sender.frames.at(-1)?.code, "duplicate_message");
  assert.equal(recipient.frames.length, 1);
});

test("room v2 replays cached errors and conflicts on changed content", () => {
  const gateway = createGateway();
  const sender = peer("error-dedupe");
  gateway.registerPeer(sender.registration);
  gateway.handleControlEnvelope("error-dedupe", hello("Alice", "net:alice", noItemVersions));
  sender.frames.length = 0;
  const id = "22222222-2222-4222-8222-222222222222";
  const disabledItem = {
    formatVersion: 1,
    segments: [{ kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" }],
  };

  gateway.handleControlEnvelope("error-dedupe", roomSend(id, disabledItem));
  const originalError = sender.frames.at(-1);
  assert.equal(originalError?.code, "feature_disabled");
  gateway.handleControlEnvelope("error-dedupe", roomSend(id, {
    segments: [{ modelId: "MegaCrit.Strike", itemType: "card", kind: "item_ref" }],
    formatVersion: 1,
  }));
  assert.deepEqual(sender.frames.at(-1), originalError);

  gateway.handleControlEnvelope("error-dedupe", roomSend(id, textContent("changed")));
  assert.equal(sender.frames.at(-1)?.code, "duplicate_message");
  assert.equal(sender.frames.some((frame) => frame.type === "room_chat_message"), false);
});

test("deep invalid content falls back to the raw envelope for dedupe conflicts", () => {
  const gateway = createGateway();
  const sender = peer("deep-invalid");
  gateway.registerPeer(sender.registration);
  gateway.handleControlEnvelope("deep-invalid", hello("Alice", "net:alice"));
  sender.frames.length = 0;
  let nested: unknown = 0;
  for (let depth = 0; depth < 70; depth += 1) nested = [nested];
  const id = "28282828-2828-4828-8828-282828282828";
  const envelope = roomSend(id, nested);

  gateway.handleControlEnvelope("deep-invalid", envelope, "raw-envelope-one");
  const original = sender.frames.at(-1);
  assert.equal(original?.code, "invalid_content");
  gateway.handleControlEnvelope("deep-invalid", envelope, "raw-envelope-one");
  assert.deepEqual(sender.frames.at(-1), original);
  gateway.handleControlEnvelope("deep-invalid", envelope, "raw-envelope-two");
  assert.equal(sender.frames.at(-1)?.code, "duplicate_message");
});

test("peer without roomChatVersions receives legacy even for text-only v2 messages", () => {
  const gateway = createGateway();
  const sender = peer("new-sender");
  const oldRecipient = peer("old-recipient");
  gateway.registerPeer(sender.registration);
  gateway.registerPeer(oldRecipient.registration);
  gateway.handleControlEnvelope("new-sender", hello("Alice", "net:alice"));
  const oldHello = hello("  Old  ", "  net:old  ") as Record<string, unknown>;
  delete oldHello.roomChatVersions;
  gateway.handleControlEnvelope("old-recipient", oldHello);
  assert.deepEqual(gateway.getLockedIdentity("old-recipient"), {
    playerName: "Old",
    playerNetId: "net:old",
  });
  gateway.handleControlEnvelope("old-recipient", {
    ...oldHello,
    playerName: "Mallory",
    playerNetId: "net:mallory",
  });
  assert.deepEqual(gateway.getLockedIdentity("old-recipient"), {
    playerName: "Old",
    playerNetId: "net:old",
  });
  assert.equal(oldRecipient.frames.length, 0);
  sender.frames.length = 0;
  oldRecipient.frames.length = 0;

  gateway.handleControlEnvelope(
    "new-sender",
    roomSend("23232323-2323-4323-8323-232323232323", textContent("plain text")),
  );
  assert.equal(oldRecipient.frames.length, 1);
  assert.equal(oldRecipient.frames[0]?.type, "room_chat");
  assert.equal(oldRecipient.frames[0]?.messageText, "plain text");
});

test("legacy host hello without versions or net id is silent and receives exact fallback", () => {
  const gateway = createGateway();
  const sender = peer("new-client");
  const legacyHost = peer("legacy-host", "host");
  const notReady = peer("not-ready-host", "host");
  gateway.registerPeer(sender.registration);
  gateway.registerPeer(legacyHost.registration);
  gateway.registerPeer(notReady.registration);
  gateway.handleControlEnvelope("new-client", hello("Alice", "net:alice"));
  sender.frames.length = 0;

  gateway.handleControlEnvelope("legacy-host", {
    type: "host_hello",
    roomId: "room-1",
    controlChannelId: "control-1",
    role: "host",
    playerName: "Host",
  });
  assert.equal(legacyHost.frames.length, 0);
  assert.equal(gateway.getLockedIdentity("legacy-host"), null);

  gateway.handleControlEnvelope(
    "legacy-host",
    roomSend("24242424-2424-4424-8424-242424242424", textContent("not v2")),
  );
  assert.equal(legacyHost.frames.at(-1)?.type, "room_chat_error");
  assert.equal(legacyHost.frames.at(-1)?.code, "protocol_mismatch");
  legacyHost.frames.length = 0;

  gateway.handleControlEnvelope(
    "new-client",
    roomSend("25252525-2525-4525-8525-252525252525", {
      formatVersion: 1,
      segments: [
        { kind: "text", text: "look " },
        { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" },
      ],
    }),
  );
  const message = (sender.frames[0]?.message as Record<string, unknown>);
  assert.deepEqual(legacyHost.frames, [{
    type: "room_chat",
    roomId: "room-1",
    playerName: "Alice",
    playerNetId: "net:alice",
    messageId: message.messageId,
    messageText: "look [Card]",
    sentAtUnixMs: Date.parse("2026-07-15T00:00:00.000Z"),
  }]);
  assert.deepEqual(notReady.frames, []);
});

test("room connection dedupe evicts after 256 entries and expires at ten minutes", () => {
  let now = 0;
  const gateway = createGateway({ now: () => now });
  const sender = peer("bounded-dedupe");
  gateway.registerPeer(sender.registration);
  gateway.handleControlEnvelope("bounded-dedupe", hello("Alice", "net:alice"));
  sender.frames.length = 0;

  for (let index = 0; index < 257; index += 1) {
    gateway.handleControlEnvelope(
      "bounded-dedupe",
      roomSend(clientId(index), textContent(`message ${index}`)),
    );
  }
  const firstId = clientId(0);
  const beforeEvictedReplay = sender.frames.filter((frame) => frame.type === "room_chat_message").length;
  gateway.handleControlEnvelope("bounded-dedupe", roomSend(firstId, textContent("message 0")));
  assert.equal(
    sender.frames.filter((frame) => frame.type === "room_chat_message").length,
    beforeEvictedReplay + 1,
  );

  const ttlId = "18181818-1818-4818-8818-181818181818";
  gateway.handleControlEnvelope("bounded-dedupe", roomSend(ttlId, textContent("ttl")));
  const beforeTtl = sender.frames.filter((frame) => frame.type === "room_chat_message").length;
  now = 10 * 60_000;
  gateway.handleControlEnvelope("bounded-dedupe", roomSend(ttlId, textContent("ttl")));
  assert.equal(
    sender.frames.filter((frame) => frame.type === "room_chat_message").length,
    beforeTtl + 1,
  );
});

test("legacy fallback stays within 60 UTF-16 units without splitting entities or surrogates", () => {
  const gateway = createGateway();
  const sender = peer("fallback-sender");
  const legacy = peer("fallback-legacy");
  gateway.registerPeer(sender.registration);
  gateway.registerPeer(legacy.registration);
  gateway.handleControlEnvelope("fallback-sender", hello("Alice", "net:alice"));
  gateway.handleControlEnvelope("fallback-legacy", hello("Legacy", "net:legacy", noItemVersions));
  sender.frames.length = 0;
  legacy.frames.length = 0;

  gateway.handleControlEnvelope("fallback-sender", roomSend(
    "19191919-1919-4919-8919-191919191919",
    {
      formatVersion: 1,
      segments: [
        { kind: "text", text: "😀".repeat(29) },
        { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" },
      ],
    },
  ));
  const surrogateFallback = String(legacy.frames.at(-1)?.messageText);
  assert.equal(surrogateFallback, "😀".repeat(29));
  assert.equal(surrogateFallback.length, 58);
  assert.equal(/^[\s\S]*[\ud800-\udbff]$/.test(surrogateFallback), false);

  gateway.handleControlEnvelope("fallback-sender", roomSend(
    "20202020-2020-4020-8020-202020202020",
    {
      formatVersion: 1,
      segments: [
        { kind: "text", text: "x".repeat(54) },
        { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" },
      ],
    },
  ));
  const completePlaceholder = String(legacy.frames.at(-1)?.messageText);
  assert.equal(completePlaceholder, `${"x".repeat(54)}[Card]`);
  assert.equal(completePlaceholder.length, 60);

  gateway.handleControlEnvelope("fallback-sender", roomSend(
    "26262626-2626-4626-8626-262626262626",
    {
      formatVersion: 1,
      segments: [
        { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" },
        { kind: "text", text: "x".repeat(59) },
      ],
    },
  ));
  assert.equal(legacy.frames.at(-1)?.messageText, "x".repeat(59));

  gateway.handleControlEnvelope("fallback-sender", roomSend(
    "27272727-2727-4727-8727-272727272727",
    {
      formatVersion: 1,
      segments: [
        { kind: "text", text: "x".repeat(59) },
        { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" },
        { kind: "text", text: "yz" },
      ],
    },
  ));
  assert.equal(legacy.frames.at(-1)?.messageText, `${"x".repeat(59)}y`);

  gateway.handleControlEnvelope("fallback-sender", roomSend(
    "29292929-2929-4929-8929-292929292929",
    {
      formatVersion: 1,
      segments: [
        { kind: "text", text: "x".repeat(59) },
        { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" },
        { kind: "text", text: "😀" },
        { kind: "emoji", emojiId: "heart" },
        { kind: "text", text: "z" },
      ],
    },
  ));
  const skippedAstral = String(legacy.frames.at(-1)?.messageText);
  assert.equal(skippedAstral, `${"x".repeat(59)}z`);
  assert.equal(skippedAstral.length, 60);
});

test("room envelopes require own context fields and ignore inherited feature versions", () => {
  const gateway = createGateway();
  const client = peer("own-fields");
  gateway.registerPeer(client.registration);

  for (const missing of ["roomId", "controlChannelId", "role"] as const) {
    const candidate = hello("Alice", "net:alice") as Record<string, unknown>;
    delete candidate[missing];
    gateway.handleControlEnvelope("own-fields", candidate);
    assert.equal(client.frames.at(-1)?.code, "protocol_mismatch");
    assert.equal(gateway.getLockedIdentity("own-fields"), null);
  }

  gateway.handleControlEnvelope("own-fields", {
    ...hello("Alice", "net:alice"),
    roomChatVersions: undefined,
  });
  assert.equal(client.frames.at(-1)?.code, "protocol_mismatch");
  assert.equal(gateway.getLockedIdentity("own-fields"), null);

  withPollutedObjectPrototype({
    richContentVersion: 1,
    emojiSetVersion: 1,
    itemRefVersion: 1,
    combatRefVersion: 1,
  }, () => {
    gateway.handleControlEnvelope("own-fields", {
      ...hello("Alice", "net:alice"),
      roomChatVersions: {},
    });
  });
  assert.deepEqual((client.frames.at(-1)?.enabledFeatures), {
    richContentVersion: 0,
    emojiSetVersion: 0,
    itemRefVersion: 0,
    combatRefVersion: 0,
  });

  client.frames.length = 0;
  withPollutedObjectPrototype({
    protocolVersion: 1,
    clientMessageId: "21212121-2121-4121-8121-212121212121",
    roomId: "room-1",
    roomSessionId: "session-1",
    content: textContent("inherited envelope"),
  }, () => {
    gateway.handleControlEnvelope("own-fields", { type: "room_chat_v2" });
  });
  assert.equal(client.frames.at(-1)?.code, "invalid_message");

  const hostile = new Proxy({}, {
    getPrototypeOf: () => {
      throw new Error("prototype trap");
    },
  });
  assert.doesNotThrow(() => gateway.handleControlEnvelope("own-fields", hostile));
  assert.equal(gateway.handleControlEnvelope("own-fields", hostile), false);
});

test("hello rejects unpaired surrogate names", () => {
  const gateway = createGateway();
  const client = peer("bad-name");
  gateway.registerPeer(client.registration);
  gateway.handleControlEnvelope("bad-name", hello("bad\ud800name", "net:bad"));
  assert.equal(client.frames.at(-1)?.code, "protocol_mismatch");
  assert.equal(gateway.getLockedIdentity("bad-name"), null);
});

function clientId(index: number): string {
  return `00000000-0000-4000-8000-${String(index).padStart(12, "0")}`;
}

function withPollutedObjectPrototype(
  properties: Record<string, unknown>,
  action: () => void,
): void {
  const originals = new Map<string, PropertyDescriptor | undefined>();
  const replaced: string[] = [];
  try {
    for (const key of Object.keys(properties)) {
      const original = Object.getOwnPropertyDescriptor(Object.prototype, key);
      originals.set(key, original);
      if (original !== undefined && !original.configurable) {
        throw new Error(`cannot safely replace Object.prototype.${key}`);
      }
      Object.defineProperty(Object.prototype, key, {
        value: properties[key],
        configurable: true,
        enumerable: false,
        writable: true,
      });
      replaced.push(key);
    }
    action();
  } finally {
    for (const key of replaced.reverse()) {
      const original = originals.get(key);
      if (original === undefined) Reflect.deleteProperty(Object.prototype, key);
      else Object.defineProperty(Object.prototype, key, original);
    }
  }
}
