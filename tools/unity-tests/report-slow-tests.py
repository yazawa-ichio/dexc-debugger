"""Summarize where test time goes, from one or more NUnit XML reports.

Usage: python report-slow-tests.py <report.xml> [more.xml ...] [--top N] [--threshold SECONDS] [--json]
Prints wall clock, the sum of test durations, the slowest tests and the slowest fixtures.
Tests above the threshold are the ones to shorten or move behind a filter.
"""
import argparse
import json
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from datetime import datetime


def parse_time(value):
    if not value:
        return None
    try:
        return datetime.strptime(value, "%Y-%m-%d %H:%M:%SZ")
    except ValueError:
        return None


def analyze(path):
    root = ET.parse(path).getroot()
    cases = []
    for case in root.iter("test-case"):
        cases.append({
            "name": case.get("fullname") or case.get("name"),
            "fixture": (case.get("classname") or "").rsplit(".", 1)[-1],
            "duration": float(case.get("duration") or 0.0),
            "result": case.get("result"),
        })
    start = parse_time(root.get("start-time"))
    end = parse_time(root.get("end-time"))
    fixtures = defaultdict(float)
    for case in cases:
        fixtures[case["fixture"]] += case["duration"]
    return {
        "report": path,
        "total": len(cases),
        "failed": sum(1 for case in cases if case["result"] == "Failed"),
        "testTimeSeconds": round(sum(case["duration"] for case in cases), 1),
        "runSeconds": round((end - start).total_seconds(), 1) if start and end else None,
        "cases": sorted(cases, key=lambda case: -case["duration"]),
        "fixtures": sorted(fixtures.items(), key=lambda item: -item[1]),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("reports", nargs="+")
    parser.add_argument("--top", type=int, default=10)
    parser.add_argument("--threshold", type=float, default=5.0, help="seconds; tests above this are listed as slow")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args()

    summaries = []
    for path in args.reports:
        if not path:
            continue
        try:
            summaries.append(analyze(path))
        except (OSError, ET.ParseError) as error:
            print("cannot read " + path + ": " + str(error), file=sys.stderr)
    if not summaries:
        return 2

    if args.json:
        output = []
        for summary in summaries:
            output.append({
                "report": summary["report"],
                "total": summary["total"],
                "failed": summary["failed"],
                "testTimeSeconds": summary["testTimeSeconds"],
                "runSeconds": summary["runSeconds"],
                "slow": [case for case in summary["cases"] if case["duration"] > args.threshold],
                "fixtures": [{"fixture": name, "seconds": round(seconds, 1)} for name, seconds in summary["fixtures"][:args.top]],
            })
        print(json.dumps(output, indent=2))
        return 0

    for summary in summaries:
        slow = [case for case in summary["cases"] if case["duration"] > args.threshold]
        print("== " + summary["report"])
        print("   tests %d (failed %d), test time %.1fs, run %s" % (
            summary["total"], summary["failed"], summary["testTimeSeconds"],
            "%.1fs" % summary["runSeconds"] if summary["runSeconds"] is not None else "n/a"))
        print("   slow tests (> %.0fs): %d" % (args.threshold, len(slow)))
        for case in summary["cases"][:args.top]:
            print("   %7.1fs  %s" % (case["duration"], case["name"]))
        print("   by fixture:")
        for name, seconds in summary["fixtures"][:args.top]:
            print("   %7.1fs  %s" % (seconds, name))
    return 0


if __name__ == "__main__":
    sys.exit(main())
