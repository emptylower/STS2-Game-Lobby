import assert from "node:assert/strict";
import test from "node:test";
import {
  assertWireBudget,
  canonicalizeChatContent,
  canonicalizeServerContent,
  type CanonicalChatMessage,
  type ChatContent,
  deterministicContentJson,
  EMOJI_SET_1,
  type EnabledRichFeatures,
  measureChatWireBytes,
  projectChatWireEnvelopes,
  renderPlainTextFallback,
  utf8JsonBytes,
} from "./protocol.js";

function hasCode(code: string) {
  return (error: unknown) =>
    error instanceof Error &&
    "code" in error &&
    (error as { code: unknown }).code === code;
}

const UUID_A = "01234567-89ab-cdef-0123-456789abcdef";
const SENDER_ID = "ABCDEFGHIJKLMNOPQRSTUV"; // 22 ASCII base64url
const SENT_AT = "2026-07-12T12:00:00.000Z"; // 24 ASCII

const allRichFeatures: EnabledRichFeatures = {
  richContentVersion: 1,
  emojiSetVersion: 1,
  itemRefVersion: 1,
  combatRefVersion: 0,
};

function textContent(text: string): ChatContent {
  return { formatVersion: 1, segments: [{ kind: "text", text }] };
}

function sampleMessage(content: ChatContent, senderName = "Ironclad"): CanonicalChatMessage {
  return {
    messageId: UUID_A,
    senderId: SENDER_ID,
    senderName,
    content,
    plainTextFallback: renderPlainTextFallback(content),
    sentAt: SENT_AT,
  };
}

test("canonicalizes ordered text emoji and item refs", () => {
  assert.deepEqual(
    canonicalizeChatContent(
      {
        formatVersion: 1,
        segments: [
          { kind: "text", text: "  看看\r\n" },
          { kind: "emoji", emojiId: "thumbs-up" },
          { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", upgradeLevel: 1 },
          { kind: "item_ref", itemType: "relic", modelId: "MegaCrit.Anchor" },
          { kind: "item_ref", itemType: "potion", modelId: "MegaCrit.FirePotion" },
        ],
      },
      allRichFeatures,
    ),
    {
      formatVersion: 1,
      segments: [
        { kind: "text", text: "看看\n" },
        { kind: "emoji", emojiId: "thumbs-up" },
        { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", upgradeLevel: 1 },
        { kind: "item_ref", itemType: "relic", modelId: "MegaCrit.Anchor" },
        { kind: "item_ref", itemType: "potion", modelId: "MegaCrit.FirePotion" },
      ],
    },
  );
});

test("rejects unknown emoji and sender supplied item labels", () => {
  assert.throws(
    () =>
      canonicalizeChatContent(
        { formatVersion: 1, segments: [{ kind: "emoji", emojiId: "not-published" }] },
        allRichFeatures,
      ),
    hasCode("invalid_content"),
  );
  assert.throws(
    () =>
      canonicalizeChatContent(
        {
          formatVersion: 1,
          segments: [
            {
              kind: "item_ref",
              itemType: "relic",
              modelId: "MegaCrit.Anchor",
              displayName: "Anchor",
            },
          ],
        },
        allRichFeatures,
      ),
    hasCode("invalid_content"),
  );
});

test("accepts exactly the 18 accepted emoji IDs and rejects a nineteenth", () => {
  assert.equal(EMOJI_SET_1.length, 18);
  assert.equal(new Set(EMOJI_SET_1).size, 18);
  for (const emojiId of EMOJI_SET_1) {
    assert.deepEqual(
      canonicalizeChatContent(
        { formatVersion: 1, segments: [{ kind: "emoji", emojiId }] },
        allRichFeatures,
      ),
      { formatVersion: 1, segments: [{ kind: "emoji", emojiId }] },
    );
  }
  assert.throws(
    () =>
      canonicalizeChatContent(
        { formatVersion: 1, segments: [{ kind: "emoji", emojiId: "plus" }] },
        allRichFeatures,
      ),
    hasCode("invalid_content"),
  );
});

test("requires enabled rich emoji and item features", () => {
  const emoji = { formatVersion: 1, segments: [{ kind: "emoji", emojiId: "smile" }] };
  const item = {
    formatVersion: 1,
    segments: [{ kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" }],
  };

  assert.throws(
    () => canonicalizeChatContent(emoji, { ...allRichFeatures, richContentVersion: 0 }),
    hasCode("feature_disabled"),
  );
  assert.throws(
    () => canonicalizeChatContent(emoji, { ...allRichFeatures, emojiSetVersion: 0 }),
    hasCode("feature_disabled"),
  );
  assert.throws(
    () => canonicalizeChatContent(item, { ...allRichFeatures, itemRefVersion: 0 }),
    hasCode("feature_disabled"),
  );
  assert.throws(
    () => canonicalizeChatContent(item, { ...allRichFeatures, richContentVersion: 0 }),
    hasCode("feature_disabled"),
  );

  assert.throws(
    () =>
      canonicalizeChatContent(
        { formatVersion: 1, segments: [{ kind: "emoji", emojiId: "not-published" }] },
        { ...allRichFeatures, richContentVersion: 0 },
      ),
    hasCode("invalid_content"),
  );
  assert.throws(
    () =>
      canonicalizeChatContent(
        {
          formatVersion: 1,
          segments: [
            { kind: "item_ref", itemType: "relic", modelId: "MegaCrit.Anchor", displayName: "Anchor" },
          ],
        },
        { ...allRichFeatures, richContentVersion: 0 },
      ),
    hasCode("invalid_content"),
  );
});

test("validates the complete rich schema before applying feature gates", () => {
  const richDisabled: EnabledRichFeatures = {
    ...allRichFeatures,
    richContentVersion: 0,
  };
  const invalidContents = [
    {
      formatVersion: 1,
      segments: [
        { kind: "emoji", emojiId: "heart" },
        { kind: "item_ref", itemType: "relic", modelId: "MegaCrit.Anchor", displayName: "Anchor" },
      ],
    },
    {
      formatVersion: 1,
      segments: [
        { kind: "emoji", emojiId: "heart" },
        { kind: "future_segment", value: 1 },
      ],
    },
    {
      formatVersion: 1,
      segments: Array.from({ length: 13 }, () => ({ kind: "emoji", emojiId: "heart" })),
    },
  ];

  for (const content of invalidContents) {
    assert.throws(
      () => canonicalizeChatContent(content, richDisabled),
      hasCode("invalid_content"),
    );
  }

  assert.throws(
    () =>
      canonicalizeChatContent(
        { formatVersion: 1, segments: [{ kind: "emoji", emojiId: "heart" }] },
        richDisabled,
      ),
    hasCode("feature_disabled"),
  );
});

test("validates model IDs as 1 to 160 allowed ASCII characters", () => {
  const accepted = ["A", "MegaCrit.mod_name-card.01", "a".repeat(160)];
  for (const modelId of accepted) {
    assert.deepEqual(
      canonicalizeChatContent(
        { formatVersion: 1, segments: [{ kind: "item_ref", itemType: "relic", modelId }] },
        allRichFeatures,
      ),
      { formatVersion: 1, segments: [{ kind: "item_ref", itemType: "relic", modelId }] },
    );
  }

  const rejected: unknown[] = [
    "",
    "a".repeat(161),
    "MegaCrit/Strike",
    "MegaCrit:Strike",
    "牌",
    "has space",
    1,
    null,
  ];
  for (const modelId of rejected) {
    assert.throws(
      () =>
        canonicalizeChatContent(
          { formatVersion: 1, segments: [{ kind: "item_ref", itemType: "relic", modelId }] },
          allRichFeatures,
        ),
      hasCode("invalid_content"),
      `expected invalid modelId ${JSON.stringify(modelId)}`,
    );
  }
});

test("accepts card upgrade levels 0 through 9 only", () => {
  for (let upgradeLevel = 0; upgradeLevel <= 9; upgradeLevel += 1) {
    assert.deepEqual(
      canonicalizeChatContent(
        {
          formatVersion: 1,
          segments: [{ kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", upgradeLevel }],
        },
        allRichFeatures,
      ),
      {
        formatVersion: 1,
        segments: [{ kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", upgradeLevel }],
      },
    );
  }

  for (const upgradeLevel of [-1, 10, 1.5, "1", null]) {
    assert.throws(
      () =>
        canonicalizeChatContent(
          {
            formatVersion: 1,
            segments: [{ kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", upgradeLevel }],
          },
          allRichFeatures,
        ),
      hasCode("invalid_content"),
    );
  }

  for (const itemType of ["relic", "potion"] as const) {
    assert.throws(
      () =>
        canonicalizeChatContent(
          {
            formatVersion: 1,
            segments: [{ kind: "item_ref", itemType, modelId: "MegaCrit.Item", upgradeLevel: 0 }],
          },
          allRichFeatures,
        ),
      hasCode("invalid_content"),
    );
  }
});

test("enforces rich segment and entity limits", () => {
  const segments = [
    ...Array.from({ length: 12 }, () => ({ kind: "emoji", emojiId: "check" })),
    ...Array.from({ length: 20 }, (_, index) => ({ kind: "text", text: String(index % 10) })),
  ];
  const canonical = canonicalizeChatContent({ formatVersion: 1, segments }, allRichFeatures);
  assert.equal(segments.length, 32);
  assert.equal(canonical.segments.length, 13);

  assert.throws(
    () =>
      canonicalizeChatContent(
        { formatVersion: 1, segments: [...segments, { kind: "text", text: "x" }] },
        allRichFeatures,
      ),
    hasCode("invalid_content"),
  );
  assert.throws(
    () =>
      canonicalizeChatContent(
        {
          formatVersion: 1,
          segments: Array.from({ length: 13 }, () => ({ kind: "emoji", emojiId: "check" })),
        },
        allRichFeatures,
      ),
    hasCode("invalid_content"),
  );
});

test("enforces 300 Unicode scalars across separated text runs", () => {
  const astral = "😀";
  assert.equal(
    canonicalizeChatContent(
      {
        formatVersion: 1,
        segments: [
          { kind: "text", text: astral.repeat(150) },
          { kind: "emoji", emojiId: "heart" },
          { kind: "text", text: astral.repeat(150) },
        ],
      },
      allRichFeatures,
    ).segments.length,
    3,
  );
  assert.throws(
    () =>
      canonicalizeChatContent(
        {
          formatVersion: 1,
          segments: [
            { kind: "text", text: astral.repeat(150) },
            { kind: "emoji", emojiId: "heart" },
            { kind: "text", text: astral.repeat(151) },
          ],
        },
        allRichFeatures,
      ),
    hasCode("invalid_content"),
  );
});

test("accepts entity-only content and merges only adjacent text runs", () => {
  assert.deepEqual(
    canonicalizeChatContent(
      {
        formatVersion: 1,
        segments: [
          { kind: "emoji", emojiId: "sparkles" },
          { kind: "item_ref", itemType: "potion", modelId: "MegaCrit.FirePotion" },
        ],
      },
      allRichFeatures,
    ),
    {
      formatVersion: 1,
      segments: [
        { kind: "emoji", emojiId: "sparkles" },
        { kind: "item_ref", itemType: "potion", modelId: "MegaCrit.FirePotion" },
      ],
    },
  );

  assert.deepEqual(
    canonicalizeChatContent(
      {
        formatVersion: 1,
        segments: [
          { kind: "text", text: "  first" },
          { kind: "text", text: " second " },
          { kind: "emoji", emojiId: "eye" },
          { kind: "text", text: " third " },
          { kind: "text", text: "fourth  " },
        ],
      },
      allRichFeatures,
    ),
    {
      formatVersion: 1,
      segments: [
        { kind: "text", text: "first second " },
        { kind: "emoji", emojiId: "eye" },
        { kind: "text", text: " third fourth" },
      ],
    },
  );
});

test("rejects unknown reserved and combat segment fields", () => {
  const invalidSegments = [
    { kind: "emoji", emojiId: "heart", label: "Heart" },
    { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", url: "https://example.com" },
    { kind: "item_ref", itemType: "unknown", modelId: "MegaCrit.Item" },
    { kind: "power_state", modelId: "MegaCrit.Strength", amount: 1, roomSessionId: UUID_A },
    { kind: "target_ref", targetKind: "player", targetKey: "1", roomSessionId: UUID_A },
    { kind: "future_segment", value: 1 },
  ];
  for (const segment of invalidSegments) {
    assert.throws(
      () => canonicalizeChatContent({ formatVersion: 1, segments: [segment] }, allRichFeatures),
      hasCode("invalid_content"),
      `expected rejection for ${segment.kind}`,
    );
  }
});

test("keeps Phase 1 combat kinds feature-disabled while strict rich server schema rejects them", () => {
  for (const segment of [
    { kind: "power_state", modelId: "MegaCrit.Strength", amount: 1, roomSessionId: UUID_A },
    { kind: "target_ref", targetKind: "player", targetKey: "1", roomSessionId: UUID_A },
  ]) {
    assert.throws(
      () => canonicalizeChatContent({ formatVersion: 1, segments: [segment] }, allRichFeatures),
      hasCode("invalid_content"),
    );
    assert.throws(
      () => canonicalizeServerContent({ formatVersion: 1, segments: [segment] }),
      hasCode("feature_disabled"),
    );
  }
});

test("rejects custom prototypes and inherited required fields", () => {
  class ContentInstance {
    formatVersion = 1;
    segments = [{ kind: "text", text: "class content" }];
  }

  const inheritedContent = Object.create({
    formatVersion: 1,
    segments: [{ kind: "text", text: "inherited content" }],
  });
  const inheritedSegment = Object.create({ kind: "text", text: "inherited segment" });
  const customPrototypeContent = Object.assign(Object.create({ marker: true }), {
    formatVersion: 1,
    segments: [{ kind: "text", text: "custom prototype" }],
  });

  for (const content of [
    new ContentInstance(),
    inheritedContent,
    customPrototypeContent,
    { formatVersion: 1, segments: [inheritedSegment] },
  ]) {
    assert.throws(
      () => canonicalizeChatContent(content, allRichFeatures),
      hasCode("invalid_content"),
    );
  }

  const nullPrototypeSegment = Object.assign(Object.create(null), {
    kind: "text",
    text: "null prototype",
  });
  const nullPrototypeContent = Object.assign(Object.create(null), {
    formatVersion: 1,
    segments: [nullPrototypeSegment],
  });
  assert.deepEqual(canonicalizeChatContent(nullPrototypeContent, allRichFeatures), {
    formatVersion: 1,
    segments: [{ kind: "text", text: "null prototype" }],
  });
});

test("requires schema fields to be own properties despite Object.prototype pollution", () => {
  const textContentValue = {
    formatVersion: 1,
    segments: [{ kind: "text", text: "safe" }],
  };

  withPollutedObjectPrototype({ formatVersion: 1 }, () => {
    assert.throws(
      () => canonicalizeChatContent({ segments: textContentValue.segments }, allRichFeatures),
      hasCode("invalid_content"),
    );
  });
  withPollutedObjectPrototype({ segments: textContentValue.segments }, () => {
    assert.throws(
      () => canonicalizeChatContent({ formatVersion: 1 }, allRichFeatures),
      hasCode("invalid_content"),
    );
  });
  withPollutedObjectPrototype({ kind: "text" }, () => {
    assert.throws(
      () =>
        canonicalizeChatContent(
          { formatVersion: 1, segments: [{ text: "inherited kind" }] },
          allRichFeatures,
        ),
      hasCode("invalid_content"),
    );
  });
  withPollutedObjectPrototype({ text: "inherited text" }, () => {
    assert.throws(
      () =>
        canonicalizeChatContent(
          { formatVersion: 1, segments: [{ kind: "text" }] },
          allRichFeatures,
        ),
      hasCode("invalid_content"),
    );
  });
  withPollutedObjectPrototype({ emojiId: "heart" }, () => {
    assert.throws(
      () =>
        canonicalizeChatContent(
          { formatVersion: 1, segments: [{ kind: "emoji" }] },
          allRichFeatures,
        ),
      hasCode("invalid_content"),
    );
  });
  withPollutedObjectPrototype({ itemType: "card" }, () => {
    assert.throws(
      () =>
        canonicalizeChatContent(
          { formatVersion: 1, segments: [{ kind: "item_ref", modelId: "MegaCrit.Strike" }] },
          allRichFeatures,
        ),
      hasCode("invalid_content"),
    );
  });
  withPollutedObjectPrototype({ modelId: "MegaCrit.Strike" }, () => {
    assert.throws(
      () =>
        canonicalizeChatContent(
          { formatVersion: 1, segments: [{ kind: "item_ref", itemType: "card" }] },
          allRichFeatures,
        ),
      hasCode("invalid_content"),
    );
  });

  assert.deepEqual(canonicalizeChatContent(textContentValue, allRichFeatures), textContentValue);
});

test("normalizes NFC/newlines and merges text", () => {
  assert.deepEqual(
    canonicalizeServerContent({
      formatVersion: 1,
      segments: [
        { kind: "text", text: "  e\u0301\r\n" },
        { kind: "text", text: "hello  " },
      ],
    }),
    { formatVersion: 1, segments: [{ kind: "text", text: "é\nhello" }] },
  );
});

test("re-applies NFC after merging adjacent text segments", () => {
  // Combining mark arrives in a later segment so per-segment NFC cannot precompose.
  const content = canonicalizeServerContent({
    formatVersion: 1,
    segments: [
      { kind: "text", text: "e" },
      { kind: "text", text: "\u0301x" },
    ],
  });
  assert.deepEqual(content, {
    formatVersion: 1,
    segments: [{ kind: "text", text: "\u00e9x" }],
  });
  const mergedText = content.segments[0]!.text;
  assert.equal(mergedText, mergedText.normalize("NFC"));
  assert.notEqual(mergedText, "e\u0301x");
  assert.equal(
    deterministicContentJson(content),
    '{"formatVersion":1,"segments":[{"kind":"text","text":"\u00e9x"}]}',
  );
});

test("phase 1 rejects known rich kinds with feature_disabled", () => {
  for (const segment of [
    { kind: "emoji", emojiId: "thumbs-up" },
    { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" },
    { kind: "power_state", modelId: "x", amount: 1, roomSessionId: UUID_A },
    { kind: "target_ref", targetId: UUID_A },
  ]) {
    assert.throws(
      () => canonicalizeServerContent({ formatVersion: 1, segments: [segment] }),
      hasCode("feature_disabled"),
      `expected feature_disabled for kind ${segment.kind}`,
    );
  }
});

test("rejects unknown kinds and reserved fields with invalid_content", () => {
  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [{ kind: "unknown_widget", text: "x" }],
      }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [{ kind: "text", text: "ok", extra: true }],
      }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [{ kind: "text", text: "ok" }],
        extra: 1,
      }),
    hasCode("invalid_content"),
  );
});

test("rejects wrong format version and non-exact types", () => {
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: 2, segments: [{ kind: "text", text: "x" }] }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: "1", segments: [{ kind: "text", text: "x" }] }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: 1, segments: "nope" }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: 1 }] }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () => canonicalizeServerContent(null),
    hasCode("invalid_content"),
  );
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: 1, segments: [null] }),
    hasCode("invalid_content"),
  );
});

test("enforces 32 segment limit before merge", () => {
  const segments = Array.from({ length: 32 }, () => ({ kind: "text", text: "a" }));
  assert.deepEqual(canonicalizeServerContent({ formatVersion: 1, segments }), {
    formatVersion: 1,
    segments: [{ kind: "text", text: "a".repeat(32) }],
  });

  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [...segments, { kind: "text", text: "b" }],
      }),
    hasCode("invalid_content"),
  );
});

test("enforces 300 Unicode scalars including astral characters", () => {
  const astral = "😀"; // one scalar, two UTF-16 code units
  assert.equal(Array.from(astral).length, 1);

  const max = astral.repeat(300);
  assert.deepEqual(canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: max }] }), {
    formatVersion: 1,
    segments: [{ kind: "text", text: max }],
  });

  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [{ kind: "text", text: max + "x" }],
      }),
    hasCode("invalid_content"),
  );

  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [
          { kind: "text", text: "a".repeat(200) },
          { kind: "text", text: "b".repeat(101) },
        ],
      }),
    hasCode("invalid_content"),
  );
});

test("rejects blank-only content after normalize/trim", () => {
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: "   " }] }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [
          { kind: "text", text: "\r\n" },
          { kind: "text", text: " \t " },
        ],
      }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: 1, segments: [] }),
    hasCode("invalid_content"),
  );
});

test("rejects NUL/C0/C1/bidi/format characters but allows LF", () => {
  const allowed = canonicalizeServerContent({
    formatVersion: 1,
    segments: [{ kind: "text", text: "line1\nline2" }],
  });
  assert.deepEqual(allowed, { formatVersion: 1, segments: [{ kind: "text", text: "line1\nline2" }] });

  // CR is normalized to LF before control checks, so it is allowed as a newline.
  assert.deepEqual(
    canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: "x\ry" }] }),
    { formatVersion: 1, segments: [{ kind: "text", text: "x\ny" }] },
  );
  assert.deepEqual(
    canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: "x\r\ny" }] }),
    { formatVersion: 1, segments: [{ kind: "text", text: "x\ny" }] },
  );

  const disallowed = [
    "\u0000", // NUL
    "\u0001", // C0
    "\u0009", // TAB (C0; not LF)
    "\u007f", // DEL
    "\u0085", // C1 NEL
    "\u009f", // C1
    "\u202a", // LRE
    "\u202e", // RLO
    "\u2066", // LRI
    "\u2069", // PDI
    "\u200b", // ZWSP
    "\u200e", // LRM
    "\ufeff", // BOM
    "\u00ad", // soft hyphen
    "\u206a", // inhibit symmetric swapping (format)
    "\u206b", // activate symmetric swapping
    "\u206c", // inhibit Arabic form shaping
    "\u206d", // activate Arabic form shaping
    "\u206e", // national digit shapes
    "\u206f", // nominal digit shapes
    "\ufe00", // variation selector-1
    "\ufe0f", // variation selector-16
    "\u{e0001}", // language tag
    "\u{e0020}", // tag space
    "\u{e007f}", // cancel tag
    "\u{e0100}", // variation selector-17
    "\ud800", // unpaired high surrogate
    "\udc00", // unpaired low surrogate
  ];

  for (const ch of disallowed) {
    assert.throws(
      () => canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: `a${ch}b` }] }),
      hasCode("invalid_content"),
      `expected rejection for U+${ch.codePointAt(0)!.toString(16)}`,
    );
  }
});

test("allows literal HTML/Markdown/BBCode/template text as plain content", () => {
  const raw =
    '<b>bold</b> **md** [b]bb[/b] {{item:card}} and <script>alert(1)</script>';
  assert.deepEqual(canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: raw }] }), {
    formatVersion: 1,
    segments: [{ kind: "text", text: raw }],
  });
});

test("renderPlainTextFallback joins text segments", () => {
  const content = canonicalizeServerContent({
    formatVersion: 1,
    segments: [
      { kind: "text", text: "hello " },
      { kind: "text", text: "world" },
    ],
  });
  assert.equal(renderPlainTextFallback(content), "hello world");
});

test("renderPlainTextFallback uses generic rich placeholders without leaking sender data", () => {
  const content = canonicalizeChatContent(
    {
      formatVersion: 1,
      segments: [
        { kind: "text", text: "look " },
        { kind: "emoji", emojiId: "thumbs-up" },
        { kind: "item_ref", itemType: "card", modelId: "Secret.ModCard", upgradeLevel: 2 },
        { kind: "item_ref", itemType: "relic", modelId: "Secret.ModRelic" },
        { kind: "item_ref", itemType: "potion", modelId: "Secret.ModPotion" },
      ],
    },
    allRichFeatures,
  );
  const fallback = renderPlainTextFallback(content);
  assert.equal(fallback, "look [Emoji][Card][Relic][Potion]");
  assert.equal(fallback.includes("Secret"), false);
  assert.equal(fallback.includes("ModCard"), false);
  assert.equal(fallback.includes("Anchor"), false);
});

test("reserved combat models serialize deterministically with generic fallback", () => {
  const content: ChatContent = {
    formatVersion: 1,
    segments: [
      {
        kind: "power_state",
        modelId: "Secret.ModStrength",
        amount: -2,
        roomSessionId: "session-1",
        ownerPlayerNetId: "net:owner",
        applierPlayerNetId: "net:applier",
      },
      {
        kind: "target_ref",
        targetKind: "player",
        targetKey: "net:target",
        roomSessionId: "session-1",
      },
      {
        kind: "target_ref",
        targetKind: "monster",
        targetKey: "monster-1",
        roomSessionId: "session-1",
      },
    ],
  };

  assert.equal(renderPlainTextFallback(content), "[Power][Player][Monster]");
  assert.equal(
    deterministicContentJson(content),
    '{"formatVersion":1,"segments":[{"kind":"power_state","modelId":"Secret.ModStrength","amount":-2,"roomSessionId":"session-1","ownerPlayerNetId":"net:owner","applierPlayerNetId":"net:applier"},{"kind":"target_ref","targetKind":"player","targetKey":"net:target","roomSessionId":"session-1"},{"kind":"target_ref","targetKind":"monster","targetKey":"monster-1","roomSessionId":"session-1"}]}',
  );
});

test("deterministicContentJson uses fixed field order", () => {
  const content = canonicalizeServerContent({
    formatVersion: 1,
    segments: [{ kind: "text", text: "hi" }],
  });
  assert.equal(
    deterministicContentJson(content),
    '{"formatVersion":1,"segments":[{"kind":"text","text":"hi"}]}',
  );

  const rich = canonicalizeChatContent(
    {
      formatVersion: 1,
      segments: [
        { emojiId: "heart", kind: "emoji" },
        { modelId: "MegaCrit.Strike", upgradeLevel: 1, itemType: "card", kind: "item_ref" },
        { modelId: "MegaCrit.Defend", itemType: "card", kind: "item_ref" },
        { modelId: "MegaCrit.Anchor", itemType: "relic", kind: "item_ref" },
        { modelId: "MegaCrit.FirePotion", itemType: "potion", kind: "item_ref" },
      ],
    },
    allRichFeatures,
  );
  assert.equal(
    deterministicContentJson(rich),
    '{"formatVersion":1,"segments":[{"kind":"emoji","emojiId":"heart"},{"kind":"item_ref","itemType":"card","modelId":"MegaCrit.Strike","upgradeLevel":1},{"kind":"item_ref","itemType":"card","modelId":"MegaCrit.Defend"},{"kind":"item_ref","itemType":"relic","modelId":"MegaCrit.Anchor"},{"kind":"item_ref","itemType":"potion","modelId":"MegaCrit.FirePotion"}]}',
  );
});

test("utf8JsonBytes measures UTF-8 JSON size", () => {
  assert.equal(utf8JsonBytes({ a: "é" }), Buffer.byteLength(JSON.stringify({ a: "é" }), "utf8"));
});

test("assertWireBudget accepts exact 8192 and rejects 8193 worst-case projections", () => {
  // Phase-1 text (300 scalars) never reaches 8 KiB by itself. Budget enforcement still
  // measures the max of chat_ack / chat_message / one-message chat_snapshot_chunk so a
  // message that cannot be replayed in one chunk is rejected. Use production
  // measureChatWireBytes / projectChatWireEnvelopes and pad plainTextFallback only for
  // exact boundaries (content text + fallback both padded would step by 2).
  const content = textContent("hello");
  const baseMessage = sampleMessage(content);

  const baseBytes = measureChatWireBytes(baseMessage);
  assert.ok(baseBytes < 8192);
  assertWireBudget(baseMessage, 8192);

  const { ack, chatMessage, snapshotChunk } = projectChatWireEnvelopes(baseMessage);
  const ackBytes = utf8JsonBytes(ack);
  const messageBytes = utf8JsonBytes(chatMessage);
  const chunkBytes = utf8JsonBytes(snapshotChunk);
  assert.equal(measureChatWireBytes(baseMessage), Math.max(ackBytes, messageBytes, chunkBytes));
  // Snapshot chunk wraps messages[] + snapshotId, so it is strictly larger than chat_message.
  // Ack includes clientMessageId but still typically smaller than the chunk envelope.
  assert.ok(chunkBytes > messageBytes, `chunkBytes (${chunkBytes}) should exceed messageBytes (${messageBytes})`);
  assert.ok(chunkBytes > ackBytes, `chunkBytes (${chunkBytes}) should exceed ackBytes (${ackBytes})`);
  assert.ok(ackBytes > 0 && messageBytes > 0);

  // Exact envelope-level 8192 / 8193 boundaries via raw wire values.
  const exact8192 = makeExactByteObject(8192);
  assert.equal(utf8JsonBytes(exact8192), 8192);
  assertWireBudget(exact8192, 8192);

  const exact8193 = makeExactByteObject(8193);
  assert.equal(utf8JsonBytes(exact8193), 8193);
  assert.throws(() => assertWireBudget(exact8193, 8192), hasCode("invalid_content"));

  // Pad only plainTextFallback so each ASCII char adds one projected byte.
  // Realistic path: content is fixed; fallback is intentionally longer only for size probes.
  const exactProjected = padFallbackToExactBytes(baseMessage, 8192);
  assert.ok(exactProjected, "expected exact 8192 projected message");
  assert.equal(measureChatWireBytes(exactProjected), 8192);
  assertWireBudget(exactProjected, 8192);

  const exactProjectedOver = padFallbackToExactBytes(baseMessage, 8193);
  assert.ok(exactProjectedOver, "expected exact 8193 projected message");
  assert.equal(measureChatWireBytes(exactProjectedOver), 8193);
  assert.throws(() => assertWireBudget(exactProjectedOver, 8192), hasCode("invalid_content"));

  // Consistent plainTextFallback path: re-derive fallback from content (realistic production shape).
  const realistic = sampleMessage(textContent("hello world"));
  assert.equal(realistic.plainTextFallback, renderPlainTextFallback(realistic.content));
  assertWireBudget(realistic, 8192);
  assert.ok(measureChatWireBytes(realistic) < 8192);
});

test("assertWireBudget measures exact 8192 and 8193 actual mixed message envelopes", () => {
  const content = canonicalizeChatContent(
    {
      formatVersion: 1,
      segments: [
        { kind: "text", text: "look " },
        { kind: "emoji", emojiId: "thumbs-up" },
        { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", upgradeLevel: 1 },
      ],
    },
    allRichFeatures,
  );
  const base = sampleMessage(content, "");
  assert.equal(base.plainTextFallback, "look [Emoji][Card]");

  const exact8192 = padSenderNameToExactBytes(base, 8192);
  assert.ok(exact8192);
  const envelopes8192 = projectChatWireEnvelopes(exact8192);
  const sizes8192 = [
    utf8JsonBytes(envelopes8192.ack),
    utf8JsonBytes(envelopes8192.chatMessage),
    utf8JsonBytes(envelopes8192.snapshotChunk),
  ];
  assert.equal(
    measureChatWireBytes(exact8192),
    Math.max(...sizes8192),
  );
  assert.equal(measureChatWireBytes(exact8192), 8192);
  assert.equal(utf8JsonBytes(envelopes8192.snapshotChunk), 8192);
  assert.ok(sizes8192.every((size) => size <= 8192));
  assertWireBudget(exact8192, 8192);

  const exact8193 = padSenderNameToExactBytes(base, 8193);
  assert.ok(exact8193);
  assert.equal(measureChatWireBytes(exact8193), 8193);
  assert.equal(utf8JsonBytes(projectChatWireEnvelopes(exact8193).snapshotChunk), 8193);
  assert.throws(() => assertWireBudget(exact8193, 8192), hasCode("invalid_content"));
});

test("wire budget reserves the worst configured snapshot chunk index", () => {
  const content = canonicalizeChatContent(
    {
      formatVersion: 1,
      segments: [
        { kind: "text", text: "look " },
        { kind: "emoji", emojiId: "thumbs-up" },
        { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", upgradeLevel: 1 },
      ],
    },
    allRichFeatures,
  );
  const base = sampleMessage(content, "");
  const oldBoundary = padSenderNameToExactBytesAtSnapshotIndex(base, 8192, 0);
  assert.ok(oldBoundary);
  assert.equal(measureChatWireBytesAtSnapshotIndex(oldBoundary, 0), 8192);
  assert.equal(measureChatWireBytesAtSnapshotIndex(oldBoundary, 10), 8193);
  assert.equal(measureChatWireBytesAtSnapshotIndex(oldBoundary, 999), 8194);
  assert.throws(() => assertWireBudget(oldBoundary, 8192), hasCode("invalid_content"));
  assert.equal(
    (projectChatWireEnvelopes(oldBoundary).snapshotChunk as { chunkIndex: unknown }).chunkIndex,
    999,
  );

  const correctedBoundary = padSenderNameToExactBytes(base, 8192);
  assert.ok(correctedBoundary);
  assert.equal(measureChatWireBytes(correctedBoundary), 8192);
  assertWireBudget(correctedBoundary, 8192);
});

function makeExactByteObject(targetBytes: number): { p: string } {
  // {"p":"..."} => 8 overhead bytes for ASCII payload.
  const overhead = Buffer.byteLength(JSON.stringify({ p: "" }), "utf8");
  assert.ok(targetBytes >= overhead);
  return { p: "x".repeat(targetBytes - overhead) };
}

function padFallbackToExactBytes(
  base: CanonicalChatMessage,
  targetBytes: number,
): CanonicalChatMessage | null {
  const baseSize = measureChatWireBytes(base);
  if (baseSize > targetBytes) {
    return null;
  }
  const pad = targetBytes - baseSize;
  const message: CanonicalChatMessage = {
    ...base,
    plainTextFallback: `${base.plainTextFallback}${"x".repeat(pad)}`,
  };
  return measureChatWireBytes(message) === targetBytes ? message : null;
}

function padSenderNameToExactBytes(
  base: CanonicalChatMessage,
  targetBytes: number,
): CanonicalChatMessage | null {
  const baseSize = measureChatWireBytes(base);
  if (baseSize > targetBytes) {
    return null;
  }
  const message: CanonicalChatMessage = {
    ...base,
    senderName: "x".repeat(targetBytes - baseSize),
  };
  return measureChatWireBytes(message) === targetBytes ? message : null;
}

function measureChatWireBytesAtSnapshotIndex(
  message: CanonicalChatMessage,
  chunkIndex: number,
): number {
  const { ack, chatMessage, snapshotChunk } = projectChatWireEnvelopes(message);
  const indexedSnapshotChunk = {
    ...(snapshotChunk as Record<string, unknown>),
    chunkIndex,
  };
  return Math.max(
    utf8JsonBytes(ack),
    utf8JsonBytes(chatMessage),
    utf8JsonBytes(indexedSnapshotChunk),
  );
}

function padSenderNameToExactBytesAtSnapshotIndex(
  base: CanonicalChatMessage,
  targetBytes: number,
  chunkIndex: number,
): CanonicalChatMessage | null {
  const baseSize = measureChatWireBytesAtSnapshotIndex(base, chunkIndex);
  if (baseSize > targetBytes) {
    return null;
  }
  const message: CanonicalChatMessage = {
    ...base,
    senderName: "x".repeat(targetBytes - baseSize),
  };
  return measureChatWireBytesAtSnapshotIndex(message, chunkIndex) === targetBytes ? message : null;
}

function withPollutedObjectPrototype(
  properties: Record<string, unknown>,
  action: () => void,
): void {
  const originals = new Map<string, PropertyDescriptor | undefined>();
  const keys = Object.keys(properties);
  const replaced: string[] = [];
  try {
    for (const key of keys) {
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
      if (original === undefined) {
        Reflect.deleteProperty(Object.prototype, key);
      } else {
        Object.defineProperty(Object.prototype, key, original);
      }
    }
  }
}
