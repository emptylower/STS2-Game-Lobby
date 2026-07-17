import type {
  LobbyModDescriptor,
  ModDiffResult,
  ModVersionMismatch,
} from "./protocol.js";
import { validateModInventory } from "./validator.js";

export interface ResolveModDiffInput {
  hostGameVersion: string;
  localGameVersion: string;
  hostMods: unknown;
  localMods: unknown;
}

export function resolveModDiff(input: ResolveModDiffInput): ModDiffResult {
  const hostGameVersion = input.hostGameVersion.trim();
  const localGameVersion = input.localGameVersion.trim();
  const exactMatch = normalizeGameVersion(hostGameVersion) === normalizeGameVersion(localGameVersion);
  const gameVersion = { host: hostGameVersion, local: localGameVersion, exactMatch };
  if (!exactMatch) {
    return {
      gameVersion,
      missingWorkshopMods: [],
      missingManualMods: [],
      extraGameplayMods: [],
      versionMismatches: [],
      canContinueRelaxed: false,
    };
  }

  const hostMods = validateModInventory(input.hostMods);
  const localMods = validateModInventory(input.localMods);
  const hostById = new Map(hostMods.map((mod) => [mod.id, mod]));
  const localById = new Map(localMods.map((mod) => [mod.id, mod]));
  const missingWorkshopMods: LobbyModDescriptor[] = [];
  const missingManualMods: LobbyModDescriptor[] = [];
  const versionMismatches: ModVersionMismatch[] = [];

  for (const hostMod of hostMods) {
    const localMod = localById.get(hostMod.id);
    if (!localMod) {
      if (hostMod.source === "steam_workshop" && hostMod.workshopFileId !== undefined) {
        missingWorkshopMods.push(hostMod);
      } else {
        missingManualMods.push(hostMod);
      }
      continue;
    }
    if (hostMod.version !== localMod.version) {
      const mismatchBase = {
        id: hostMod.id,
        hostVersion: hostMod.version,
        localVersion: localMod.version,
      };
      versionMismatches.push(hostMod.workshopFileId === undefined
        ? mismatchBase
        : { ...mismatchBase, workshopFileId: hostMod.workshopFileId });
    }
  }

  return {
    gameVersion,
    missingWorkshopMods,
    missingManualMods,
    extraGameplayMods: localMods.filter((mod) => mod.role === "gameplay" && !hostById.has(mod.id)),
    versionMismatches,
    canContinueRelaxed: true,
  };
}

function normalizeGameVersion(value: string): string {
  return value.startsWith("v") || value.startsWith("V") ? value.slice(1) : value;
}
