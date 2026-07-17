export const MOD_SYNC_PROTOCOL_VERSION = 1;
export const MOD_SYNC_MAX_DESCRIPTORS = 64;
export const MOD_SYNC_MAX_ID_CHARACTERS = 128;
export const MOD_SYNC_MAX_VERSION_CHARACTERS = 64;
export const MOD_SYNC_MAX_DEPENDENCIES = 16;
export const MOD_SYNC_MAX_PAYLOAD_BYTES = 65_536;
export const STS2_STEAM_APP_ID = 2_868_840;

export type LobbyModRole = "gameplay" | "dependency";
export type LobbyModSource = "steam_workshop" | "mods_directory" | "unknown";

export interface LobbyModDescriptor {
  id: string;
  version: string;
  role: LobbyModRole;
  source: LobbyModSource;
  workshopFileId?: string;
  dependencies: string[];
}

export interface ModVersionMismatch {
  id: string;
  hostVersion: string;
  localVersion: string;
  workshopFileId?: string;
}

export interface ModDiffResult {
  gameVersion: {
    host: string;
    local: string;
    exactMatch: boolean;
  };
  missingWorkshopMods: LobbyModDescriptor[];
  missingManualMods: LobbyModDescriptor[];
  extraGameplayMods: LobbyModDescriptor[];
  versionMismatches: ModVersionMismatch[];
  canContinueRelaxed: boolean;
}

export interface ModSyncFixture {
  schemaVersion: number;
  name: string;
  gameVersion: { host: string; local: string };
  hostMods: LobbyModDescriptor[];
  localMods: LobbyModDescriptor[];
  expected: {
    missingWorkshopModIds: string[];
    missingManualModIds: string[];
    extraGameplayModIds: string[];
    versionMismatchModIds: string[];
    canContinueRelaxed: boolean;
  };
}
