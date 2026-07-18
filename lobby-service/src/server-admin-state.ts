import { randomUUID } from "node:crypto";
import { mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import type { ChatFeatureGovernance } from "./chat/feature-resolver.js";

export type ServerAnnouncementType = "update" | "event" | "warning" | "info";

export interface ServerAnnouncement {
  id: string;
  type: ServerAnnouncementType;
  title: string;
  dateLabel: string;
  body: string;
  enabled: boolean;
}

export interface ServerAdminState {
  displayName: string;
  publicListingEnabled: boolean;
  modSyncEnabled: boolean;
  bandwidthCapacityMbps: number | null;
  probePeak7dCapacityMbps: number | null;
  resolvedCapacityMbps: number | null;
  capacitySource: string;
  announcements: ServerAnnouncement[];
  extraMetadata: Record<string, string>;
  chatFeatures: ChatFeatureGovernance;
}

export interface ServerAdminSettingsView {
  displayName: string;
  publicListingEnabled: boolean;
  modSyncEnabled: boolean;
  bandwidthCapacityMbps: number | null;
  probePeak7dCapacityMbps: number | null;
  resolvedCapacityMbps: number | null;
  capacitySource: string;
  announcements: ServerAnnouncement[];
  extraMetadata: Record<string, string>;
  chatFeatures: ChatFeatureGovernance;
}

export interface ServerAdminStateDefaults {
  publicListingEnabledDefault?: boolean;
  modSyncEnabledDefault?: boolean;
  chatFeaturesDefault: ChatFeatureGovernance;
}

export class ServerAdminStateStore {
  private readonly stateFilePath: string;
  private state: ServerAdminState;

  constructor(stateFilePath: string, defaults: ServerAdminStateDefaults) {
    this.stateFilePath = resolve(stateFilePath);
    this.state = loadState(
      this.stateFilePath,
      defaults.publicListingEnabledDefault ?? true,
      defaults.modSyncEnabledDefault ?? true,
      defaults.chatFeaturesDefault,
    );
  }

  getState() {
    return cloneState(this.state);
  }

  getSettingsView(): ServerAdminSettingsView {
    return {
      displayName: this.state.displayName,
      publicListingEnabled: this.state.publicListingEnabled,
      modSyncEnabled: this.state.modSyncEnabled,
      bandwidthCapacityMbps: this.state.bandwidthCapacityMbps,
      probePeak7dCapacityMbps: this.state.probePeak7dCapacityMbps,
      resolvedCapacityMbps: this.state.resolvedCapacityMbps,
      capacitySource: this.state.capacitySource,
      announcements: this.state.announcements.map(cloneAnnouncement),
      extraMetadata: { ...this.state.extraMetadata },
      chatFeatures: { ...this.state.chatFeatures },
    };
  }

  updateSettings(next: {
    displayName?: string;
    publicListingEnabled?: boolean;
    modSyncEnabled?: boolean;
    bandwidthCapacityMbps?: number | null | undefined;
    announcements?: unknown;
    extraMetadata?: Record<string, string> | undefined;
    chatFeatures?: Partial<ChatFeatureGovernance> | undefined;
  }) {
    const staged = cloneState(this.state);
    if (next.displayName !== undefined) {
      staged.displayName = sanitizeText(next.displayName, 64);
    }
    if (next.publicListingEnabled !== undefined) {
      staged.publicListingEnabled = next.publicListingEnabled;
    }
    if (next.modSyncEnabled !== undefined) {
      staged.modSyncEnabled = next.modSyncEnabled;
    }
    if (next.bandwidthCapacityMbps !== undefined) {
      staged.bandwidthCapacityMbps = sanitizeOptionalMbps(next.bandwidthCapacityMbps);
    }
    if (next.announcements !== undefined) {
      staged.announcements = sanitizeAnnouncements(next.announcements);
    }
    if (next.extraMetadata !== undefined) {
      staged.extraMetadata = sanitizeMetadata(next.extraMetadata);
    }
    if (next.chatFeatures !== undefined) {
      staged.chatFeatures = mergeChatFeatures(staged.chatFeatures, next.chatFeatures);
    }
    this.save(staged);
    this.state = staged;
    return this.getSettingsView();
  }

  getPublicAnnouncements(): ServerAnnouncement[] {
    return this.state.announcements
      .filter((announcement) => announcement.enabled)
      .map(cloneAnnouncement);
  }

  patch(patch: Partial<Omit<ServerAdminState, "chatFeatures">> & {
    chatFeatures?: Partial<ChatFeatureGovernance>;
  }) {
    const staged: ServerAdminState = {
      ...this.state,
      ...patch,
      displayName: patch.displayName != null ? sanitizeText(patch.displayName, 64) : this.state.displayName,
      bandwidthCapacityMbps: patch.bandwidthCapacityMbps !== undefined
        ? sanitizeOptionalMbps(patch.bandwidthCapacityMbps)
        : this.state.bandwidthCapacityMbps,
      probePeak7dCapacityMbps: patch.probePeak7dCapacityMbps !== undefined
        ? sanitizeOptionalMbps(patch.probePeak7dCapacityMbps)
        : this.state.probePeak7dCapacityMbps,
      resolvedCapacityMbps: patch.resolvedCapacityMbps !== undefined
        ? sanitizeOptionalMbps(patch.resolvedCapacityMbps)
        : this.state.resolvedCapacityMbps,
      capacitySource: patch.capacitySource != null
        ? sanitizeCapacitySource(patch.capacitySource)
        : this.state.capacitySource,
      announcements: patch.announcements != null
        ? sanitizeAnnouncements(patch.announcements)
        : this.state.announcements,
      extraMetadata: patch.extraMetadata != null ? sanitizeMetadata(patch.extraMetadata) : this.state.extraMetadata,
      chatFeatures: patch.chatFeatures != null
        ? mergeChatFeatures(this.state.chatFeatures, patch.chatFeatures)
        : this.state.chatFeatures,
    };
    this.save(staged);
    this.state = staged;
    return this.getState();
  }

  private save(next: ServerAdminState) {
    mkdirSync(dirname(this.stateFilePath), { recursive: true });
    const tempFilePath = `${this.stateFilePath}.tmp`;
    writeFileSync(tempFilePath, JSON.stringify(next, null, 2), "utf8");
    renameSync(tempFilePath, this.stateFilePath);
  }
}

function loadState(
  path: string,
  publicListingDefault: boolean,
  modSyncEnabledDefault: boolean,
  chatFeaturesDefault: ChatFeatureGovernance,
): ServerAdminState {
  try {
    const raw = readFileSync(path, "utf8");
    return normalizeState(
      JSON.parse(raw),
      publicListingDefault,
      modSyncEnabledDefault,
      chatFeaturesDefault,
    );
  } catch {
    return {
      displayName: "",
      publicListingEnabled: publicListingDefault,
      modSyncEnabled: modSyncEnabledDefault,
      bandwidthCapacityMbps: null,
      probePeak7dCapacityMbps: null,
      resolvedCapacityMbps: null,
      capacitySource: "unknown",
      announcements: [],
      extraMetadata: {},
      chatFeatures: { ...chatFeaturesDefault },
    };
  }
}

function normalizeState(
  value: unknown,
  publicListingDefault: boolean,
  modSyncEnabledDefault: boolean,
  chatFeaturesDefault: ChatFeatureGovernance,
): ServerAdminState {
  const data = value && typeof value === "object" ? value as Record<string, unknown> : {};
  return {
    displayName: sanitizeText(typeof data.displayName === "string" ? data.displayName : "", 64),
    publicListingEnabled: typeof data.publicListingEnabled === "boolean"
      ? data.publicListingEnabled
      : publicListingDefault,
    modSyncEnabled: typeof data.modSyncEnabled === "boolean"
      ? data.modSyncEnabled
      : modSyncEnabledDefault,
    bandwidthCapacityMbps: sanitizeOptionalMbps(typeof data.bandwidthCapacityMbps === "number" ? data.bandwidthCapacityMbps : null),
    probePeak7dCapacityMbps: sanitizeOptionalMbps(typeof data.probePeak7dCapacityMbps === "number" ? data.probePeak7dCapacityMbps : null),
    resolvedCapacityMbps: sanitizeOptionalMbps(typeof data.resolvedCapacityMbps === "number" ? data.resolvedCapacityMbps : null),
    capacitySource: sanitizeCapacitySource(typeof data.capacitySource === "string" ? data.capacitySource : "unknown"),
    announcements: sanitizeAnnouncements(data.announcements),
    extraMetadata: sanitizeMetadata(data.extraMetadata),
    chatFeatures: normalizeChatFeatures(data.chatFeatures, chatFeaturesDefault),
  };
}

function normalizeChatFeatures(
  value: unknown,
  defaults: ChatFeatureGovernance,
): ChatFeatureGovernance {
  const data = value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {};
  return {
    serverChatEnabled: booleanOrDefault(data.serverChatEnabled, defaults.serverChatEnabled),
    richContentEnabled: booleanOrDefault(data.richContentEnabled, defaults.richContentEnabled),
    emojiEnabled: booleanOrDefault(data.emojiEnabled, defaults.emojiEnabled),
    itemRefsEnabled: booleanOrDefault(data.itemRefsEnabled, defaults.itemRefsEnabled),
    roomChatV2Enabled: booleanOrDefault(data.roomChatV2Enabled, defaults.roomChatV2Enabled),
    roomCombatRefsEnabled: booleanOrDefault(data.roomCombatRefsEnabled, defaults.roomCombatRefsEnabled),
  };
}

function mergeChatFeatures(
  current: ChatFeatureGovernance,
  patch: Partial<ChatFeatureGovernance>,
): ChatFeatureGovernance {
  return normalizeChatFeatures(patch, current);
}

function booleanOrDefault(value: unknown, fallback: boolean): boolean {
  return typeof value === "boolean" ? value : fallback;
}

function sanitizeMetadata(value: unknown) {
  if (!value || typeof value !== "object") {
    return {};
  }

  const output: Record<string, string> = {};
  for (const [key, entryValue] of Object.entries(value as Record<string, unknown>)) {
    if (typeof entryValue !== "string") {
      continue;
    }

    const normalizedKey = sanitizeText(key, 64);
    const normalizedValue = sanitizeText(entryValue, 256);
    if (!normalizedKey || !normalizedValue) {
      continue;
    }

    output[normalizedKey] = normalizedValue;
  }

  return output;
}

function sanitizeText(value: string, maxLength: number) {
  return value.trim().slice(0, maxLength);
}

function sanitizeOptionalMbps(value: number | null | undefined) {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) {
    return null;
  }

  return Math.round(value * 100) / 100;
}

function sanitizeCapacitySource(value: string) {
  return value === "manual" || value === "probe_peak_7d" ? value : "unknown";
}

function sanitizeAnnouncements(value: unknown): ServerAnnouncement[] {
  if (!Array.isArray(value)) {
    return [];
  }

  const output: ServerAnnouncement[] = [];
  const seenIds = new Set<string>();
  for (const [index, entry] of value.entries()) {
    if (!entry || typeof entry !== "object") {
      continue;
    }

    const candidate = entry as Record<string, unknown>;
    const title = sanitizeText(typeof candidate.title === "string" ? candidate.title : "", 64);
    const body = sanitizeText(typeof candidate.body === "string" ? candidate.body : "", 280);
    if (!title && !body) {
      continue;
    }

    const normalizedTitle = title || inferAnnouncementTitle(body);
    const normalizedBody = body || normalizedTitle;
    const baseId = sanitizeText(typeof candidate.id === "string" ? candidate.id : "", 64) || `announcement-${index + 1}`;
    output.push({
      id: ensureUniqueAnnouncementId(baseId, seenIds),
      type: sanitizeAnnouncementType(candidate.type),
      title: normalizedTitle,
      dateLabel: sanitizeText(typeof candidate.dateLabel === "string" ? candidate.dateLabel : "", 32),
      body: normalizedBody,
      enabled: candidate.enabled !== false,
    });
    if (output.length >= 12) {
      break;
    }
  }

  return output;
}

function sanitizeAnnouncementType(value: unknown): ServerAnnouncementType {
  return value === "update" || value === "event" || value === "warning" || value === "info"
    ? value
    : "info";
}

function inferAnnouncementTitle(body: string) {
  const trimmed = body.trim();
  if (!trimmed) {
    return "公告";
  }

  return trimmed.length <= 18 ? trimmed : `${trimmed.slice(0, 18)}...`;
}

function ensureUniqueAnnouncementId(baseId: string, seenIds: Set<string>) {
  let nextId = baseId;
  let attempt = 1;
  while (!nextId || seenIds.has(nextId)) {
    nextId = `${baseId || randomUUID()}-${attempt}`;
    attempt++;
  }

  seenIds.add(nextId);
  return nextId;
}

function cloneAnnouncement(value: ServerAnnouncement): ServerAnnouncement {
  return { ...value };
}

function cloneState<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}
