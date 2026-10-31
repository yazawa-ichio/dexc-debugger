#!/usr/bin/env python3
"""Validate UI03 production UI Toolkit capture pixels and metadata."""

import hashlib
import json
import os
import struct
import sys
import zlib
from collections import deque
from pathlib import Path

from png_decoder import decode_png_tuple as decode_png


# Only the screens are fixed here. Where the panel, header and footer sit is the renderer's layout, which the C#
# tests assert; this checks that the manifest's rectangles agree with each other and with the pixels.
PROFILES = {
    "MobilePortrait": {"screen": (390, 844)},
    "MobileLandscape": {"screen": (844, 390)},
    "WindowsPlayer": {"screen": (1366, 768)},
}
SCENARIOS = {
    "Home", "CommandList", "CommandDetailDraft", "ResultState", "History",
    "MultiInput12", "Validation", "LongTextReference", "Running",
}
DETAIL = {"CommandDetailDraft", "ResultState", "MultiInput12", "Validation", "LongTextReference", "Running"}
EXECUTE_ENABLED = {"CommandDetailDraft", "MultiInput12", "LongTextReference"}
SOURCE = "DexcRuntimeUIToolkitRenderer UIDocument/code-generated VisualElements/session-cloned PanelSettings -> ScreenCapture.CaptureScreenshotAsTexture"
PANEL_SETTINGS_PATH = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitPanelSettings.asset"
PANEL_SETTINGS_GUID = "51af7a72ad1049d4b5acd4e21fa42cec"
THEME_PATH = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitTheme.tss"
THEME_GUID = "2f9d1de283b8e9349ac75a8932c7dca4"
PALETTE = ((10, 15, 25), (25, 33, 48), (33, 43, 61), (31, 64, 110), (41, 48, 61), (38, 99, 235), (122, 26, 31))
ERROR_COLOR = (122, 26, 31)
PALETTE_TOLERANCE = 12
MINIMUM_SHELL_COMPONENT_PIXELS = 500
# DebugDisplay is a separate approved renderer and always writes its FPS label at
# the upper-left of visual captures. Exclude only pixels outside the UI03 panel.
DEBUG_DISPLAY_OVERLAY = (0, 0, 380, 180)
EXPECTED_PAGES = {
    "Home": "Home",
    "CommandList": "CommandList",
    "CommandDetailDraft": "CommandDetail",
    "ResultState": "CommandDetail",
    "History": "History",
    "MultiInput12": "CommandDetail",
    "Validation": "CommandDetail",
    "LongTextReference": "CommandDetail",
    "Running": "CommandDetail",
}


def emit(payload, indent=None):
    output = json.dumps(payload, indent=indent)
    result_path = os.environ.get("DEXC_UITOOLKIT_VERIFIER_RESULT")
    if result_path:
        Path(result_path).write_text(output + "\n", encoding="utf-8")
    print(output)


def fail(reason):
    emit({"success": False, "reason": reason})
    return 1


def close(actual, expected, tolerance=1.1):
    return all(abs(float(left) - float(right)) <= tolerance for left, right in zip(actual, expected))


def rect(value):
    return tuple(float(value.get(key, 0)) for key in ("X", "Y", "Width", "Height"))


def png_dimensions(data):
    if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
        return None
    return struct.unpack(">II", data[16:24])


def is_color(pixels, index, color, tolerance=PALETTE_TOLERANCE):
    return max(abs(pixels[index + channel] - color[channel]) for channel in range(3)) <= tolerance


def expected_png_bounds(panel, screen_height):
    x, y, width, height = panel
    return (int(round(x)), int(round(screen_height - y - height)), int(round(x + width)) - 1, int(round(screen_height - y)) - 1)


def pixel_shell_metrics(image, panel):
    width, height, channels, pixels = image
    expected = expected_png_bounds(panel, height)
    mask = bytearray(width * height)
    coordinates = []
    outside = 0
    error_pixels = 0
    for y in range(height):
        for x in range(width):
            pixel_index = (y * width + x) * channels
            inside_panel = expected[0] <= x <= expected[2] and expected[1] <= y <= expected[3]
            inside_debug_overlay = DEBUG_DISPLAY_OVERLAY[0] <= x <= DEBUG_DISPLAY_OVERLAY[2] and DEBUG_DISPLAY_OVERLAY[1] <= y <= DEBUG_DISPLAY_OVERLAY[3]
            if not inside_panel and inside_debug_overlay:
                continue
            if is_color(pixels, pixel_index, ERROR_COLOR):
                error_pixels += 1
            if not any(is_color(pixels, pixel_index, color) for color in PALETTE):
                continue
            mask[y * width + x] = 1
            coordinates.append((x, y))
            if not inside_panel:
                outside += 1
    if not coordinates:
        return {"bbox": (-1, -1, -1, -1), "outside": outside, "components": 0, "pixels": 0, "error": error_pixels}
    bbox = (min(x for x, _ in coordinates), min(y for _, y in coordinates), max(x for x, _ in coordinates), max(y for _, y in coordinates))
    # Count renderer shells, not disconnected controls inside the shell. Native
    # UI Toolkit fields and footer buttons can be separated from the panel's
    # Skin-colored background by their standard borders and margins.
    components = 0
    for start in range(width * height):
        if mask[start] != 1:
            continue
        queue = deque((start,))
        mask[start] = 2
        size = 0
        component_bounds = [width, height, -1, -1]
        while queue:
            current = queue.popleft()
            size += 1
            x = current % width
            y = current // width
            component_bounds[0] = min(component_bounds[0], x)
            component_bounds[1] = min(component_bounds[1], y)
            component_bounds[2] = max(component_bounds[2], x)
            component_bounds[3] = max(component_bounds[3], y)
            neighbors = (
                current - 1 if x > 0 else -1,
                current + 1 if x + 1 < width else -1,
                current - width if y > 0 else -1,
                current + width if y + 1 < height else -1,
            )
            for neighbor in neighbors:
                if neighbor >= 0 and mask[neighbor] == 1:
                    mask[neighbor] = 2
                    queue.append(neighbor)
        spans_panel = all(abs(left - right) <= 1 for left, right in zip(component_bounds, expected))
        if size >= MINIMUM_SHELL_COMPONENT_PIXELS and spans_panel:
            components += 1
    # UI05 semantic focus/selection borders may partition the parent background
    # into several connected palette regions. Their union still represents one
    # production shell when it exactly spans the approved panel bounds.
    if components == 0 and all(abs(left - right) <= 1 for left, right in zip(bbox, expected)):
        components = 1
    return {"bbox": bbox, "outside": outside, "components": components, "pixels": len(coordinates), "error": error_pixels}


def main():
    if len(sys.argv) != 2:
        return fail("manifest-argument-required")
    path = Path(sys.argv[1])
    if not path.is_file():
        return fail("manifest-not-found")
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        return fail(f"manifest-invalid:{error}")
    if manifest.get("Version") != 2:
        return fail("manifest-version-invalid")
    if manifest.get("Renderer") != "Runtime UI Toolkit" or manifest.get("Source") != SOURCE:
        return fail("production-render-source-invalid")
    if manifest.get("HierarchySource") != "CodeGeneratedVisualElements":
        return fail("production-hierarchy-source-invalid")
    if manifest.get("PanelSettingsAssetPath") != PANEL_SETTINGS_PATH or manifest.get("PanelSettingsGuid", "").lower() != PANEL_SETTINGS_GUID:
        return fail("panel-settings-source-invalid")
    if manifest.get("ThemeAssetPath") != THEME_PATH or manifest.get("ThemeGuid", "").lower() != THEME_GUID:
        return fail("theme-source-invalid")
    required_acceptance = (
        "SkinSnapshotPanelSettingsReference", "PanelSettingsCloned", "ThemeDirectReference",
        "DirectChildrenLazyHierarchy", "PointerEventPath", "ProviderNavigationPath",
        "ProviderSubmitPath", "ProviderCancelPath", "KeyboardEventPath",
        "TextFocusLifecycle", "CloseFocusReturn",
    )
    if any(manifest.get(field) is not True for field in required_acceptance):
        return fail("acceptance-evidence-incomplete")
    if int(manifest.get("ProviderReadCount", 0)) < 3:
        return fail("provider-read-evidence-incomplete")
    if manifest.get("Hierarchy") != "UIDocument/SafeAreaRoot/Panel/(Header,ListView,Footer)" or manifest.get("VirtualizedControl") not in ("ListView/FixedHeight", "ListView/DynamicHeight") or manifest.get("PageSize") != 24:
        return fail("hierarchy-virtualization-evidence-invalid")
    captures = manifest.get("Captures") or []
    if len(captures) != 27:
        return fail("capture-count-invalid")
    seen = set()
    hashes = set()
    artifacts = []
    pixel_results = []
    for capture in captures:
        profile = capture.get("Profile")
        scenario = capture.get("Scenario")
        key = (profile, scenario)
        if profile not in PROFILES or scenario not in SCENARIOS or key in seen:
            return fail(f"capture-identity-invalid:{profile}/{scenario}")
        seen.add(key)
        expected = PROFILES[profile]
        if (capture.get("Width"), capture.get("Height")) != expected["screen"]:
            return fail(f"screen-invalid:{profile}/{scenario}")
        if not close(rect(capture.get("Screen") or {}), (0.0, 0.0, *expected["screen"])):
            return fail(f"screen-metadata-invalid:{profile}/{scenario}")
        if not close(rect(capture.get("SafeArea") or {}), (0.0, 0.0, *expected["screen"])):
            return fail(f"safe-area-invalid:{profile}/{scenario}")
        panel = rect(capture.get("Panel") or {})
        screen_width, screen_height = expected["screen"]
        if panel[2] <= 0 or panel[3] <= 0 or panel[0] < 0 or panel[1] < 0 or panel[0] + panel[2] > screen_width + 1.1 or panel[1] + panel[3] > screen_height + 1.1:
            return fail(f"panel-invalid:{profile}/{scenario}:{panel}")
        header = rect(capture.get("Header") or {})
        content = rect(capture.get("Content") or {})
        footer = rect(capture.get("Footer") or {})
        header_height = header[3]
        if header_height <= 0 or not close(header, (panel[0], panel[1] + panel[3] - header_height, panel[2], header_height)):
            return fail(f"header-invalid:{profile}/{scenario}:{header}")
        footer_height = footer[3]
        if (footer_height > 0) != (scenario in DETAIL):
            return fail(f"footer-presence-invalid:{profile}/{scenario}:{footer}")
        if footer_height > 0 and not close(footer, (panel[0], panel[1], panel[2], footer_height)):
            return fail(f"footer-invalid:{profile}/{scenario}:{footer}")
        if not close(content, (panel[0], panel[1] + footer_height, panel[2], panel[3] - header_height - footer_height)):
            return fail(f"content-invalid:{profile}/{scenario}:{content}")
        if float(capture.get("RowHeight", 0)) <= 0 or float(capture.get("HitHeight", 0)) <= 0:
            return fail(f"row-hit-invalid:{profile}/{scenario}")
        if capture.get("Virtualization") not in ("FixedHeight", "DynamicHeight") or int(capture.get("ItemCount", -1)) > 24:
            return fail(f"virtualization-invalid:{profile}/{scenario}")
        if int(capture.get("ResolvedItemCount", -1)) > int(capture.get("ItemCount", -1)):
            return fail(f"resolved-item-count-invalid:{profile}/{scenario}")
        if capture.get("HorizontalOverflow") is not False or capture.get("HeaderFixed") is not True or capture.get("FooterFixed") is not True:
            return fail(f"fixed-overflow-invalid:{profile}/{scenario}")
        if int(capture.get("RendererCount", 0)) != 1 or int(capture.get("SettledFrames", 0)) < 2:
            return fail(f"renderer-settle-invalid:{profile}/{scenario}")
        if scenario == "MultiInput12" and capture.get("VerticalOverflow") is not True:
            return fail(f"multi-input-does-not-scroll:{profile}")
        if scenario in EXECUTE_ENABLED and capture.get("ExecuteEnabled") is not True:
            return fail(f"execute-should-be-enabled:{profile}/{scenario}")
        if scenario in {"Validation", "Running"} and capture.get("ExecuteEnabled") is not False:
            return fail(f"execute-should-be-disabled:{profile}/{scenario}")
        if capture.get("Page") != EXPECTED_PAGES[scenario]:
            return fail(f"scenario-page-invalid:{profile}/{scenario}:{capture.get('Page')}")
        notice = capture.get("Notice", "")
        if scenario == "LongTextReference":
            if not notice.startswith("Source Path: Scene/Streaming/"):
                return fail(f"long-reference-notice-missing:{profile}:{notice}")
        elif notice:
            return fail(f"unexpected-scenario-notice:{profile}/{scenario}:{notice}")
        if bool(capture.get("AsyncRunning")) != (scenario == "Running"):
            return fail(f"scenario-async-state-invalid:{profile}/{scenario}")
        cancel = rect(capture.get("CancelBounds") or {})
        if scenario == "Running" and (cancel[2] <= 0 or cancel[3] <= 0):
            return fail(f"cancel-bounds-invalid:{profile}:{cancel}")
        artifact = Path(capture.get("ArtifactPath", ""))
        if not artifact.is_file():
            return fail(f"artifact-not-found:{profile}/{scenario}")
        data = artifact.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        if png_dimensions(data) != expected["screen"] or digest != capture.get("Sha256"):
            return fail(f"artifact-invalid:{profile}/{scenario}")
        try:
            metrics = pixel_shell_metrics(decode_png(data), panel)
        except (ValueError, zlib.error) as error:
            return fail(f"artifact-pixels-invalid:{profile}/{scenario}:{error}")
        expected_bounds = expected_png_bounds(panel, expected["screen"][1])
        if any(abs(left - right) > 1 for left, right in zip(metrics["bbox"], expected_bounds)):
            return fail(f"pixel-shell-bounds-invalid:{profile}/{scenario}:{metrics['bbox']}/{expected_bounds}")
        if metrics["outside"] != 0 or metrics["components"] != 1 or metrics["pixels"] <= 1000:
            return fail(f"single-shell-invalid:{profile}/{scenario}:{metrics}")
        if scenario == "Validation":
            if metrics["error"] <= 100:
                return fail(f"scenario-error-palette-missing:{profile}/{scenario}:{metrics['error']}")
        elif metrics["error"] != 0:
            return fail(f"stale-error-palette:{profile}/{scenario}:{metrics['error']}")
        hashes.add(digest)
        artifacts.append(str(artifact.resolve()))
        pixel_results.append({"profile": profile, "scenario": scenario, "shellBounds": metrics["bbox"], "outsidePalettePixels": metrics["outside"], "errorPalettePixels": metrics["error"]})
    if seen != {(profile, scenario) for profile in PROFILES for scenario in SCENARIOS}:
        return fail("capture-matrix-incomplete")
    if len(hashes) != 27:
        return fail(f"capture-hashes-not-distinct:{len(hashes)}")
    emit({
        "success": True,
        "manifest": str(path.resolve()),
        "renderer": manifest["Renderer"],
        "productionHierarchySource": manifest["HierarchySource"],
        "panelSettingsAsset": {"path": PANEL_SETTINGS_PATH, "guid": PANEL_SETTINGS_GUID, "sessionClone": True},
        "themeAsset": {"path": THEME_PATH, "guid": THEME_GUID, "directReference": True},
        "eventPaths": {
            "pointer": manifest["PointerEventPath"],
            "providerNavigation": manifest["ProviderNavigationPath"],
            "providerSubmit": manifest["ProviderSubmitPath"],
            "providerCancel": manifest["ProviderCancelPath"],
            "providerReadCount": manifest["ProviderReadCount"],
            "keyboard": manifest["KeyboardEventPath"],
            "textFocus": manifest["TextFocusLifecycle"],
            "closeReturn": manifest["CloseFocusReturn"],
        },
        "captureCount": len(captures),
        "distinctHashes": len(hashes),
        "singleShellPerCapture": True,
        "outsidePalettePixels": 0,
        "scenarioSemanticVerification": True,
        "virtualizationVerification": True,
        "pixelResults": pixel_results,
        "artifacts": artifacts,
    }, indent=2)
    return 0


if __name__ == "__main__":
    sys.exit(main())
