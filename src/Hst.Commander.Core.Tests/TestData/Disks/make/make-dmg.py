#!/usr/bin/env python3
"""
Writes a disk image as a zlib compressed Apple disk image (UDIF "UDZO", .dmg), as hdiutil does on macOS, for the tests
(no Linux tool writes DMG images). Usage: python3 make-dmg.py <disk image> <dmg>

The DMG is the data fork, a property list with one "blkx" entry describing the whole disk in chunks of 1 MB (zlib
compressed, or "ignore" chunks for sectors that are all zeros), and the 512 byte "koly" trailer. The data fork and the
chunks have CRC32 checksums like hdiutil's. Only the Python standard library is used.
"""

import plistlib
import struct
import sys
import zlib

SECTOR_SIZE = 512
CHUNK_SECTORS = 2048
CHUNK_ZLIB = 0x80000005
CHUNK_IGNORE = 0x00000002
CHUNK_END = 0xFFFFFFFF
CHECKSUM_CRC32 = 2


def checksum(crc):
    """UDIF checksum: type, size in bits and 128 bytes of data, the CRC32 in the first 4."""
    return struct.pack('>II', CHECKSUM_CRC32, 32) + struct.pack('>I', crc) + bytes(124)


def main(source_path, dmg_path):
    with open(source_path, 'rb') as source:
        disk = source.read()
    if len(disk) % SECTOR_SIZE:
        disk += bytes(SECTOR_SIZE - len(disk) % SECTOR_SIZE)
    sector_count = len(disk) // SECTOR_SIZE

    data_fork = bytearray()
    chunks = []
    for first_sector in range(0, sector_count, CHUNK_SECTORS):
        count = min(CHUNK_SECTORS, sector_count - first_sector)
        chunk = disk[first_sector * SECTOR_SIZE:(first_sector + count) * SECTOR_SIZE]
        if chunk.count(0) == len(chunk):
            chunks.append((CHUNK_IGNORE, first_sector, count, len(data_fork), 0))
            continue
        compressed = zlib.compress(chunk, 9)
        chunks.append((CHUNK_ZLIB, first_sector, count, len(data_fork), len(compressed)))
        data_fork += compressed
    chunks.append((CHUNK_END, sector_count, 0, len(data_fork), 0))

    # the "mish" block table of the blkx entry
    disk_crc = zlib.crc32(disk)
    mish = struct.pack('>4sIQQQII24x', b'mish', 1, 0, sector_count, 0, CHUNK_SECTORS + 8, 0)
    mish += checksum(disk_crc)
    mish += struct.pack('>I', len(chunks))
    for chunk_type, first_sector, count, offset, length in chunks:
        mish += struct.pack('>IIQQQQ', chunk_type, 0, first_sector, count, offset, length)

    plist = plistlib.dumps({'resource-fork': {'blkx': [{
        'Attributes': '0x0050',
        'CFName': 'whole disk (Apple_HFS : 0)',
        'Data': mish,
        'ID': '-1',
        'Name': 'whole disk (Apple_HFS : 0)',
    }]}})

    xml_offset = len(data_fork)
    koly = struct.pack('>4sIII', b'koly', 4, 512, 1)
    koly += struct.pack('>QQQQQ', 0, 0, len(data_fork), 0, 0)
    koly += struct.pack('>II16s', 1, 1, bytes(16))
    koly += checksum(zlib.crc32(data_fork))
    koly += struct.pack('>QQ', xml_offset, len(plist))
    koly += bytes(120)
    # the master checksum, of the checksums of the blkx entries
    koly += checksum(zlib.crc32(struct.pack('>I', disk_crc)))
    koly += struct.pack('>IQ', 1, sector_count)
    koly += bytes(12)
    assert len(koly) == 512

    with open(dmg_path, 'wb') as dmg:
        dmg.write(data_fork)
        dmg.write(plist)
        dmg.write(koly)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
