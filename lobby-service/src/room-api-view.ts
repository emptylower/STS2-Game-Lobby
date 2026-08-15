import type { ClientApiGeneration } from "./client-version.js";
import type { RoomProtocolSelection } from "./protocol-capabilities.js";
import { projectProfile, type ProtocolProfile } from "./protocol-profile.js";

export interface CanonicalRoomApiSource {
  readonly protocolProfileV2: ProtocolProfile;
  readonly protocolSelection: RoomProtocolSelection;
  readonly [key: string]: unknown;
}

export function toRoomApiView<T extends CanonicalRoomApiSource>(
  room: T,
  generation: ClientApiGeneration,
): Omit<T, "protocolProfileV2" | "protocolSelection"> & {
  protocolProfile: ReturnType<typeof projectProfile>;
  protocolProfileV2?: ProtocolProfile;
  protocolSelection?: RoomProtocolSelection;
} {
  const { protocolProfileV2, protocolSelection, ...base } = room;
  if (generation === "compat_0_3_0_5") {
    return { ...base, protocolProfile: projectProfile(protocolProfileV2, generation) };
  }
  return {
    ...base,
    protocolProfile: projectProfile(protocolProfileV2, generation),
    protocolProfileV2,
    protocolSelection,
  };
}

export function toUnversionedRoomApiView<T extends CanonicalRoomApiSource>(room: T) {
  return {
    ...room,
    protocolProfile: projectProfile(room.protocolProfileV2, "compat_0_3_0_5"),
  };
}
