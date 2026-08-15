import type { ClientApiGeneration } from "./client-version.js";
import { ProtocolContractError } from "./protocol-errors.js";

export type ProtocolProfile = "compat_4_5_v1" | "tail_v1";
export type LegacyProtocolProfile = "legacy_4p" | "extended_8p";

export interface RequestedProfileInput {
  generation: ClientApiGeneration;
  canonicalProfile?: string | undefined;
  legacyProfile?: string | undefined;
}

export function parseRequestedProfile(input: RequestedProfileInput): ProtocolProfile {
  if (input.generation === "compat_0_3_0_5") {
    if (input.legacyProfile === undefined || input.legacyProfile === "extended_8p") {
      return "compat_4_5_v1";
    }
    if (input.legacyProfile === "legacy_4p") {
      throw new ProtocolContractError(426, "client_update_required", "该协议版本已停止支持，请升级客户端。", {
        requiredClientVersion: "0.3.0",
      });
    }
    throw unsupported();
  }

  if (input.canonicalProfile === "compat_4_5_v1" || input.canonicalProfile === "tail_v1") {
    return input.canonicalProfile;
  }
  throw unsupported();
}

export function projectProfile(profile: ProtocolProfile, generation: ClientApiGeneration): ProtocolProfile | LegacyProtocolProfile {
  if (generation === "compat_0_3_0_5") {
    return "extended_8p";
  }
  return profile;
}

function unsupported(): ProtocolContractError {
  return new ProtocolContractError(409, "protocol_profile_unsupported", "请求的多人协议不受支持。", {
    requiredProtocolProfile: "compat_4_5_v1",
  });
}
