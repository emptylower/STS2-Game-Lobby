export type ProtocolErrorCode =
  | "client_update_required"
  | "protocol_profile_unsupported"
  | "ritsulib_not_allowed_in_compat_mode"
  | "ritsulib_presence_mismatch"
  | "lan_protocol_version_mismatch"
  | "ritsulib_sidecar_unavailable"
  | "capability_digest_mismatch"
  | "lan_legacy_carrier_unsupported"
  | "lan_registry_fingerprint_required"
  | "lan_registry_fingerprint_mismatch"
  | "lan_native_frame_invalid"
  | "lan_client_version_too_old";

export interface ProtocolErrorDetails {
  requiredClientVersion?: string | undefined;
  requiredRitsuLibPresent?: boolean | undefined;
  detail?: string | undefined;
  expectedFingerprintPrefix?: string | undefined;
  receivedFingerprintPrefix?: string | undefined;
}

export class ProtocolContractError extends Error {
  constructor(
    readonly statusCode: number,
    readonly code: ProtocolErrorCode,
    message: string,
    readonly details?: ProtocolErrorDetails,
  ) {
    super(message);
  }
}
