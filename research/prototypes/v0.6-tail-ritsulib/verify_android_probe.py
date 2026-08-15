#!/usr/bin/env python3
"""Reject stale or incomplete standalone/typed-sidecar Android evidence."""
import json, pathlib, sys
P = "STS2_LAN_V06_ANDROID_PROBE "
H = "cfc9097350801026775fd333fb19c6758becbffd4142f58bab0884f4231f5cfa"
def marker(path):
    values=[]
    for line in pathlib.Path(path).read_text(errors="replace").splitlines():
        if P in line: values.append(json.loads(line.split(P,1)[1].strip()))
    if len(values)!=1: raise ValueError(f"{path}: expected one terminal marker, found {len(values)}")
    return values[0]
def req(value,key,typ):
    if type(value.get(key)) is not typ: raise ValueError(f"{key}: missing/wrong type")
    return value[key]
def main():
    if len(sys.argv)!=6: raise ValueError("usage: verifier standalone android-host desktop-host android-client desktop-client")
    values=[marker(path) for path in sys.argv[1:]]
    standalone=values[0]
    if standalone.get("phase")!="standalone" or standalone.get("carrier")!="standalone_tail_v1": raise ValueError("standalone carrier mismatch")
    for key in ("passed","alignmentPaddingWasZero"):
        if req(standalone,key,bool) is not True: raise ValueError(f"standalone {key} false")
    if req(standalone,"containerSha256",str)!=H or req(standalone,"containerLength",int)!=36: raise ValueError("standalone fixture drift")
    nonces=set()
    for value in values[1:]:
        if value.get("phase")!="sidecar" or value.get("carrier")!="ritsulib_sidecar_v1": raise ValueError("sidecar carrier mismatch")
        if req(value,"ritsuPresent",bool) is not True or req(value,"passed",bool) is not True: raise ValueError("sidecar not initialized/passed")
        nonce=req(value,"flowNonce",str)
        if not nonce: raise ValueError("empty flow nonce")
        nonces.add(nonce)
        if req(value,"containerSha256",str)!=H or req(value,"containerLength",int)!=36: raise ValueError("sidecar fixture drift")
        for key in ("trustedTicketHintBootstrappedReachability","sidecarReachableBeforeFirstLanFlow","handlerBlockedUntilPairValidated","vanillaBytesMatchFixture","hintClearedOnTeardown","reusedPeerIdStartsUnknown"):
            if req(value,key,bool) is not True: raise ValueError(f"{key} false")
        if req(value,"standaloneTailPresent",bool) is not False: raise ValueError("standalone tail leaked into sidecar")
    if len(nonces)!=2: raise ValueError("sidecar endpoint logs do not form two unique pairs")
    print("PASS: fresh Android standalone and two typed-sidecar pairs validated")
if __name__=="__main__":
    try: main()
    except (OSError,ValueError,json.JSONDecodeError) as e: print(f"BLOCKED: {e}",file=sys.stderr); sys.exit(1)
