#!/usr/bin/env python3
"""Validate UI01 Runtime IMGUI responsive visual-capture artifacts."""

import hashlib
import json
import math
import struct
import sys
import zlib
from pathlib import Path

from png_decoder import decode_png


# Only the screens are fixed here. Where the panel, header and footer sit is the renderer's layout, which the C#
# tests assert; this checks that the manifest's rectangles agree with each other and with the pixels.
PROFILES = {
    "MobilePortrait": {"screen": (390, 844)},
    "MobileLandscape": {"screen": (844, 390)},
    "WindowsPlayer": {"screen": (1366, 768)},
}
LEGACY_PAGES = {"Home", "CommandList", "CommandDetailDraft", "ResultState", "History"}
SCENARIOS = LEGACY_PAGES | {"MultiInput12", "Validation", "LongTextReference", "Running"}
DETAIL_SCENARIOS = {
    "CommandDetailDraft", "ResultState", "MultiInput12", "Validation",
    "LongTextReference", "Running",
}
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
EXECUTE_ENABLED = {"CommandDetailDraft", "ResultState", "MultiInput12", "LongTextReference"}
SOURCE = (
    "DexcRuntimeUIDriver.OnGUI -> DexcRuntimeUIRenderer.DrawMenuAndDisplay -> "
    "ScreenCapture.CaptureScreenshotAsTexture"
)


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


def pixel(image, x, y):
    index = (y * image["width"] + x) * image["channels"]
    return tuple(image["pixels"][index:index + 3])


def pixel_bounds(image, value):
    x, y, width, height = value
    return (
        max(0, min(image["width"], math.floor(x))),
        max(0, min(image["height"], math.floor(y))),
        max(0, min(image["width"], math.ceil(x + width))),
        max(0, min(image["height"], math.ceil(y + height))),
    )


def count_visible(image, value, background):
    x_min, y_min, x_max, y_max = pixel_bounds(image, value)
    return sum(
        1
        for y in range(y_min, y_max)
        for x in range(x_min, x_max)
        if any(abs(channel - base) > 8 for channel, base in zip(pixel(image, x, y), background))
    )


def count_ink(image, value):
    x_min, y_min, x_max, y_max = pixel_bounds(image, value)
    return sum(
        1
        for y in range(y_min, y_max)
        for x in range(x_min, x_max)
        if max(pixel(image, x, y)) > 64
    )


def visible_horizontal_bounds(image, value, background):
    x_min, y_min, x_max, y_max = pixel_bounds(image, value)
    visible_x = [
        x
        for y in range(y_min, y_max)
        for x in range(x_min, x_max)
        if any(abs(channel - base) > 8 for channel, base in zip(pixel(image, x, y), background))
    ]
    return (-1, -1) if not visible_x else (min(visible_x), max(visible_x))


def horizontal_scrollbar_metrics(image, value):
    x_min, y_min, x_max, y_max = pixel_bounds(image, value)
    y_min = max(y_min, y_max - 24)
    minimum_run = math.ceil(max(0.0, float(value[2])) * 0.45)
    maximum_run = 0
    matching_rows = 0
    for y in range(y_min, y_max):
        run = 0
        row_maximum = 0
        for x in range(x_min, x_max):
            channels = pixel(image, x, y)
            high = max(channels)
            low = min(channels)
            run = run + 1 if 30 <= high <= 190 and high - low <= 12 else 0
            row_maximum = max(row_maximum, run)
        maximum_run = max(maximum_run, row_maximum)
        if row_maximum >= minimum_run:
            matching_rows += 1
    return maximum_run, matching_rows


def rect(value):
    return (
        float(value.get("X", 0)),
        float(value.get("Y", 0)),
        float(value.get("Width", 0)),
        float(value.get("Height", 0)),
    )


def close_rect(actual, expected):
    return all(abs(left - right) <= 0.01 for left, right in zip(actual, expected))


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

    version = manifest.get("Version")
    if version not in {2, 3}:
        return fail("manifest-version-invalid")
    if manifest.get("Renderer") != "Runtime IMGUI":
        return fail("renderer-invalid")
    if manifest.get("Source") != SOURCE:
        return fail("production-render-source-invalid")
    if float(manifest.get("RowHeight", 0)) <= 0:
        return fail("manifest-row-height-invalid")

    identities = SCENARIOS if version == 3 else LEGACY_PAGES
    captures = manifest.get("Captures") or []
    if len(captures) != len(PROFILES) * len(identities):
        return fail("capture-count-invalid")
    seen = set()
    hashes_by_profile = {profile: set() for profile in PROFILES}
    footer_bounds_by_profile = {profile: {} for profile in PROFILES}
    verified = []
    for capture in captures:
        profile = capture.get("Profile")
        scenario = capture.get("Scenario") if version == 3 else capture.get("Page")
        page = capture.get("Page") if version == 3 else scenario
        key = (profile, scenario)
        if profile not in PROFILES or scenario not in identities or key in seen:
            return fail(f"capture-identity-invalid:{profile}/{scenario}")
        seen.add(key)
        expected = PROFILES[profile]
        screen_width, screen_height = expected["screen"]
        if not close_rect(rect(capture.get("Screen") or {}), (0.0, 0.0, screen_width, screen_height)):
            return fail(f"screen-invalid:{profile}/{scenario}")
        if not close_rect(rect(capture.get("SafeArea") or {}), (0.0, 0.0, screen_width, screen_height)):
            return fail(f"safe-area-invalid:{profile}/{scenario}")
        panel = rect(capture.get("Panel") or {})
        if panel[2] <= 0 or panel[3] <= 0 or panel[0] < 0 or panel[1] < 0 or panel[0] + panel[2] > screen_width + 0.01 or panel[1] + panel[3] > screen_height + 0.01:
            return fail(f"panel-invalid:{profile}/{scenario}:{panel}")
        header = rect(capture.get("Header") or {})
        content = rect(capture.get("ContentViewport") or {})
        footer = rect(capture.get("Footer") or {})
        header_height = header[3]
        if header_height <= 0 or not close_rect(header, (panel[0], panel[1], panel[2], header_height)):
            return fail(f"header-invalid:{profile}/{scenario}")
        footer_height = footer[3]
        if (footer_height > 0) != (scenario in DETAIL_SCENARIOS):
            return fail(f"footer-presence-invalid:{profile}/{scenario}:{footer}")
        if not close_rect(content, (panel[0], panel[1] + header_height, panel[2], panel[3] - header_height - footer_height)):
            return fail(f"content-viewport-invalid:{profile}/{scenario}")
        if not close_rect(footer, (panel[0], panel[1] + panel[3] - footer_height, panel[2], footer_height)):
            return fail(f"footer-invalid:{profile}/{scenario}:{footer}")
        if float(capture.get("RowHeight", 0)) <= 0:
            return fail(f"row-height-invalid:{profile}/{scenario}")
        if int(capture.get("SettledFrames", 0)) < 2:
            return fail(f"capture-not-stabilized:{profile}/{scenario}")
        if capture.get("ShellVerified") is not True:
            return fail(f"shell-not-verified:{profile}/{scenario}")
        if capture.get("ContentVerified") is not True:
            return fail(f"content-not-verified:{profile}/{scenario}")
        if int(capture.get("NonBackgroundPixels", 0)) <= 100:
            return fail(f"capture-empty:{profile}/{scenario}")
        if int(capture.get("PanelColorBuckets", 0)) <= 8:
            return fail(f"panel-content-empty:{profile}/{scenario}")

        if version == 3:
            if page != EXPECTED_PAGES[scenario]:
                return fail(f"scenario-page-invalid:{profile}/{scenario}:{page}")
            if capture.get("HeaderFixed") is not True or capture.get("FooterFixed") is not True:
                return fail(f"fixed-region-invalid:{profile}/{scenario}")
            if scenario == "MultiInput12" and capture.get("VerticalOverflow") is not True:
                return fail(f"multi-input-does-not-scroll:{profile}")
            if bool(capture.get("ExecuteEnabled")) != (scenario in EXECUTE_ENABLED):
                return fail(f"execute-state-invalid:{profile}/{scenario}")
            notice = capture.get("Notice", "")
            if scenario == "LongTextReference":
                if not notice.startswith("Source Path: Scene/Streaming/"):
                    return fail(f"long-reference-notice-missing:{profile}:{notice}")
            elif scenario == "Validation":
                if notice != "Command validation failed.":
                    return fail(f"validation-notice-invalid:{profile}:{notice}")
            elif notice:
                return fail(f"unexpected-scenario-notice:{profile}/{scenario}:{notice}")
            if bool(capture.get("AsyncRunning")) != (scenario == "Running"):
                return fail(f"scenario-async-state-invalid:{profile}/{scenario}")
            if bool(capture.get("CancelVisible")) != (scenario == "Running"):
                return fail(f"cancel-state-invalid:{profile}/{scenario}")

        artifact_path = Path(capture.get("ArtifactPath", ""))
        if not artifact_path.is_file():
            return fail(f"artifact-not-found:{profile}/{scenario}")
        data = artifact_path.read_bytes()
        if png_dimensions(data) != (screen_width, screen_height):
            return fail(f"artifact-dimensions-invalid:{profile}/{scenario}")
        digest = hashlib.sha256(data).hexdigest()
        if digest != capture.get("Sha256"):
            return fail(f"artifact-hash-invalid:{profile}/{scenario}")
        try:
            image = decode_png(data)
        except (ValueError, zlib.error) as error:
            return fail(f"artifact-pixels-invalid:{profile}/{scenario}:{error}")
        background = pixel(image, 0, 0)
        horizontal_scrollbar_max_run, horizontal_scrollbar_rows = horizontal_scrollbar_metrics(image, content)
        horizontal_overflow = horizontal_scrollbar_rows >= 2
        if horizontal_overflow:
            return fail(f"horizontal-scrollbar-visible:{profile}/{scenario}:{horizontal_scrollbar_max_run}/{horizontal_scrollbar_rows}")
        if version == 3:
            if bool(capture.get("HorizontalOverflow")) != horizontal_overflow:
                return fail(f"horizontal-overflow-metadata-mismatch:{profile}/{scenario}")
            has_scrollbar_metrics = "HorizontalScrollbarMaxRun" in capture or "HorizontalScrollbarRows" in capture
            if has_scrollbar_metrics:
                if int(capture.get("HorizontalScrollbarMaxRun", -1)) != horizontal_scrollbar_max_run:
                    return fail(f"horizontal-scrollbar-run-mismatch:{profile}/{scenario}")
                if int(capture.get("HorizontalScrollbarRows", -1)) != horizontal_scrollbar_rows:
                    return fail(f"horizontal-scrollbar-rows-mismatch:{profile}/{scenario}")
        has_back = page != "Home"
        back_rect = (header[0], header[1], min(40.0, header[2]), header[3])
        title_x = header[0] + (40.0 if has_back else 0.0)
        title_rect = (title_x, header[1], min(160.0, max(0.0, header[0] + header[2] - 40.0 - title_x)), header[3])
        close_rect_pixels = (max(header[0], header[0] + header[2] - 40.0), header[1], min(40.0, header[2]), header[3])
        back_pixels = count_visible(image, back_rect, background)
        title_pixels = count_visible(image, title_rect, background)
        close_pixels = count_visible(image, close_rect_pixels, background)
        outside_pixels = count_ink(image, (0.0, content[1], panel[0], content[3])) + count_ink(
            image, (panel[0] + panel[2], content[1], max(0.0, screen_width - panel[0] - panel[2]), content[3])
        )
        left_boundary_pixels = count_ink(image, (content[0], content[1], min(2.0, content[2]), content[3]))
        footer_bounds = visible_horizontal_bounds(image, footer, background) if footer[3] > 0 else (-1, -1)
        if has_back and back_pixels <= 40:
            return fail(f"header-back-missing:{profile}/{scenario}")
        if title_pixels <= 80:
            return fail(f"header-title-missing:{profile}/{scenario}")
        if close_pixels <= 40:
            return fail(f"header-close-missing:{profile}/{scenario}")
        if outside_pixels != 0:
            return fail(f"content-outside-panel:{profile}/{scenario}:{outside_pixels}")
        if left_boundary_pixels != 0:
            return fail(f"content-left-clipped:{profile}/{scenario}:{left_boundary_pixels}")
        if footer[3] > 0:
            footer_width = footer_bounds[1] - footer_bounds[0] + 1
            if footer_bounds[0] < footer[0] or footer_bounds[1] >= footer[0] + footer[2] or footer_bounds[0] - footer[0] > 6 or footer[0] + footer[2] - 1 - footer_bounds[1] > 6 or footer_width < footer[2] - 12:
                return fail(f"footer-action-clipped:{profile}/{scenario}:{footer_bounds}")
            footer_bounds_by_profile[profile][scenario] = footer_bounds
        measured = {
            "HeaderBackVisiblePixels": back_pixels,
            "HeaderTitleVisiblePixels": title_pixels,
            "HeaderCloseVisiblePixels": close_pixels,
            "ContentOutsideInkPixels": outside_pixels,
            "ContentLeftBoundaryInkPixels": left_boundary_pixels,
            "FooterVisibleXMin": footer_bounds[0],
            "FooterVisibleXMax": footer_bounds[1],
        }
        if "HorizontalScrollbarMaxRun" in capture or "HorizontalScrollbarRows" in capture:
            measured["HorizontalScrollbarMaxRun"] = horizontal_scrollbar_max_run
            measured["HorizontalScrollbarRows"] = horizontal_scrollbar_rows
        for field, value in measured.items():
            if int(capture.get(field, -2)) != value:
                return fail(f"pixel-metric-mismatch:{profile}/{scenario}:{field}")
        hashes_by_profile[profile].add(digest)
        verified.append(str(artifact_path.resolve()))

    if seen != {(profile, identity) for profile in PROFILES for identity in identities}:
        return fail("capture-matrix-incomplete")
    for profile, hashes in hashes_by_profile.items():
        if len(hashes) != len(identities):
            return fail(f"scenario-images-not-distinct:{profile}")
        draft_footer = footer_bounds_by_profile[profile].get("CommandDetailDraft")
        result_footer = footer_bounds_by_profile[profile].get("ResultState")
        if draft_footer is None or result_footer is None or any(abs(left - right) > 1 for left, right in zip(draft_footer, result_footer)):
            return fail(f"result-footer-differs-from-detail:{profile}:{draft_footer}/{result_footer}")

    print(json.dumps({
        "success": True,
        "manifest": str(manifest_path.resolve()),
        "renderer": manifest["Renderer"],
        "source": manifest["Source"],
        "rowHeight": manifest["RowHeight"],
        "captureCount": len(captures),
        "profiles": sorted(PROFILES),
        "scenarios": sorted(identities),
        "pixelShellVerification": True,
        "horizontalOverflowPixelVerification": True,
        "artifacts": verified,
    }, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
