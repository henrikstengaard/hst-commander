"""Makes hst-commander.icns, the macOS app icon, from the icon PNGs, on any OS (needs Pillow).

The entries are those Apple's iconutil writes: 16x16 and 32x32 as RLE compressed RGB with an 8-bit mask (is32/s8mk,
il32/l8mk) from the simplified, pixel aligned versions, and PNGs for the larger sizes and the @2x versions. The PNG
entries for 16x16, 32x32 and 64x64 (icp4, icp5, icp6), which e.g. Pillow writes, aren't used, as Finder draws them as
garbage (the title bar and Get Info icon).

usage: python make-icns.py
"""
import io
import struct

from PIL import Image

SOURCE = "hst-commander.png"
OUTPUT = "hst-commander.icns"


def png(image, size):
    resized = image if image.size == (size, size) else image.resize((size, size), Image.Resampling.LANCZOS)
    data = io.BytesIO()
    resized.save(data, "PNG")
    return data.getvalue()


def pack_bits(channel):
    """RLE of icns RGB channels: 0-127 copies the next n+1 bytes, 128-255 repeats the next byte n-125 times."""
    out = bytearray()
    i = 0
    while i < len(channel):
        run = 1
        while i + run < len(channel) and run < 130 and channel[i + run] == channel[i]:
            run += 1
        if run >= 3:
            out += bytes([run + 125, channel[i]])
            i += run
            continue
        start = i
        while i < len(channel) and i - start < 128:
            if i + 2 < len(channel) and channel[i] == channel[i + 1] == channel[i + 2]:
                break
            i += 1
        out += bytes([i - start - 1]) + channel[start:i]
    return bytes(out)


def rgb_and_mask(image):
    r, g, b, a = image.convert("RGBA").split()
    return b"".join(pack_bits(channel.tobytes()) for channel in (r, g, b)), a.tobytes()


def entry(kind, data):
    return kind.encode("ascii") + struct.pack(">I", len(data) + 8) + data


def main():
    source = Image.open(SOURCE).convert("RGBA")
    small = Image.open("hst-commander-16x16.png").convert("RGBA")
    medium = Image.open("hst-commander-32x32.png").convert("RGBA")
    is32, s8mk = rgb_and_mask(small)
    il32, l8mk = rgb_and_mask(medium)
    entries = [
        entry("is32", is32), entry("s8mk", s8mk),  # 16x16
        entry("il32", il32), entry("l8mk", l8mk),  # 32x32
        entry("ic11", png(medium, 32)),  # 16x16@2x
        entry("ic12", png(source, 64)),  # 32x32@2x
        entry("ic07", png(source, 128)),  # 128x128
        entry("ic13", png(source, 256)),  # 128x128@2x
        entry("ic08", png(source, 256)),  # 256x256
        entry("ic14", png(source, 512)),  # 256x256@2x
        entry("ic09", png(source, 512)),  # 512x512
        entry("ic10", png(source, 1024)),  # 512x512@2x
    ]
    body = b"".join(entries)
    with open(OUTPUT, "wb") as file:
        file.write(b"icns" + struct.pack(">I", len(body) + 8) + body)


if __name__ == "__main__":
    main()
