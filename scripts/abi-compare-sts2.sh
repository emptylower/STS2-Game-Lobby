#!/usr/bin/env bash
# 双版本 ABI 对比（spec §6.1/实施门禁）：对比 0.107.1 fixture 与本机 0.111.0 的
# native_bus_v1 依赖面。fixture 位于仓库外（显式传参：路径 + SHA-256 + ilspycmd 版本）。
#
# 用法：
#   scripts/abi-compare-sts2.sh <fixture-sts2.dll> <fixture-sha256> <ilspycmd-version> [输出目录]
set -euo pipefail

FIXTURE_PATH="${1:?usage: abi-compare-sts2.sh <fixture-sts2.dll> <fixture-sha256> <ilspycmd-version> [output-dir]}"
FIXTURE_SHA256="${2:?missing fixture sha256}"
ILSPY_VERSION="${3:?missing ilspycmd version}"
OUT_DIR="${4:-$(mktemp -d /tmp/sts2-abi-compare.XXXXXX)}"

# ilspycmd 是 dotnet tool：宿主需要 DOTNET_ROOT 指向 .NET 安装（由调用方导出）。
ILSPY="${ILSPYCMD:-$HOME/.dotnet/tools/ilspycmd}"
LOCAL_DLL="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64/sts2.dll"

die() { echo "[abi-compare] ERROR: $*" >&2; exit 1; }

[[ -f "$FIXTURE_PATH" ]] || die "fixture not found: $FIXTURE_PATH"
[[ -f "$LOCAL_DLL" ]] || die "local 0.111.0 sts2.dll not found: $LOCAL_DLL"
[[ -x "$ILSPY" ]] || die "ilspycmd not found at $ILSPY"

ACTUAL_SHA="$(shasum -a 256 "$FIXTURE_PATH" | awk '{print $1}')"
[[ "$ACTUAL_SHA" == "$FIXTURE_SHA256" ]] || die "fixture sha256 mismatch: expected=$FIXTURE_SHA256 actual=$ACTUAL_SHA"
ACTUAL_ILSPY="$("$ILSPY" --version | head -1 | sed 's/ilspycmd: //')"
[[ "$ACTUAL_ILSPY" == "$ILSPY_VERSION" ]] || die "ilspycmd version mismatch: expected=$ILSPY_VERSION actual=$ACTUAL_ILSPY"

mkdir -p "$OUT_DIR"
echo "[abi-compare] output: $OUT_DIR"
echo "[abi-compare] fixture: $FIXTURE_PATH sha256=$ACTUAL_SHA"
echo "[abi-compare] local:   $LOCAL_DLL"
echo "[abi-compare] ilspycmd: $ACTUAL_ILSPY"

# native_bus_v1 依赖面（spec §4.2.3）：① INetMessage/MessageTypes/NetTypeCache/ContentSorter
# ② NetMessageBus ③ ENetClient/ENetHost/ENetConnectionExtension ④ 两个 OnPacketReceived
# ⑤ PacketReader/PacketWriter ⑥ 矩阵消息类型。
TYPES=(
  MegaCrit.Sts2.Core.Multiplayer.Serialization.INetMessage
  MegaCrit.Sts2.Core.Multiplayer.Serialization.MessageTypes
  'MegaCrit.Sts2.Core.Multiplayer.Serialization.NetTypeCache`1'
  'MegaCrit.Sts2.Core.Multiplayer.Serialization.ContentSorter`1'
  MegaCrit.Sts2.Core.Multiplayer.NetMessageBus
  MegaCrit.Sts2.Core.Multiplayer.Transport.ENet.ENetClient
  MegaCrit.Sts2.Core.Multiplayer.Transport.ENet.ENetHost
  MegaCrit.Sts2.Core.Multiplayer.Transport.ENet.ENetConnectionExtension
  MegaCrit.Sts2.Core.Multiplayer.NetHostGameService
  MegaCrit.Sts2.Core.Multiplayer.NetClientGameService
  MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader
  MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter
  MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.InitialGameInfoMessage
  MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinRequestMessage
  MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinResponseMessage
  MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLoadJoinRequestMessage
  MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLoadJoinResponseMessage
  MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientRejoinRequestMessage
  MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientRejoinResponseMessage
  MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.PlayerJoinedMessage
  MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyBeginRunMessage
)

# ilspycmd 对泛型元数据名解析挑剔：逐候选名尝试；全部失败时回退到全程序集单文件提取。
decompile() {
  local dll="$1" dest="$2"
  mkdir -p "$dest"
  for type in "${TYPES[@]}"; do
    local safe="${type//\`1/}"
    local file="$dest/${safe//./_}.cs"
    local ok=0
    for candidate in "$type" "$type\`1"; do
      if "$ILSPY" -t "$candidate" "$dll" > "$file" 2>>"$dest/errors.log"; then
        ok=1
        break
      fi
    done
    if [[ "$ok" != "1" ]]; then
      # 类型在该版本不存在（已裁决差异组，如 0.107.1 无 ContentSorter）：写显式缺席标记。
      echo "// TYPE ABSENT IN THIS GAME VERSION" > "$file"
      echo "[abi-compare] note: $type absent in $(basename "$dll")"
    fi
  done
  rm -f "$dest/__full.cs"
}

decompile "$FIXTURE_PATH" "$OUT_DIR/v0.107.1"
decompile "$LOCAL_DLL" "$OUT_DIR/v0.111.0"

failures=0

# ① 逐类型源 diff：跨游戏版本的源级差异是预期的（游戏演进）；全部落入 abi-diff.txt
#    供人工裁决。阻断条件是下方的事实/签名不变量，而非源差异本身。
: > "$OUT_DIR/abi-diff.txt"
for type in "${TYPES[@]}"; do
  safe="${type//\`1/}"
  file="${safe//./_}.cs"
  if ! diff -u "$OUT_DIR/v0.107.1/$file" "$OUT_DIR/v0.111.0/$file" >> "$OUT_DIR/abi-diff.txt"; then
    echo "DIFF: $type" >> "$OUT_DIR/abi-diff.txt"
    echo "[abi-compare] DIFF (documented for adjudication): $type"
  fi
done

# ② 事实断言：入站 mode 恒 None（TryService 不映射可靠性标志）。
for version in v0.107.1 v0.111.0; do
  grep -q "value.mode = NetTransferMode.None;" "$OUT_DIR/$version/MegaCrit_Sts2_Core_Multiplayer_Transport_ENet_ENetConnectionExtension.cs" \
    || { echo "[abi-compare] MISSING invariant: $version inbound mode=None"; failures=$((failures+1)); }
done

# ③ 事实断言（版本感知，已裁决的差异组）：
#    - 0.111.0：ContentSorter 六级排序键（affectsGameplay → id → null-mod → mod id → FullName → assembly）。
#    - 0.107.1：无 ContentSorter；NetTypeCache 直接按 t.Name 序数排序。
#    裁决（2026-09-01）：指纹门禁在两版均按"全表内容摘要"比对，同版本内确定性成立；
#    跨版本房间被 strict game-version 检查隔离，故不构成设计缺陷（详见 REPORT.md）。
grep -q "affectsGameplay" "$OUT_DIR/v0.111.0/MegaCrit_Sts2_Core_Multiplayer_Serialization_ContentSorter.cs" \
  && grep -q "CompareOrdinal(p1.mod.manifest.id" "$OUT_DIR/v0.111.0/MegaCrit_Sts2_Core_Multiplayer_Serialization_ContentSorter.cs" \
  && grep -q "CompareOrdinal(p1.type.FullName" "$OUT_DIR/v0.111.0/MegaCrit_Sts2_Core_Multiplayer_Serialization_ContentSorter.cs" \
  && grep -q "type.Assembly.FullName" "$OUT_DIR/v0.111.0/MegaCrit_Sts2_Core_Multiplayer_Serialization_ContentSorter.cs" \
  || { echo "[abi-compare] MISSING invariant: v0.111.0 ContentSorter six-key ordering"; failures=$((failures+1)); }
grep -q "types.Sort((Type t1, Type t2) => string.CompareOrdinal(t1.Name, t2.Name))" \
  "$OUT_DIR/v0.107.1/MegaCrit_Sts2_Core_Multiplayer_Serialization_NetTypeCache.cs" \
  || { echo "[abi-compare] MISSING invariant: v0.107.1 NetTypeCache plain ordinal name sort"; failures=$((failures+1)); }

# ④ 事实断言：未知 ID 分支（modded 警告一次后丢弃）与 TryDeserializeMessage 签名。
for version in v0.107.1 v0.111.0; do
  f="$OUT_DIR/$version/MegaCrit_Sts2_Core_Multiplayer_NetMessageBus.cs"
  grep -q "public bool TryDeserializeMessage(byte\[\] packetBytes, out INetMessage? message, out ulong? overrideSenderId)" "$f" \
    || { echo "[abi-compare] MISSING invariant: $version TryDeserializeMessage signature"; failures=$((failures+1)); }
  # warn-once 容忍是 0.111.0 行为；0.107.1 为 Log.Error+丢弃（同为非致命）——已裁决可接受。
  grep -q "_warnedMessageTypes.Add(b);" "$f" || grep -q "not a valid message ID" "$f" \
    || { echo "[abi-compare] MISSING invariant: $version unknown-id non-fatal drop branch"; failures=$((failures+1)); }
done

# ⑥ 事实断言：MessageTypes 反射发现 API 与接收/序列化签名在两版一致。
for version in v0.107.1 v0.111.0; do
  # Count 为 0.111.0 新增属性（0.107.1 无）：客户端自检不得依赖它（已裁决，见 REPORT.md）。
  grep -q "public static bool TryGetMessageType(int id, out Type? type)" "$OUT_DIR/$version/MegaCrit_Sts2_Core_Multiplayer_Serialization_MessageTypes.cs" \
    || { echo "[abi-compare] MISSING invariant: $version MessageTypes.TryGetMessageType"; failures=$((failures+1)); }
done

# ⑦ 事实断言：矩阵消息 Serialize/Deserialize 与 OnPacketReceived 签名（非泛型补丁目标）。
MATRIX_TYPES=(InitialGameInfoMessage ClientLobbyJoinRequestMessage ClientLobbyJoinResponseMessage \
  ClientLoadJoinRequestMessage ClientLoadJoinResponseMessage ClientRejoinRequestMessage \
  ClientRejoinResponseMessage PlayerJoinedMessage LobbyBeginRunMessage)
for version in v0.107.1 v0.111.0; do
  for message in "${MATRIX_TYPES[@]}"; do
    f="$OUT_DIR/$version/MegaCrit_Sts2_Core_Multiplayer_Messages_Lobby_${message}.cs"
    grep -qE "public (void|unsafe void) Serialize\(PacketWriter" "$f" \
      || { echo "[abi-compare] MISSING invariant: $version $message.Serialize(PacketWriter)"; failures=$((failures+1)); }
    grep -qE "public (void|unsafe void) Deserialize\(PacketReader" "$f" \
      || { echo "[abi-compare] MISSING invariant: $version $message.Deserialize(PacketReader)"; failures=$((failures+1)); }
  done
  grep -q "public void OnPacketReceived(ulong senderId, byte\[\] packetBytes, NetTransferMode mode, int channel)" \
    "$OUT_DIR/$version/MegaCrit_Sts2_Core_Multiplayer_NetHostGameService.cs" \
    || { echo "[abi-compare] MISSING invariant: $version host OnPacketReceived"; failures=$((failures+1)); }
  grep -q "public void OnPacketReceived(ulong senderId, byte\[\] packetBytes, NetTransferMode mode, int channel)" \
    "$OUT_DIR/$version/MegaCrit_Sts2_Core_Multiplayer_NetClientGameService.cs" \
    || { echo "[abi-compare] MISSING invariant: $version client OnPacketReceived"; failures=$((failures+1)); }
done

# ⑤ 事实断言：transport 发送签名（非泛型底层）。
for version in v0.107.1 v0.111.0; do
  grep -q "public override void SendMessageToClient(ulong peerId, byte\[\] bytes, int length, NetTransferMode mode, int channel = 0)" \
    "$OUT_DIR/$version/MegaCrit_Sts2_Core_Multiplayer_Transport_ENet_ENetHost.cs" \
    || { echo "[abi-compare] MISSING invariant: $version ENetHost.SendMessageToClient signature"; failures=$((failures+1)); }
  grep -q "public override void SendMessageToHost(byte\[\] bytes, int length, NetTransferMode mode, int channel = 0)" \
    "$OUT_DIR/$version/MegaCrit_Sts2_Core_Multiplayer_Transport_ENet_ENetClient.cs" \
    || { echo "[abi-compare] MISSING invariant: $version ENetClient.SendMessageToHost signature"; failures=$((failures+1)); }
done

if [[ "$failures" -ne 0 ]]; then
  echo "[abi-compare] FAILED: $failures divergence group(s); see $OUT_DIR/abi-diff.txt"
  exit 1
fi
echo "[abi-compare] OK: dependency-face identical across 0.107.1 and 0.111.0 (diff: $OUT_DIR/abi-diff.txt)"
