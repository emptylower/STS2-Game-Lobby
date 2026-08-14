#!/bin/sh
# Runs four isolated Android game processes and validates their terminal markers.
set -eu

require_env() {
  eval "value=\${$1-}"
  if [ -z "$value" ]; then
    echo "run_android_probe.sh: required environment variable is empty: $1" >&2
    exit 2
  fi
}

for name in \
  ADB_BIN ANDROID_SERIAL ANDROID_STS2_PACKAGE ANDROID_MOD_DIR \
  ANDROID_STS2_LOG_PATH ANDROID_STS2_LOG ANDROID_PROBE_DLL \
  ANDROID_PROBE_MANIFEST ANDROID_RITSULIB_PACKAGE_DIR \
  RITSULIB_PACKAGE_TREE_SHA256 ANDROID_PROBE_FIXTURE_PATH \
  ANDROID_PROBE_INPUT_DIR ANDROID_PROBE_RUNTIME_CONFIG
do
  require_env "$name"
done

test -x "$ADB_BIN"
test -f "$ANDROID_PROBE_DLL"
test -f "$ANDROID_PROBE_MANIFEST"
test -d "$ANDROID_RITSULIB_PACKAGE_DIR"
test -f "$ANDROID_RITSULIB_PACKAGE_DIR/mod_manifest.json"
test -f "$ANDROID_RITSULIB_PACKAGE_DIR/STS2-RitsuLib.dll"
test "$(python3 research/prototypes/v0.6-tail-ritsulib/package_tree_hash.py "$ANDROID_RITSULIB_PACKAGE_DIR")" = "$RITSULIB_PACKAGE_TREE_SHA256"

run_case() {
  case_name="$1"
  ritsu_enabled="$2"
  mode="$3"
  "$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_PROBE_DLL" "$ANDROID_MOD_DIR/"
  "$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_PROBE_MANIFEST" "$ANDROID_MOD_DIR/"
  if [ "$ritsu_enabled" = true ]; then
    "$ADB_BIN" -s "$ANDROID_SERIAL" shell rm -rf "$ANDROID_MOD_DIR/STS2-RitsuLib"
    "$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_RITSULIB_PACKAGE_DIR" "$ANDROID_MOD_DIR/STS2-RitsuLib"
  else
    "$ADB_BIN" -s "$ANDROID_SERIAL" shell rm -rf "$ANDROID_MOD_DIR/STS2-RitsuLib"
  fi
  python3 -c \
    'import json,sys; json.dump({"mode":sys.argv[1],"fixturePath":sys.argv[2],"inputDir":sys.argv[3]}, open(sys.argv[4], "w"), separators=(",",":"))' \
    "$mode" "$ANDROID_PROBE_FIXTURE_PATH" "$ANDROID_PROBE_INPUT_DIR" "$ANDROID_PROBE_RUNTIME_CONFIG"
  "$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_PROBE_RUNTIME_CONFIG" "$ANDROID_MOD_DIR/sts2_lan_v06_probe_runtime.json"
  "$ADB_BIN" -s "$ANDROID_SERIAL" shell am force-stop "$ANDROID_STS2_PACKAGE"
  "$ADB_BIN" -s "$ANDROID_SERIAL" logcat -c
  "$ADB_BIN" -s "$ANDROID_SERIAL" shell monkey -p "$ANDROID_STS2_PACKAGE" -c android.intent.category.LAUNCHER 1
  sleep 30
  "$ADB_BIN" -s "$ANDROID_SERIAL" pull "$ANDROID_STS2_LOG_PATH" "$ANDROID_STS2_LOG.$case_name"
}

run_case encode-without-ritsu false encode
"$ADB_BIN" -s "$ANDROID_SERIAL" pull "$ANDROID_PROBE_FIXTURE_PATH" "$ANDROID_STS2_LOG.sender-without.bin"
run_case encode-with-ritsu true encode
"$ADB_BIN" -s "$ANDROID_SERIAL" pull "$ANDROID_PROBE_FIXTURE_PATH" "$ANDROID_STS2_LOG.sender-with.bin"

"$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_STS2_LOG.sender-without.bin" "$ANDROID_MOD_DIR/sender-without.bin"
"$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_STS2_LOG.sender-with.bin" "$ANDROID_MOD_DIR/sender-with.bin"
run_case decode-without-ritsu false decode
run_case decode-with-ritsu true decode

python3 research/prototypes/v0.6-tail-ritsulib/verify_android_probe.py \
  "$ANDROID_STS2_LOG.encode-without-ritsu" \
  "$ANDROID_STS2_LOG.encode-with-ritsu" \
  "$ANDROID_STS2_LOG.decode-without-ritsu" \
  "$ANDROID_STS2_LOG.decode-with-ritsu"
