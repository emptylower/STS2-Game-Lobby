#!/usr/bin/env bash
# native_bus_v1 迁移零引用门禁（spec §4.1）：固定符号表在客户端源码中必须零命中。
# 只匹配 C# 标识符：命中行若全部为字符串字面量/注释则不视为违规（实现上通过排除法核对）。
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLIENT_ROOT="$ROOT_DIR/sts2-lan-connect"

SYMBOLS=(
  "RitsuLibSidecar"
  "SidecarPairing"
  "HostSidecarActivationGate"
  "RitsuSidecarENetClientConnectionInitializer"
  "SubmitSidecarBeforeVanilla"
  "LanConnectStandaloneTailCarrier"
  "LanConnectRitsuLibLobbyCompatibility"
  "SerializeMessage<"
  "LanConnectTailPlanOverride"
  "ResolveDesktopPatchPlan"
  "ResolveGenericSerializeMessageMethod"
  "desktop_generic_v1"
)

# 旧 carrier 只允许以线上字符串字面量出现在 DTO 映射与识别拒绝分支。
ALLOWED_LITERAL_CONTEXTS=(
  '"standalone_tail_v1"'
  '"ritsulib_sidecar_v1"'
  '"native_bus_v1"'
)

failures=0
for symbol in "${SYMBOLS[@]}"; do
  while IFS=: read -r file line_number line_content; do
    [[ -z "${file:-}" ]] && continue
    case "$file" in
      */obj/*|*/bin/*|*/.godot/*|*/release/*) continue ;;
    esac
    # 排除注释与纯字符串行（旧 carrier 字面量/错误码字面量不触发）。
    trimmed="${line_content#"${line_content%%[![:space:]]*}"}"
    case "$trimmed" in
      '//'*) continue ;;
      '*'*) continue ;;
    esac
    if [[ "$symbol" == "SerializeMessage<" ]]; then
      # 泛型调用（非补丁目标）合法：仅当行内出现 Harmony 目标语境才违规。
      case "$line_content" in
        *AccessTools*|*GetMethods*|*Patch*|*MakeGenericMethod*) echo "VIOLATION: $file:$line_number: $line_content"; failures=$((failures+1)) ;;
      esac
      continue
    fi
    if [[ "$symbol" == "desktop_generic_v1" ]]; then
      case "$line_content" in
        *'"'*) continue ;;
      esac
    fi
    if [[ "$symbol" == "RitsuLibSidecar" ]]; then
      # 允许：错误码字符串字面量与 DTO 线上字段（JsonPropertyName）上下文。
      case "$line_content" in
        *'"ritsulib_sidecar'*|*JsonPropertyName*) continue ;;
      esac
    fi
    echo "VIOLATION: $file:$line_number: $line_content"
    failures=$((failures+1))
  done < <(rg -n --no-heading -F "$symbol" "$CLIENT_ROOT" "$ROOT_DIR/sts2-lan-connect.Tests" \
      "$ROOT_DIR/sts2-lan-connect.GdUnitTests" "$ROOT_DIR/sts2-lan-connect.ProtocolPlanTests" 2>/dev/null || true)
done

if [[ "$failures" -ne 0 ]]; then
  echo "[check-native-bus-migration] FAILED with $failures violation(s)."
  exit 1
fi
echo "[check-native-bus-migration] OK: zero banned symbol references."
