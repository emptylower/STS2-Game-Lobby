import type { ChatContent } from "./protocol.js";

export interface ChatFeatureVersions {
  richContentVersion: 0 | 1;
  emojiSetVersion: 0 | 1;
  itemRefVersion: 0 | 1;
  combatRefVersion: 0 | 1;
}

export interface ResolveFeatureInput {
  channel: "server" | "room";
  compiled: ChatFeatureVersions;
  configured: ChatFeatureVersions;
  admin?: Partial<ChatFeatureVersions>;
  channelEnabled: boolean;
  roomV2Enabled?: boolean;
  sender?: ChatFeatureVersions;
  receiver?: ChatFeatureVersions;
}

export const NO_CHAT_FEATURES: Readonly<ChatFeatureVersions> = Object.freeze({
  richContentVersion: 0,
  emojiSetVersion: 0,
  itemRefVersion: 0,
  combatRefVersion: 0,
});

export const PHASE_3_CHAT_FEATURES: Readonly<ChatFeatureVersions> = Object.freeze({
  richContentVersion: 1,
  emojiSetVersion: 1,
  itemRefVersion: 1,
  combatRefVersion: 0,
});

const ALL_DECLARED_VERSIONS: Readonly<ChatFeatureVersions> = Object.freeze({
  richContentVersion: 1,
  emojiSetVersion: 1,
  itemRefVersion: 1,
  combatRefVersion: 1,
});

export function resolveEnabledFeatures(input: ResolveFeatureInput): ChatFeatureVersions {
  if (!input.channelEnabled || (input.channel === "room" && input.roomV2Enabled === false)) {
    return { ...NO_CHAT_FEATURES };
  }

  const sender = input.sender ?? ALL_DECLARED_VERSIONS;
  const receiver = input.receiver ?? ALL_DECLARED_VERSIONS;
  const enabled = (key: keyof ChatFeatureVersions): 0 | 1 => {
    const operatorValue = input.admin?.[key] ?? input.configured[key];
    return input.compiled[key] === 1
      && operatorValue === 1
      && sender[key] === 1
      && receiver[key] === 1
      ? 1
      : 0;
  };

  const richContentVersion = enabled("richContentVersion");
  if (richContentVersion === 0) {
    return { ...NO_CHAT_FEATURES };
  }

  return {
    richContentVersion,
    emojiSetVersion: enabled("emojiSetVersion"),
    itemRefVersion: enabled("itemRefVersion"),
    // Combat references are reserved for Phase 4 and never enabled by this resolver.
    combatRefVersion: 0,
  };
}

export function supportsContent(
  content: ChatContent,
  features: ChatFeatureVersions,
): boolean {
  for (const segment of content.segments) {
    if (segment.kind === "emoji") {
      if (features.richContentVersion !== 1 || features.emojiSetVersion !== 1) {
        return false;
      }
    } else if (segment.kind === "item_ref") {
      if (features.richContentVersion !== 1 || features.itemRefVersion !== 1) {
        return false;
      }
    }
  }
  return true;
}
