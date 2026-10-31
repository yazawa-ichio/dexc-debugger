"""Strict RGB/RGBA 8-bit, non-interlaced PNG reader for capture verification."""

import struct
import zlib


SIGNATURE = b"\x89PNG\r\n\x1a\n"
MAX_DECODED_BYTES = 256 * 1024 * 1024


def decode_png(data):
    if data[:8] != SIGNATURE:
        raise ValueError("invalid-png-signature")
    offset = 8
    header = None
    compressed = bytearray()
    seen_idat = False
    seen_palette = False
    idat_ended = False
    ended = False
    while offset < len(data):
        if len(data) - offset < 12:
            raise ValueError("truncated-png-chunk")
        length, kind = struct.unpack_from(">I4s", data, offset)
        end = offset + 12 + length
        if length > 0x7fffffff or end > len(data):
            raise ValueError("invalid-png-chunk-length")
        if not all(65 <= value <= 90 or 97 <= value <= 122 for value in kind) or kind[2] & 32:
            raise ValueError("invalid-png-chunk-type")
        chunk = data[offset + 8:end - 4]
        crc = struct.unpack_from(">I", data, end - 4)[0]
        if zlib.crc32(kind + chunk) & 0xffffffff != crc:
            raise ValueError("invalid-png-crc")
        if header is None and kind != b"IHDR":
            raise ValueError("missing-png-header")
        if kind == b"IHDR":
            if header is not None or length != 13:
                raise ValueError("invalid-png-header")
            header = struct.unpack(">IIBBBBB", chunk)
            width, height, depth, color, compression, filtering, interlace = header
            if not 0 < width <= 0x7fffffff or not 0 < height <= 0x7fffffff:
                raise ValueError("invalid-png-dimensions")
            if depth != 8 or color not in (2, 6) or compression != 0 or filtering != 0 or interlace != 0:
                raise ValueError("unsupported-png-format")
            channels = 4 if color == 6 else 3
            stride = width * channels
            expected = height * (stride + 1)
            if expected > MAX_DECODED_BYTES:
                raise ValueError("png-decoded-size-limit")
        elif kind == b"IDAT":
            if idat_ended:
                raise ValueError("nonconsecutive-png-idat")
            seen_idat = True
            compressed.extend(chunk)
        elif kind == b"IEND":
            if length != 0 or not seen_idat or end != len(data):
                raise ValueError("invalid-png-end")
            ended = True
        else:
            if seen_idat:
                idat_ended = True
            if kind == b"PLTE":
                if seen_palette or seen_idat or length == 0 or length % 3 or length > 768:
                    raise ValueError("invalid-png-palette")
                seen_palette = True
            elif not kind[0] & 32:
                raise ValueError("unsupported-png-critical-chunk")
        offset = end
    if not ended:
        raise ValueError("missing-png-end")
    try:
        inflater = zlib.decompressobj()
        raw = inflater.decompress(compressed, expected + 1)
    except zlib.error as error:
        raise ValueError("invalid-png-compression") from error
    if len(raw) != expected or not inflater.eof or inflater.unused_data or inflater.unconsumed_tail:
        raise ValueError("png-data-length-invalid")
    pixels = bytearray(height * stride)
    source = 0
    for row in range(height):
        filter_type = raw[source]
        source += 1
        if filter_type > 4:
            raise ValueError("unsupported-png-filter")
        target = row * stride
        if filter_type == 0:
            pixels[target:target + stride] = raw[source:source + stride]
            source += stride
            continue
        for column in range(stride):
            value = raw[source]
            source += 1
            left = pixels[target + column - channels] if column >= channels else 0
            up = pixels[target + column - stride] if row else 0
            upper_left = pixels[target + column - stride - channels] if row and column >= channels else 0
            if filter_type == 1:
                value += left
            elif filter_type == 2:
                value += up
            elif filter_type == 3:
                value += (left + up) // 2
            elif filter_type == 4:
                estimate = left + up - upper_left
                distances = abs(estimate - left), abs(estimate - up), abs(estimate - upper_left)
                value += left if distances[0] <= distances[1] and distances[0] <= distances[2] else up if distances[1] <= distances[2] else upper_left
            pixels[target + column] = value & 255
    return {"width": width, "height": height, "channels": channels, "pixels": pixels}


def decode_png_tuple(data):
    image = decode_png(data)
    return image["width"], image["height"], image["channels"], image["pixels"]
