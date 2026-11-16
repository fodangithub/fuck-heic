#!/usr/bin/env python3
"""
End-to-end tests for PhotoViewer's Debug HTTP control service.

Usage:
    python ui_debug_tests.py [port]

Start the app in a Debug build with an image open (e.g. a HEIC), then run this script.
It drives the UI over http://127.0.0.1:<port> and asserts on the reported state.

Requires: Python 3.7+ (stdlib only).
"""
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8321
BASE = f"http://127.0.0.1:{PORT}"

_passed = 0
_failed = 0
_failures = []


def _request(method, path):
    req = urllib.request.Request(BASE + path, method=method)
    with urllib.request.urlopen(req, timeout=10) as resp:
        return json.loads(resp.read().decode("utf-8"))


def get(path):
    return _request("GET", path)


def post(path):
    return _request("POST", path)


def check(name, cond, detail=""):
    global _passed, _failed
    if cond:
        _passed += 1
        print(f"  PASS  {name}")
    else:
        _failed += 1
        _failures.append(f"{name}  ({detail})")
        print(f"  FAIL  {name}   {detail}")


def approx(a, b, tol):
    return abs(a - b) <= tol


def settle(delay=0.6):
    """Give the UI a moment to apply layout after an action."""
    time.sleep(delay)


def nav_next_and_wait(timeout=6.0):
    """Navigate forward and wait until /info reports a different file.

    Decoding may happen on a background thread, so the POST response can lag;
    poll until the file actually changes (or the timeout expires).
    """
    before = get("/info")["file"]
    post("/nav/next")
    deadline = time.time() + timeout
    last = None
    while time.time() < deadline:
        last = get("/info")
        if last.get("file") != before:
            return last
        time.sleep(0.1)
    return last


def main():
    print(f"PhotoViewer debug tests  ->  {BASE}\n")

    # ---- Test 1: /info is reachable and reports sane geometry ----
    print("[1] GET /info")
    info = get("/info")
    check("info has image", info.get("image") is not None)
    pw = info["image"]["pixelWidth"]
    ph = info["image"]["pixelHeight"]
    check("pixelWidth > 0", pw > 0, f"pw={pw}")
    check("pixelHeight > 0", ph > 0, f"ph={ph}")
    vw = info["viewport"]["width"]
    vh = info["viewport"]["height"]
    check("viewport width > 0", vw > 0, f"vw={vw}")
    check("viewport height > 0", vh > 0, f"vh={vh}")
    check("count >= 1", info.get("count", 0) >= 1)
    check("not auto-playing initially", info.get("isAutoPlaying") is False)
    print()

    # Establish a known baseline: fit to window (centers, sets fitZoom).
    post("/fit")
    settle()
    fit = get("/info")
    fit_zoom = fit["fitZoom"]

    # ---- Test 2: zoom to an absolute scale ----
    print("[2] POST /zoom?scale=2")
    z2 = post("/zoom?scale=2")
    settle()
    check("zoom == 2.0", approx(z2["zoom"], 2.0, 1e-6), f"zoom={z2['zoom']}")
    check("zoomPercent == 200", approx(z2["zoomPercent"], 200, 0.01), f"{z2['zoomPercent']}")
    check("imageElement.width == pixelWidth*2",
          approx(z2["imageElement"]["width"], pw * 2, max(1.0, pw * 0.002)),
          f"{z2['imageElement']['width']} vs {pw*2}")
    check("imageElement.height == pixelHeight*2",
          approx(z2["imageElement"]["height"], ph * 2, max(1.0, ph * 0.002)),
          f"{z2['imageElement']['height']} vs {ph*2}")
    print()

    # ---- Test 3: relative pan via dx/dy ----
    print("[3] POST /pan?dx=-50&dy=0")
    post("/fit"); settle()
    before = get("/info")["pan"]["x"]
    panned = post("/pan?dx=-50&dy=0")
    settle()
    check("pan.x decreased by 50", approx(panned["pan"]["x"], before - 50, 0.01),
          f"{before} -> {panned['pan']['x']}")
    print()

    # ---- Test 4: directional pan helper (/pan/left) ----
    print("[4] POST /pan/left?n=25")
    post("/fit"); settle()
    before = get("/info")["pan"]["x"]
    left = post("/pan/left?n=25")
    settle()
    check("pan.x decreased by 25", approx(left["pan"]["x"], before - 25, 0.01),
          f"{before} -> {left['pan']['x']}")
    print()

    # ---- Test 5: zoom in / out steps ----
    print("[5] POST /zoom/in and /zoom/out")
    post("/original"); settle()
    base = get("/info")["zoom"]
    zin = post("/zoom/in")
    check("zoom/in multiplies by 1.25", approx(zin["zoom"], base * 1.25, 1e-6),
          f"{base} -> {zin['zoom']}")
    zout = post("/zoom/out")
    check("zoom/out returns to base", approx(zout["zoom"], base, 1e-6), f"{zout['zoom']}")
    print()

    # ---- Test 6: fit window ----
    print("[6] POST /fit")
    post("/zoom?scale=2"); settle()
    fitted = post("/fit")
    settle()
    check("fit zoom == fitZoom", approx(fitted["zoom"], fitted["fitZoom"], 1e-6),
          f"zoom={fitted['zoom']} fitZoom={fitted['fitZoom']}")
    check("fit zoom <= 1.0", fitted["zoom"] <= 1.0, f"{fitted['zoom']}")
    check("fit centers (pan ~ 0)", approx(fitted["pan"]["x"], 0, 0.01)
          and approx(fitted["pan"]["y"], 0, 0.01), f"{fitted['pan']}")
    print()

    # ---- Test 7: original (100%) ----
    print("[7] POST /original")
    orig = post("/original")
    settle()
    check("original zoom == 1.0", approx(orig["zoom"], 1.0, 1e-6), f"{orig['zoom']}")
    check("original centered", approx(orig["pan"]["x"], 0, 0.01)
          and approx(orig["pan"]["y"], 0, 0.01), f"{orig['pan']}")
    print()

    # ---- Test 8: reset (position + size + rotation) ----
    print("[8] POST /rotate?deg=90 then /reset")
    post("/rotate?deg=90"); settle()
    rotated = get("/info")
    check("rotation == 90 after rotate", approx(rotated["rotation"], 90, 1e-6), f"{rotated['rotation']}")
    reset = post("/reset")
    settle()
    check("reset clears rotation", approx(reset["rotation"], 0, 1e-6), f"{reset['rotation']}")
    check("reset centers", approx(reset["pan"]["x"], 0, 0.01)
          and approx(reset["pan"]["y"], 0, 0.01), f"{reset['pan']}")
    check("reset restores fit zoom", approx(reset["zoom"], reset["fitZoom"], 1e-6),
          f"zoom={reset['zoom']} fitZoom={reset['fitZoom']}")
    print()

    # ---- Test 9: navigation ----
    print("[9] POST /nav/next")
    start = get("/info")
    idx0 = start["index"]
    count = start["count"]
    nxt = post("/nav/next")
    settle()
    expected = (idx0 + 1) % count
    check("index incremented (mod count)", nxt["index"] == expected,
          f"{idx0} -> {nxt['index']} (expected {expected})")
    check("file changed", nxt["file"] != start["file"], f"{start['file']} -> {nxt['file']}")
    prv = post("/nav/prev")
    settle()
    check("prev returns to start index", prv["index"] == idx0,
          f"{nxt['index']} -> {prv['index']} (expected {idx0})")
    print()

    # ---- Test 10: auto-play start/pause ----
    print("[10] POST /play?interval=1 then /pause")
    playing = post("/play?interval=1")
    check("isAutoPlaying true after /play", playing.get("isAutoPlaying") is True)
    check("interval applied", approx(playing.get("autoPlayIntervalSeconds", -1), 1.0, 1e-6),
          f"{playing.get('autoPlayIntervalSeconds')}")
    paused = post("/pause")
    check("isAutoPlaying false after /pause", paused.get("isAutoPlaying") is False)
    print()

    # ---- Test 11: unknown endpoint returns an error payload ----
    print("[11] unknown endpoint")
    err = post("/does/not/exist")
    check("error payload present", "error" in err, f"{err}")
    print()

    # ---- Test 12: opening an image always fits it to the viewport ----
    # Regression: SetImage used to defer the fit-to-window and leave the zoom model
    # stale, so a freshly opened image rendered at 100% (or the previous image's
    # zoom) for one or more frames — a large photo then showed only a small middle
    # portion ("the picture is not fully displayed").
    print("[12] open fits to viewport (no stale zoom)")
    info = get("/info")
    vw, vh = info["viewport"]["width"], info["viewport"]["height"]
    count = info["count"]

    # Zoom in hard, then re-open the same file: it must come back fitted.
    post("/zoom?scale=3"); settle()
    opened = post("/open?path=" + urllib.parse.quote(info["file"]))
    settle()
    check("re-open resets zoom to fit",
          approx(opened["zoom"], opened["fitZoom"], 1e-6),
          f"zoom={opened['zoom']} fitZoom={opened['fitZoom']}")
    check("re-open centered (pan ~ 0)",
          approx(opened["pan"]["x"], 0, 0.01) and approx(opened["pan"]["y"], 0, 0.01),
          f"{opened['pan']}")
    dw, dh = opened["imageElement"]["width"], opened["imageElement"]["height"]
    check("re-opened image fits viewport", dw <= vw + 0.5 and dh <= vh + 0.5,
          f"{dw:.0f}x{dh:.0f} vs viewport {vw:.0f}x{vh:.0f}")

    # Every sibling must satisfy the same "fully displayed" invariant, and the
    # folder listing must not contain the same file twice.
    seen = []
    duplicates = []
    overflow = []
    for _ in range(count):
        d = nav_next_and_wait()
        if d is None or d.get("image") is None:
            continue
        key = os.path.normcase(d["file"])
        if key in seen:
            duplicates.append(d["file"])
        seen.append(key)
        w, h = d["imageElement"]["width"], d["imageElement"]["height"]
        if w > vw + 0.5 or h > vh + 0.5:
            overflow.append(f"{d['file']}: {w:.0f}x{h:.0f} > {vw:.0f}x{vh:.0f}")
    check("no duplicate file entries in folder listing", not duplicates,
          f"{duplicates}")
    check("every image fits the viewport when opened", not overflow,
          f"{overflow}")
    print()

    # ---- Summary ----
    print("=" * 46)
    print(f"  {_passed} passed, {_failed} failed")
    if _failures:
        print("  Failures:")
        for f in _failures:
            print(f"    - {f}")
    print("=" * 46)
    return 0 if _failed == 0 else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except urllib.error.URLError as e:
        print(f"Could not reach PhotoViewer at {BASE}: {e}\n"
              f"Start the app in a Debug build with an image open, then retry.")
        sys.exit(2)
