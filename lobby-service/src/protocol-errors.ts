export type ProtocolErrorCode =
  | "client_update_required"
  | "protocol_profile_unsupported"
  | "ritsulib_not_allowed_in_compat_mode"
  | "ritsulib_presence_mismatch"
  | "lan_protocol_version_mismatch"
  | "ritsulib_sidecar_unavailable"
  | "capability_digest_mismatch";

export interface ProtocolErrorDetails {
  requiredClientVersion?: string | undefined;
  requiredProtocolProfile?: string | undefined;
  requiredRitsuLibPresent?: boolean | undefined;
  [key: string]: unknown;
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
