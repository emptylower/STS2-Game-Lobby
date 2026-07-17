import {
  MOD_SYNC_MAX_DEPENDENCIES,
  MOD_SYNC_MAX_DESCRIPTORS,
  MOD_SYNC_MAX_ID_CHARACTERS,
  MOD_SYNC_MAX_PAYLOAD_BYTES,
  MOD_SYNC_MAX_VERSION_CHARACTERS,
  type LobbyModDescriptor,
  type LobbyModRole,
  type LobbyModSource,
} from "./protocol.js";

const DESCRIPTOR_KEYS = new Set(["id", "version", "role", "source", "workshopFileId", "dependencies"]);
const ROLES = new Set<LobbyModRole>(["gameplay", "dependency"]);
const SOURCES = new Set<LobbyModSource>(["steam_workshop", "mods_directory", "unknown"]);
const RESERVED_PREFIXES = ["sts2_lan_connect", "lan_connect.", "sts2-lan-connect."];
const WORKSHOP_ID_PATTERN = /^[0-9]{1,20}$/;

export class ModSyncValidationError extends Error {
  readonly code = "invalid_mod_inventory";

  constructor(message: string) {
    super(message);
    this.name = "ModSyncValidationError";
  }
}

export interface ModInventoryValidationOptions {
  maxDescriptors?: number;
  maxPayloadBytes?: number;
}

export function validateModInventory(
  value: unknown,
  options: ModInventoryValidationOptions = {},
): LobbyModDescriptor[] {
  if (!Array.isArray(value)) {
    throw new ModSyncValidationError("Mod inventory must be an array.");
  }
  const maxDescriptors = options.maxDescriptors ?? MOD_SYNC_MAX_DESCRIPTORS;
  if (value.length > maxDescriptors) {
    throw new ModSyncValidationError(`Mod inventory cannot exceed ${maxDescriptors} descriptors.`);
  }

  const ids = new Set<string>();
  const canonical = value.map((candidate, index) => canonicalizeDescriptor(candidate, index, ids));
  canonical.sort((left, right) => ordinalCompare(left.id, right.id));
  const maxPayloadBytes = options.maxPayloadBytes ?? MOD_SYNC_MAX_PAYLOAD_BYTES;
  if (Buffer.byteLength(JSON.stringify(canonical), "utf8") > maxPayloadBytes) {
    throw new ModSyncValidationError(`Canonical mod inventory cannot exceed ${maxPayloadBytes} bytes.`);
  }
  return canonical;
}

export function canonicalModInventoryJson(value: unknown): string {
  return JSON.stringify(validateModInventory(value));
}

function canonicalizeDescriptor(value: unknown, index: number, ids: Set<string>): LobbyModDescriptor {
  if (!isPlainObject(value)) {
    throw new ModSyncValidationError(`Descriptor ${index} must be an object.`);
  }
  for (const key of Object.keys(value)) {
    if (!DESCRIPTOR_KEYS.has(key)) {
      throw new ModSyncValidationError(`Descriptor ${index} has an unknown field: ${key}`);
    }
  }
  for (const key of ["id", "version", "role", "source", "dependencies"]) {
    if (!Object.hasOwn(value, key)) {
      throw new ModSyncValidationError(`Descriptor ${index} is missing required field: ${key}`);
    }
  }

  const id = normalizeIdentifier(value.id, `Descriptor ${index} id`);
  if (ids.has(id)) {
    throw new ModSyncValidationError(`Duplicate mod identifier: ${id}`);
  }
  ids.add(id);
  if (typeof value.version !== "string") {
    throw new ModSyncValidationError(`Descriptor ${index} version must be a string.`);
  }
  const version = value.version.trim();
  validateText(version, MOD_SYNC_MAX_VERSION_CHARACTERS, `Descriptor ${index} version`, true);
  if (typeof value.role !== "string" || !ROLES.has(value.role as LobbyModRole)) {
    throw new ModSyncValidationError(`Descriptor ${index} has an invalid role.`);
  }
  if (typeof value.source !== "string" || !SOURCES.has(value.source as LobbyModSource)) {
    throw new ModSyncValidationError(`Descriptor ${index} has an invalid source.`);
  }
  if (!Array.isArray(value.dependencies)) {
    throw new ModSyncValidationError(`Descriptor ${index} dependencies must be an array.`);
  }
  if (value.dependencies.length > MOD_SYNC_MAX_DEPENDENCIES) {
    throw new ModSyncValidationError(`Descriptor ${index} has too many dependencies.`);
  }
  const dependencies = [...new Set(value.dependencies.map((dependency, dependencyIndex) =>
    normalizeIdentifier(dependency, `Descriptor ${index} dependency ${dependencyIndex}`)))]
    .sort(ordinalCompare);

  let workshopFileId: string | undefined;
  if (Object.hasOwn(value, "workshopFileId")) {
    if (typeof value.workshopFileId !== "string"
      || !WORKSHOP_ID_PATTERN.test(value.workshopFileId)
      || /^0+$/.test(value.workshopFileId)) {
      throw new ModSyncValidationError(`Descriptor ${index} has an invalid Workshop file id.`);
    }
    workshopFileId = value.workshopFileId;
  }

  const base = {
    id,
    version,
    role: value.role as LobbyModRole,
    source: value.source as LobbyModSource,
  };
  return workshopFileId === undefined
    ? { ...base, dependencies }
    : { ...base, workshopFileId, dependencies };
}

function normalizeIdentifier(value: unknown, label: string): string {
  if (typeof value !== "string") {
    throw new ModSyncValidationError(`${label} must be a string.`);
  }
  const normalized = value.trim();
  validateText(normalized, MOD_SYNC_MAX_ID_CHARACTERS, label, false);
  if (RESERVED_PREFIXES.some((prefix) => normalized.startsWith(prefix))) {
    throw new ModSyncValidationError(`${label} uses a reserved identifier.`);
  }
  return normalized;
}

function validateText(value: string, maxCharacters: number, label: string, allowEmpty: boolean): void {
  if (!allowEmpty && value.length === 0) {
    throw new ModSyncValidationError(`${label} cannot be empty.`);
  }
  if (Array.from(value).length > maxCharacters) {
    throw new ModSyncValidationError(`${label} exceeds ${maxCharacters} characters.`);
  }
  for (const character of value) {
    const code = character.codePointAt(0) ?? 0;
    if (code <= 0x1f || (code >= 0x7f && code <= 0x9f)) {
      throw new ModSyncValidationError(`${label} contains control characters.`);
    }
  }
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return false;
  }
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function ordinalCompare(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}
