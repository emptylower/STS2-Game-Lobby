#!/usr/bin/env python3
"""Validate the four real-process Android feasibility markers."""

from __future__ import annotations

import hashlib
import json
import pathlib
import sys
from typing import Any


PREFIX = "STS2_LAN_V06_ANDROID_PROBE "
TAIL_SHA256 = "cfc9097350801026775fd333fb19c6758becbffd4142f58bab0884f4231f5cfa"


def fail(message: str) -> None:
    raise ValueError(message)


def marker_from_log(path: pathlib.Path) -> dict[str, Any]:
    candidates: list[dict[str, Any]] = []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if PREFIX not in line:
            continue
        payload = line.split(PREFIX, 1)[1].strip()
        try:
            value = json.loads(payload)
        except json.JSONDecodeError as error:
            fail(f"{path}: malformed marker: {error}")
        if not isinstance(value, dict):
            fail(f"{path}: marker must be an object")
        candidates.append(value)
    if len(candidates) != 1:
        fail(f"{path}: expected exactly one terminal marker, found {len(candidates)}")
    return candidates[0]


def require(value: dict[str, Any], name: str, expected_type: type) -> Any:
    item = value.get(name)
    if type(item) is not expected_type:
        fail(f"{name}: expected {expected_type.__name__}, got {item!r}")
    return item


def require_ritsu(marker: dict[str, Any], expected_present: bool) -> None:
    if require(marker, "ritsuPresent", bool) is not expected_present:
        fail(f"ritsuPresent does not match the process configuration: {marker!r}")
    owners = require(marker, "ritsuPatchOwners", list)
    for owner in owners:
        if not isinstance(owner, str):
            fail("ritsuPatchOwners must contain strings")
    if expected_present:
        if marker.get("ritsuManifestId") != "STS2-RitsuLib":
            fail("with-Ritsu marker does not report manifest id STS2-RitsuLib")
        for field in ("ritsuManifestVersion", "ritsuSelectedAssembly"):
            if not isinstance(marker.get(field), str) or not marker[field]:
                fail(f"with-Ritsu marker is missing {field}")
        if not marker["ritsuSelectedAssembly"].startswith("lib/"):
            fail("with-Ritsu marker selected assembly is not inside lib/")
        if not owners:
            fail("with-Ritsu marker has no loaded Harmony owner/target evidence")
    elif marker.get("ritsuManifestId") is not None or marker.get("ritsuManifestVersion") is not None:
        fail("without-Ritsu marker reports a Ritsu manifest")


def require_common(marker: dict[str, Any], phase: str, ritsu_present: bool) -> None:
    if marker.get("phase") != phase or marker.get("passed") is not True:
        fail(f"expected successful {phase} marker")
    require(marker, "sts2Version", str)
    if not marker["sts2Version"]:
        fail("sts2Version is empty")
    if require(marker, "containsOpenGeneric", bool):
        fail("probe reported an open generic Harmony target")
    if require(marker, "invalidProgram", bool):
        fail("probe reported InvalidProgramException")
    require_ritsu(marker, ritsu_present)


def require_encode(marker: dict[str, Any], ritsu_present: bool) -> None:
    require_common(marker, "encode", ritsu_present)
    for field in ("fixtureSha256", "lanTailSha256"):
        value = require(marker, field, str)
        if len(value) != 64:
            fail(f"{field} is not a sha256")
    if require(marker, "fixtureLength", int) < 36:
        fail("fixture is shorter than the frozen LAN Tail")
    if marker["lanTailSha256"] != TAIL_SHA256:
        fail("encode marker LAN Tail hash drifted")
    for field in ("lanEndBit", "ritsuStartBit"):
        if require(marker, field, int) != 288:
            fail(f"encode {field} drifted from 288")
    if not ritsu_present and (marker["fixtureLength"] != 36 or marker["fixtureSha256"] != TAIL_SHA256):
        fail("without-Ritsu encode fixture is not exactly the frozen LAN Tail")


def require_decode(marker: dict[str, Any], ritsu_present: bool) -> set[bool]:
    require_common(marker, "decode", ritsu_present)
    results = require(marker, "results", list)
    if len(results) != 2:
        fail("each decode process must report exactly two sender fixtures")
    seen: set[bool] = set()
    for result in results:
        if not isinstance(result, dict):
            fail("decode result is not an object")
        sender = require(result, "senderRitsu", bool)
        if sender in seen:
            fail("duplicate senderRitsu result in decode marker")
        seen.add(sender)
        if require(result, "messageOk", bool) is not True:
            fail("decode result messageOk is false")
        if require(result, "lanTailSha256", str) != TAIL_SHA256:
            fail("decode result LAN Tail hash drifted")
        for field in ("lanEndBit", "ritsuStartBit"):
            if require(result, field, int) != 288:
                fail(f"decode result {field} drifted from 288")
    return seen


def main() -> int:
    if len(sys.argv) != 5:
        print("usage: verify_android_probe.py <encode-no> <encode-with> <decode-no> <decode-with>", file=sys.stderr)
        return 2
    try:
        markers = [marker_from_log(pathlib.Path(argument)) for argument in sys.argv[1:]]
        require_encode(markers[0], False)
        require_encode(markers[1], True)
        if require_decode(markers[2], False) != {False, True}:
            fail("without-Ritsu receiver did not decode both senders")
        if require_decode(markers[3], True) != {False, True}:
            fail("with-Ritsu receiver did not decode both senders")
        if len({marker["sts2Version"] for marker in markers}) != 1:
            fail("STS2 version differs across Android probe processes")
        with_ritsu = [markers[1], markers[3]]
        if len({marker["ritsuManifestVersion"] for marker in with_ritsu}) != 1:
            fail("Ritsu manifest version differs across with-Ritsu processes")
        print("PASS: four Android probe markers validated")
    except (OSError, ValueError) as error:
        print(f"BLOCKED: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
