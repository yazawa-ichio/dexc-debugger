#!/usr/bin/env python3
"""Return a small pass/fail summary for a Unity NUnit XML report."""

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def emit(summary: dict, as_json: bool) -> None:
    if as_json:
        print(json.dumps(summary, indent=2))
    else:
        for key, value in summary.items():
            print(f"{key}: {value}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("report")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args()
    report = Path(args.report)

    if not report.is_file():
        emit({"success": False, "reason": "report-not-found", "report": str(report)}, args.json)
        return 2

    try:
        root = ET.parse(report).getroot()
        if root.tag != "test-run":
            raise ValueError("The report root is not test-run.")
        result = root.attrib.get("result", "")
        total = int(root.attrib.get("total", "0"))
        passed = int(root.attrib.get("passed", "0"))
        failed = int(root.attrib.get("failed", "0"))
        inconclusive = int(root.attrib.get("inconclusive", "0"))
        # The capture tests ignore themselves outside the --graphics runner, so an ordinary green run reports
        # Skipped:Ignored rather than Passed. That is still a pass; a run where nothing passed is not.
        settled = result in ("Passed", "Skipped:Ignored")
        summary = {
            "success": settled and passed > 0 and failed == 0 and inconclusive == 0,
            "result": result,
            "total": total,
            "passed": passed,
            "failed": failed,
            "skipped": int(root.attrib.get("skipped", "0")),
            "inconclusive": inconclusive,
            "report": str(report.resolve()),
        }
    except (ET.ParseError, OSError, ValueError) as error:
        emit({"success": False, "reason": "invalid-report", "message": str(error), "report": str(report)}, args.json)
        return 2

    emit(summary, args.json)
    return 0 if summary["success"] else 1


if __name__ == "__main__":
    sys.exit(main())
