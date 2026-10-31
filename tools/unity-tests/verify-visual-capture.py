#!/usr/bin/env python3
"""Validate I15 Player visual-capture artifacts from the opt-in PlayMode runner."""

import hashlib
import json
import struct
import sys
from pathlib import Path


def fail(message: str) -> int:
    print(json.dumps({"success": False, "reason": message}))
    return 1


def png_dimensions(data: bytes):
    if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    length = struct.unpack(">I", data[8:12])[0]
    if data[12:16] != b"IHDR" or length != 13 or len(data) < 16 + length:
        return None
    return struct.unpack(">II", data[16:24])


def main() -> int:
    if len(sys.argv) != 2:
        return fail("manifest-argument-required")
    manifest_path = Path(sys.argv[1])
    if not manifest_path.is_file():
        return fail("manifest-not-found")
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        return fail(f"manifest-invalid:{error}")

    player_path = Path(manifest.get("PlayerPath", ""))
    if not player_path.is_file():
        return fail("capture-image-not-found")
    player_bytes = player_path.read_bytes()
    if not player_bytes.startswith(b"\x89PNG\r\n\x1a\n"):
        return fail("capture-image-not-png")
    dimensions = png_dimensions(player_bytes)
    if dimensions != (manifest.get("PlayerWidth"), manifest.get("PlayerHeight")):
        return fail("capture-image-dimensions-invalid")
    player_hash = hashlib.sha256(player_bytes).hexdigest()
    if player_hash != manifest.get("PlayerSha256"):
        return fail("capture-hash-mismatch")
    if manifest.get("Frame", 0) <= 0 or manifest.get("PlayerFrame", 0) != manifest.get("Frame"):
        return fail("invalid-frame")
    player_source = manifest.get("PlayerSource", "")
    if player_source != "UnityEngine.Camera.targetTexture.ReadPixels after URP DexcPlayerRenderPass":
        return fail("render-source-mismatch")
    player_renderer = manifest.get("PlayerRenderer", "")
    if player_renderer != "DexcPlayerRenderer.EnqueuePass -> DexcPlayerRenderPass":
        return fail("production-renderer-mismatch")
    if manifest.get("PlayerCameraType") != "Game":
        return fail("player-camera-type-mismatch")
    if manifest.get("PlayerNonBackgroundPixels", 0) <= 0:
        return fail("render-target-empty")
    required_kinds = {"Line", "WireSphere", "WireCube"}
    if not required_kinds.issubset(set(manifest.get("PlayerKinds") or [])):
        return fail("primitive-identity-incomplete")
    print(json.dumps({
        "success": True,
        "frame": manifest["Frame"],
        "player": str(player_path.resolve()),
        "playerSha256": player_hash,
        "playerSource": manifest["PlayerSource"],
        "playerRenderer": manifest["PlayerRenderer"],
        "playerCameraType": manifest["PlayerCameraType"],
        "playerFrame": manifest["PlayerFrame"],
        "playerWidth": manifest["PlayerWidth"],
        "playerHeight": manifest["PlayerHeight"],
        "playerNonBackgroundPixels": manifest["PlayerNonBackgroundPixels"],
        "playerKinds": manifest["PlayerKinds"],
    }, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
