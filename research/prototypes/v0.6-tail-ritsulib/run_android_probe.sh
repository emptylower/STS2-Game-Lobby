#!/bin/sh
# Runs one fresh Android standalone process and two Android/desktop typed-sidecar pairs.
set -eu

need() { eval "value=\${$1-}"; test -n "$value" || { echo "BLOCKED: missing $1" >&2; exit 2; }; }
for variable in ADB_BIN ANDROID_SERIAL ANDROID_STS2_PACKAGE ANDROID_MOD_DIR ANDROID_STS2_LOG_PATH ANDROID_PROBE_DLL ANDROID_PROBE_MANIFEST ANDROID_RITSULIB_PACKAGE_DIR RITSULIB_PACKAGE_TREE_SHA256 DESKTOP_SIDECAR_PROBE_COMMAND ANDROID_PROBE_LOG_DIR; do need "$variable"; done
test -x "$ADB_BIN"; test -x "$DESKTOP_SIDECAR_PROBE_COMMAND"; test -f "$ANDROID_PROBE_DLL"; test -f "$ANDROID_PROBE_MANIFEST"; test -f "$ANDROID_RITSULIB_PACKAGE_DIR/mod_manifest.json"; test "$(python3 research/prototypes/v0.6-tail-ritsulib/package_tree_hash.py "$ANDROID_RITSULIB_PACKAGE_DIR")" = "$RITSULIB_PACKAGE_TREE_SHA256"
mkdir -p "$ANDROID_PROBE_LOG_DIR"

run_android() {
  name="$1"; mode="$2"; nonce="$3"; evidence="$4"
  runtime="${ANDROID_PROBE_LOG_DIR}/${name}.runtime.json"; log="${ANDROID_PROBE_LOG_DIR}/${name}.log"
  rm -f "$log"
  python3 -c 'import json,sys; json.dump({"mode":sys.argv[1],"flowNonce":sys.argv[2],"sidecarEvidencePath":sys.argv[3]},open(sys.argv[4],"w"),separators=(",",":"))' "$mode" "$nonce" "$evidence" "$runtime"
  "$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_PROBE_DLL" "$ANDROID_MOD_DIR/" >/dev/null
  "$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_PROBE_MANIFEST" "$ANDROID_MOD_DIR/" >/dev/null
  "$ADB_BIN" -s "$ANDROID_SERIAL" push "$runtime" "$ANDROID_MOD_DIR/sts2_lan_v06_probe_runtime.json" >/dev/null
  "$ADB_BIN" -s "$ANDROID_SERIAL" shell am force-stop "$ANDROID_STS2_PACKAGE"
  "$ADB_BIN" -s "$ANDROID_SERIAL" logcat -c
  "$ADB_BIN" -s "$ANDROID_SERIAL" shell monkey -p "$ANDROID_STS2_PACKAGE" -c android.intent.category.LAUNCHER 1 >/dev/null
  for _ in $(seq 1 30); do "$ADB_BIN" -s "$ANDROID_SERIAL" shell cat "$ANDROID_STS2_LOG_PATH" 2>/dev/null | grep -q 'STS2_LAN_V06_ANDROID_PROBE ' && break; sleep 2; done
  "$ADB_BIN" -s "$ANDROID_SERIAL" pull "$ANDROID_STS2_LOG_PATH" "$log" >/dev/null
  "$ADB_BIN" -s "$ANDROID_SERIAL" shell am force-stop "$ANDROID_STS2_PACKAGE"
}

nonce="$(date +%s)-$$"; standalone_evidence="${ANDROID_PROBE_LOG_DIR}/standalone.evidence.json"; : > "$standalone_evidence"
run_android android-standalone-write-read standalone "$nonce" "$standalone_evidence"
for role in host client; do
  nonce="$(date +%s)-$$-$role"; evidence="${ANDROID_PROBE_LOG_DIR}/desktop-${role}.evidence.json"; desktop_log="${ANDROID_PROBE_LOG_DIR}/desktop-${role}.log"
  "$DESKTOP_SIDECAR_PROBE_COMMAND" --android-role "$role" --flow-nonce "$nonce" --evidence "$evidence" >"$desktop_log" 2>&1 & desktop_pid=$!
  run_android "android-ritsu-sidecar-$role" sidecar "$nonce" "$evidence"
  wait "$desktop_pid"
done
python3 research/prototypes/v0.6-tail-ritsulib/verify_android_probe.py "$ANDROID_PROBE_LOG_DIR/android-standalone-write-read.log" "$ANDROID_PROBE_LOG_DIR/android-ritsu-sidecar-host.log" "$ANDROID_PROBE_LOG_DIR/desktop-host.log" "$ANDROID_PROBE_LOG_DIR/android-ritsu-sidecar-client.log" "$ANDROID_PROBE_LOG_DIR/desktop-client.log"
