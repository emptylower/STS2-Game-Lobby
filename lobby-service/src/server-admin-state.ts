import { randomUUID } from "node:crypto";
import { mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";

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
  bandwidthCapacityMbps: number | null;
  probePeak7dCapacityMbps: number | null;
  resolvedCapacityMbps: number | null;
  capacitySource: string;
  announcements: ServerAnnouncement[];
  extraMetadata: Record<string, string>;
}

export interface ServerAdminSettingsView {
  displayName: string;
  publicListingEnabled: boolean;
  bandwidthCapacityMbps: number | null;
  probePeak7dCapacityMbps: number | null;
  resolvedCapacityMbps: number | null;
  capacitySource: string;
  announcements: ServerAnnouncement[];
  extraMetadata: Record<string, string>;
}

export class ServerAdminStateStore {
  private readonly stateFilePath: string;
  private state: ServerAdminState;

  constructor(stateFilePath: string, defaults?: { publicListingEnabledDefault?: boolean }) {
    this.stateFilePath = resolve(stateFilePath);
    this.state = loadState(this.stateFilePath, defaults?.publicListingEnabledDefault ?? true);
  }

  getState() {
    return cloneState(this.state);
  }

  getSettingsView(): ServerAdminSettingsView {
    return {
      displayName: this.state.displayName,
      publicListingEnabled: this.state.publicListingEnabled,
      bandwidthCapacityMbps: this.state.bandwidthCapacityMbps,
      probePeak7dCapacityMbps: this.state.probePeak7dCapacityMbps,
      resolvedCapacityMbps: this.state.resolvedCapacityMbps,
      capacitySource: this.state.capacitySource,
      announcements: this.state.announcements.map(cloneAnnouncement),
      extraMetadata: { ...this.state.extraMetadata },
    };
  }

  updateSettings(next: {
    displayName: string;
    publicListingEnabled: boolean;
    bandwidthCapacityMbps?: number | null | undefined;
    announcements?: unknown;
    extraMetadata?: Record<string, string> | undefined;
  }) {
    this.state.displayName = sanitizeText(next.displayName, 64);
    this.state.publicListingEnabled = next.publicListingEnabled;
    if (next.bandwidthCapacityMbps !== undefined) {
      this.state.bandwidthCapacityMbps = sanitizeOptionalMbps(next.bandwidthCapacityMbps);
    }
    if (next.announcements !== undefined) {
      this.state.announcements = sanitizeAnnouncements(next.announcements);
    }
    if (next.extraMetadata) {
      this.state.extraMetadata = sanitizeMetadata(next.extraMetadata);
    }
    this.save();
    return this.getSettingsView();
  }

  getPublicAnnouncements(): ServerAnnouncement[] {
    return this.state.announcements
      .filter((announcement) => announcement.enabled)
      .map(cloneAnnouncement);
  }

  patch(patch: Partial<ServerAdminState>) {
    this.state = {
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
    };
    this.save();
    return this.getState();
  }

  private save() {
    mkdirSync(dirname(this.stateFilePath), { recursive: true });
    const tempFilePath = `${this.stateFilePath}.tmp`;
    writeFileSync(tempFilePath, JSON.stringify(this.state, null, 2), "utf8");
    renameSync(tempFilePath, this.stateFilePath);
  }
}

function loadState(path: string, publicListingDefault: boolean): ServerAdminState {
  try {
    const raw = readFileSync(path, "utf8");
    return normalizeState(JSON.parse(raw), publicListingDefault);
  } catch {
    return {
      displayName: "",
      publicListingEnabled: publicListingDefault,
      bandwidthCapacityMbps: null,
      probePeak7dCapacityMbps: null,
      resolvedCapacityMbps: null,
      capacitySource: "unknown",
      announcements: [],
      extraMetadata: {},
    };
  }
}

function normalizeState(value: unknown, publicListingDefault: boolean): ServerAdminState {
  const data = value && typeof value === "object" ? value as Record<string, unknown> : {};
  return {
    displayName: sanitizeText(typeof data.displayName === "string" ? data.displayName : "", 64),
    publicListingEnabled: typeof data.publicListingEnabled === "boolean"
      ? data.publicListingEnabled
      : publicListingDefault,
    bandwidthCapacityMbps: sanitizeOptionalMbps(typeof data.bandwidthCapacityMbps === "number" ? data.bandwidthCapacityMbps : null),
    probePeak7dCapacityMbps: sanitizeOptionalMbps(typeof data.probePeak7dCapacityMbps === "number" ? data.probePeak7dCapacityMbps : null),
    resolvedCapacityMbps: sanitizeOptionalMbps(typeof data.resolvedCapacityMbps === "number" ? data.resolvedCapacityMbps : null),
    capacitySource: sanitizeCapacitySource(typeof data.capacitySource === "string" ? data.capacitySource : "unknown"),
    announcements: sanitizeAnnouncements(data.announcements),
    extraMetadata: sanitizeMetadata(data.extraMetadata),
  };
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
