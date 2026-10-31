#!/usr/bin/env python3
"""PNG corruption and adapter regression tests; no Unity or third-party modules."""

import importlib.util
import struct
import subprocess
import sys
import types
import unittest
import zlib
from pathlib import Path

from png_decoder import decode_png


DIRECTORY = Path(__file__).resolve().parent
ADAPTERS = ["verify-runtime-ui-capture.py", "verify-runtime-ugui-capture.py",
    "verify-runtime-uitoolkit-capture.py"]
BASELINE = None
if "--baseline" in sys.argv:
    index = sys.argv.index("--baseline")
    BASELINE = sys.argv[index + 1]
    del sys.argv[index:index + 2]


def load(filename):
    path = DIRECTORY / filename
    if BASELINE:
        source = subprocess.check_output(["git", "show", BASELINE + ":tools/unity-tests/" + filename], cwd=DIRECTORY)
        module = types.ModuleType(filename)
        module.__file__ = str(path)
        exec(compile(source, str(path), "exec"), module.__dict__)
        return module
    spec = importlib.util.spec_from_file_location(filename, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def chunk(kind, body):
    return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xffffffff)


def png(raw=b"\0\x19\x32\x64", width=1, height=1, color=2, depth=8, compression=0, filtering=0, interlace=0):
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, depth, color, compression, filtering, interlace)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


class PngTests(unittest.TestCase):
    def test_filters_and_adapters_preserve_exact_pixels(self):
        for channels in (3, 4):
            # Hand-computed second-row filter bytes for two pixels exercise left/up/Paeth ties.
            first = bytes([10, 30, 50, 70][:channels] * 2)
            second = bytes([20, 40, 60, 80][:channels] + [30, 50, 70, 90][:channels])
            for filter_type in range(5):
                encoded = bytearray()
                for i, value in enumerate(second):
                    left = second[i - channels] if i >= channels else 0
                    up = first[i]
                    ul = first[i - channels] if i >= channels else 0
                    p = left + up - ul
                    paeth = min((left, up, ul), key=lambda x: abs(p - x))
                    prediction = (0, left, up, (left + up) // 2, paeth)[filter_type]
                    encoded.append((value - prediction) & 255)
                data = png(b"\0" + first + bytes([filter_type]) + encoded, 2, 2, 6 if channels == 4 else 2)
                for name in ADAPTERS:
                    with self.subTest(channels=channels, filter=filter_type, adapter=name):
                        image = load(name).decode_png(data)
                        actual = image[3] if isinstance(image, tuple) else image["pixels"]
                        self.assertEqual(bytes(actual), first + second)

    def test_malformed_png_is_rejected_by_every_adapter(self):
        valid = png()
        header, idat, end = valid[8:33], valid[33:-12], valid[-12:]
        corruptions = {
            "short-raw": png(b"\0\x19"), "long-raw": png(b"\0\x19\x32\x64\x00"),
            "no-header": valid[:8] + idat + end, "no-idat": valid[:33] + end,
            "no-end": valid[:-12], "truncated-chunk": valid[:-2],
            "crc": valid[:29] + b"\0\0\0\0" + valid[33:],
            "duplicate-header": valid[:33] + header + idat + end,
            "trailing-bytes": valid + b"x", "nonempty-end": valid[:-12] + chunk(b"IEND", b"x"),
            "zero-width": png(b"\0", width=0), "zero-height": png(b"", height=0),
            "compression-method": png(compression=1), "filter-method": png(filtering=1),
            "filter": png(b"\5\x19\x32\x64"), "interlaced": png(interlace=1),
            "depth": png(depth=16), "color": png(color=0),
            "bad-zlib": valid[:33] + chunk(b"IDAT", b"bad") + end,
            "zlib-tail": valid[:33] + chunk(b"IDAT", zlib.compress(b"\0\x19\x32\x64") + b"x") + end,
            "missing-zlib-end": valid[:33] + chunk(b"IDAT", zlib.compress(b"\0\x19\x32\x64")[:-2]) + end,
            "critical": valid[:33] + chunk(b"ABCD", b"") + idat + end,
            "header-length": valid[:8] + chunk(b"IHDR", valid[16:29] + b"x") + idat + end,
            "chunk-overrun": valid[:33] + struct.pack(">I", 0x7fffffff) + b"IDAT" + b"\0\0\0\0",
            "duplicate-palette": valid[:33] + chunk(b"PLTE", b"\0\0\0") * 2 + idat + end,
        }
        for name in ADAPTERS:
            decoder = load(name).decode_png
            for case, data in corruptions.items():
                with self.subTest(adapter=name, case=case):
                    with self.assertRaises(ValueError):
                        decoder(data)

    def test_chunk_splitting_and_ancillary_metadata(self):
        data = png()
        compressed = zlib.compress(b"\0\x19\x32\x64")
        split = data[:33] + chunk(b"tEXt", b"key\0value") + chunk(b"IDAT", compressed[:3]) + chunk(b"IDAT", compressed[3:]) + data[-12:]
        self.assertEqual(decode_png(split), decode_png(data))
        separated = data[:33] + chunk(b"IDAT", compressed[:3]) + chunk(b"tEXt", b"key\0value") + chunk(b"IDAT", compressed[3:]) + data[-12:]
        with self.assertRaises(ValueError):
            decode_png(separated)

    def test_large_dimensions_are_rejected_before_allocation(self):
        with self.assertRaisesRegex(ValueError, "size-limit"):
            decode_png(png(width=0x7fffffff))


if __name__ == "__main__":
    unittest.main()
