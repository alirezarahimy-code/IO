#!/usr/bin/env python3
"""Copy exactly two PUBLIC vendor images from pinned TWF 4.28 MyTime3.dll.

No NuGet/Pillow/HTTP, no Web.config/LogoPath/customer image, no BinaryFormatter
DESERIALIZATION, no SQL and no write to the original. The original's public
Home/MyBanner(2,3) selects ImagesResource.Arm/logo1. This bounded extractor
reads .NET resource metadata, extracts the embedded BMP/PNG byte streams and
encodes the 24-bit BMP as an offline PNG using Python's standard library.
The Windows binary probe independently checks ORIGINAL decoded pixels against
both generated new-app images in the same CI run.
"""
import hashlib
import pathlib
import struct
import sys
import zlib

EXPECTED_DLL = "edba04ccafb9f111870f9050f86e9059e92389188bbd3115678afb21691ddd6b"
EXPECTED_EMBEDDED = {
    "Arm": "360812e7d2631ead03e23ec997b2dedeada6ab9a881898ad656d4904e59a1790",
    "logo1": "bb2d2c1ebc50f02c1aae57a4e50a3f4b8d1b00641b5d1ea4660a95ffce72572e",
}


def u16(b, p):
    return struct.unpack_from("<H", b, p)[0]


def u32(b, p):
    return struct.unpack_from("<I", b, p)[0]


def seven(b, p):
    value = 0
    for i in range(5):
        byte = b[p]
        p += 1
        value |= (byte & 127) << (7 * i)
        if byte < 128:
            return value, p
    raise ValueError("oversized resource length")


def text(b, p, encoding="utf-8"):
    length, p = seven(b, p)
    if length > 300:
        raise ValueError("unexpectedly long resource name/type")
    return b[p:p + length].decode(encoding), p + length


def resource_stream(b):
    pe = u32(b, 0x3c)
    if b[pe:pe + 4] != b"PE\0\0":
        raise ValueError("not a pinned PE")
    optional = pe + 24
    magic = u16(b, optional)
    directory = optional + (96 if magic == 0x10b else 112 if magic == 0x20b else 0)
    if directory == optional:
        raise ValueError("not a managed PE")
    cli_rva = u32(b, directory + 14 * 8)
    sections = optional + u16(b, pe + 20)

    def raw(rva):
        for i in range(u16(b, pe + 6)):
            s = sections + i * 40
            length = max(u32(b, s + 8), u32(b, s + 16))
            base = u32(b, s + 12)
            if base <= rva < base + length:
                return u32(b, s + 20) + rva - base
        raise ValueError("original CLR resource is not in a mapped section")

    cli = raw(cli_rva)
    resource_rva, resource_size = struct.unpack_from("<II", b, cli + 24)
    if not resource_rva or resource_size > len(b):
        raise ValueError("unexpected original resource directory")
    start = raw(resource_rva)
    end = start + resource_size
    while start + 8 <= end:
        length = u32(b, start)
        if length < 16 or start + 4 + length > end:
            raise ValueError("invalid original embedded resource length")
        yield start + 4, start + 4 + length
        start += 4 + length


def images_from_original(b):
    for r, end in resource_stream(b):
        if u32(b, r) != 0xBEEFCACE:
            continue
        p = r + 12 + u32(b, r + 8)
        version, count, types = struct.unpack_from("<iii", b, p)
        p += 12
        if version != 2 or not 0 <= count <= 2000 or not 0 <= types < 100:
            raise ValueError("unexpected original .resources format")
        names_of_types = []
        for _ in range(types):
            type_name, p = text(b, p)
            names_of_types.append(type_name)
        p += (-p) % 8
        p += 4 * count  # name hashes
        positions = p
        p += 4 * count
        data_offset = u32(b, p)
        names = p + 4
        if not any(name.startswith("System.Drawing.Bitmap, System.Drawing,")
                   for name in names_of_types):
            continue
        result = {}
        for i in range(count):
            name, after = text(b, names + u32(b, positions + i * 4), "utf-16-le")
            if name not in EXPECTED_EMBEDDED:
                continue
            (offset,) = struct.unpack_from("<i", b, after)
            location = r + data_offset + offset
            typecode, payload = seven(b, location)
            if typecode != 64 or b[payload + 160] != 2:
                raise ValueError("unexpected original bitmap payload header")
            length = u32(b, payload + 156)
            original_bytes = b[payload + 161:payload + 161 + length]
            if payload + 161 + length > end or len(original_bytes) != length:
                raise ValueError("original bitmap payload escaped resource")
            if hashlib.sha256(original_bytes).hexdigest() != EXPECTED_EMBEDDED[name]:
                raise ValueError("pinned original image content changed")
            result[name] = original_bytes
        if result.keys() == EXPECTED_EMBEDDED.keys():
            return result
    raise ValueError("the two public original image resources were not found")


def png_chunk(kind, value):
    data = kind + value
    return struct.pack(">I", len(value)) + data + struct.pack(">I", zlib.crc32(data))


def bmp24_to_png(bitmap):
    if not bitmap.startswith(b"BM") or u32(bitmap, 2) != len(bitmap) or u32(bitmap, 14) != 40:
        raise ValueError("original Arm is not an expected 24-bit BMP")
    width, height = struct.unpack_from("<ii", bitmap, 18)
    if (width, height) != (295, 55) or u16(bitmap, 28) != 24 or u32(bitmap, 30) != 0:
        raise ValueError("unexpected original Arm dimensions or compression")
    first = u32(bitmap, 10)
    stride = (width * 3 + 3) & ~3
    rows = []
    for row in range(height - 1, -1, -1):
        rgb = bytearray()
        scanline = bitmap[first + row * stride:first + (row + 1) * stride]
        if len(scanline) != stride:
            raise ValueError("truncated original BMP")
        for x in range(0, width * 3, 3):
            blue, green, red = scanline[x:x + 3]
            rgb.extend((red, green, blue))
        rows.append(b"\x00" + rgb)  # PNG no-filter row, top to bottom
    signature = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return signature + png_chunk(b"IHDR", ihdr) + \
        png_chunk(b"IDAT", zlib.compress(b"".join(rows), 9)) + png_chunk(b"IEND", b"")


def main(args):
    if len(args) != 2:
        raise SystemExit("Pass read-only original MyTime3.dll and a new-app output directory")
    source = pathlib.Path(args[0])
    content = source.read_bytes()
    if hashlib.sha256(content).hexdigest() != EXPECTED_DLL:
        raise ValueError("refusing to extract from an unpinned original")
    images = images_from_original(content)
    if not images["logo1"].startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError("original vendor logo is not PNG")
    target = pathlib.Path(args[1])
    target.mkdir(parents=True, exist_ok=True)
    files = {
        "aria-about-arm.png": bmp24_to_png(images["Arm"]),
        "aria-about-logo.png": images["logo1"],
    }
    for name, data in files.items():
        path = target / name
        path.write_bytes(data)
        print(name, len(data), "SHA-256", hashlib.sha256(data).hexdigest(), "(public original vendor image)")


if __name__ == "__main__":
    main(sys.argv[1:])
