#!/bin/sh
# Makes the file system and disk images in TestData/Disks with the real tools of each file system (not DiscUtils, which
# reads them in the tests), gzipped. Run it in a privileged Ubuntu 24.04 container from the TestData/Disks folder:
#
#   docker run --rm --privileged -v "$PWD":/disks ubuntu:24.04 sh /disks/make/make-images.sh
#
# Every file system holds hello.txt ("Hello from <name>" and a line break) and dir/nested.txt ("Nested file" and a line
# break), except HFS+: the kernel in Docker Desktop can't mount HFS+ and hfsplus' hpmount can't write, so it's empty.
# ext4.img.gz is older and made differently (see DiskTestData.ExtLabel), it's left as it is.
set -e

OUT=/disks
WORK=/work
mkdir -p $WORK

apt-get update -qq >/dev/null
DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends \
  dosfstools exfatprogs ntfs-3g e2fsprogs xfsprogs btrfs-progs hfsprogs udftools squashfs-tools fdisk qemu-utils \
  python3 gzip >/dev/null

populate() {
  echo "Hello from $2" > "$1/hello.txt"
  mkdir -p "$1/dir"
  echo "Nested file" > "$1/dir/nested.txt"
}

# mounts an image, adds the files and unmounts it: populate_image <image> <name> [mount type]
populate_image() {
  mkdir -p /mnt/$2
  if [ -n "$3" ]; then mount -t $3 "$1" /mnt/$2; else mount -o loop "$1" /mnt/$2; fi
  populate /mnt/$2 $2
  if [ "$2" = btrfs ]; then
    # files compressed with each algorithm DiscUtils.Btrfs decompresses (zlib, and lzo and zstd with lzo.net and
    # ZstdSharp), 64 KB of text so they are compressed
    for algorithm in zlib lzo zstd; do
      mkdir /mnt/$2/$algorithm
      btrfs property set /mnt/$2/$algorithm compression $algorithm
      seq -f "Line %g compressed with $algorithm" 1 2500 | head -c 65536 > /mnt/$2/$algorithm/compressed.txt
    done
    sync
  fi
  umount /mnt/$2
}

gz() { gzip -9 -n -c "$1" > "$OUT/$2"; }

# file systems without a partition table
truncate -s 32M $WORK/xfs.img
# xfsprogs refuses file systems below 300 MB unless run by its test suite
TEST_DIR=1 TEST_DEV=1 QA_CHECK_FS=1 mkfs.xfs -q -L XFSTEST $WORK/xfs.img
populate_image $WORK/xfs.img xfs
gz $WORK/xfs.img xfs.img.gz

truncate -s 128M $WORK/btrfs.img
mkfs.btrfs -q -L BTRFSTEST $WORK/btrfs.img
populate_image $WORK/btrfs.img btrfs
gz $WORK/btrfs.img btrfs.img.gz

truncate -s 8M $WORK/hfsplus.img
mkfs.hfsplus -v HFSTEST $WORK/hfsplus.img >/dev/null
gz $WORK/hfsplus.img hfsplus.img.gz

truncate -s 8M $WORK/udf.img
mkudffs --label=UDFTEST --media-type=hd $WORK/udf.img >/dev/null
populate_image $WORK/udf.img udf
gz $WORK/udf.img udf.img.gz

for compression in gzip xz zstd lzo; do
  rm -rf $WORK/squashfs-src && mkdir -p $WORK/squashfs-src
  populate $WORK/squashfs-src squashfs
  rm -f $WORK/squashfs-$compression.img
  mksquashfs $WORK/squashfs-src $WORK/squashfs-$compression.img -comp $compression -all-root -no-progress \
    -mkfs-time 0 -all-time 0 >/dev/null
  gz $WORK/squashfs-$compression.img squashfs-$compression.img.gz
done

# partitions of the disks, 16 MB each except XFS (32 MB) and SquashFS (the image padded to 1 MB)
truncate -s 16M $WORK/fat.img
mkfs.vfat -F 16 -n FATTEST $WORK/fat.img >/dev/null
populate_image $WORK/fat.img fat

truncate -s 16M $WORK/ntfs.img
mkntfs -F -Q -L NTFSTEST $WORK/ntfs.img >/dev/null 2>&1
populate_image $WORK/ntfs.img ntfs ntfs-3g

truncate -s 16M $WORK/exfat.img
mkfs.exfat -L EXFATTEST $WORK/exfat.img >/dev/null
populate_image $WORK/exfat.img exfat

rm -rf $WORK/ext4-src && mkdir -p $WORK/ext4-src
populate $WORK/ext4-src ext4
truncate -s 16M $WORK/ext4.img
mkfs.ext4 -q -L EXT4TEST -E root_owner=0:0 -d $WORK/ext4-src $WORK/ext4.img

cp $WORK/squashfs-gzip.img $WORK/squashfs.img
truncate -s 1M $WORK/squashfs.img

# writes the partition images to a disk at the starts sfdisk gave them: write_partitions <disk> <image>...
write_partitions() {
  disk=$1; shift
  number=1
  for image in "$@"; do
    start=$(sfdisk -d "$disk" | grep "start=" | sed -n "${number}p" | sed 's/.*start= *\([0-9]*\).*/\1/')
    dd if="$image" of="$disk" bs=512 seek="$start" conv=notrunc status=none
    number=$((number + 1))
  done
}

# GPT disk: FAT, NTFS, exFAT, ext4, XFS, HFS+ and SquashFS partitions
rm -f $WORK/gpt.img
truncate -s 136M $WORK/gpt.img
sfdisk -q $WORK/gpt.img <<EOF
label: gpt
unit: sectors
size=16MiB, type=EBD0A0A2-B9E5-4433-87C0-68B6B72699C7, name="FAT"
size=16MiB, type=EBD0A0A2-B9E5-4433-87C0-68B6B72699C7, name="NTFS"
size=16MiB, type=EBD0A0A2-B9E5-4433-87C0-68B6B72699C7, name="exFAT"
size=16MiB, type=0FC63DAF-8483-4772-8E79-3D69D8477DE4, name="ext4"
size=32MiB, type=0FC63DAF-8483-4772-8E79-3D69D8477DE4, name="XFS"
size=8MiB, type=48465300-0000-11AA-AA11-00306543ECAC, name="HFS+"
size=1MiB, type=0FC63DAF-8483-4772-8E79-3D69D8477DE4, name="SquashFS"
EOF
write_partitions $WORK/gpt.img $WORK/fat.img $WORK/ntfs.img $WORK/exfat.img $WORK/ext4.img $WORK/xfs.img \
  $WORK/hfsplus.img $WORK/squashfs.img
gz $WORK/gpt.img disk-gpt.img.gz

# MBR disk: FAT (0x06), NTFS (0x07), exFAT (0x07) and ext4 (0x83) primary partitions
rm -f $WORK/mbr.img
truncate -s 66M $WORK/mbr.img
sfdisk -q $WORK/mbr.img <<EOF
label: dos
unit: sectors
size=16MiB, type=6, bootable
size=16MiB, type=7
size=16MiB, type=7
size=16MiB, type=83
EOF
write_partitions $WORK/mbr.img $WORK/fat.img $WORK/ntfs.img $WORK/exfat.img $WORK/ext4.img
gz $WORK/mbr.img disk-mbr.img.gz

# the GPT disk in virtual disk images: dynamic VHDX, VDI and VHD made by qemu-img, and a zlib compressed DMG (UDZO)
qemu-img convert -q -f raw -O vhdx -o subformat=dynamic $WORK/gpt.img $WORK/gpt.vhdx
gz $WORK/gpt.vhdx disk-gpt.vhdx.gz
qemu-img convert -q -f raw -O vdi -o static=off $WORK/gpt.img $WORK/gpt.vdi
gz $WORK/gpt.vdi disk-gpt.vdi.gz
qemu-img convert -q -f raw -O vpc -o subformat=dynamic,force_size=on $WORK/gpt.img $WORK/gpt.vhd
gz $WORK/gpt.vhd disk-gpt.vhd.gz
python3 -I /disks/make/make-dmg.py $WORK/gpt.img $WORK/gpt.dmg
# checks the DMG with qemu's DMG reader, which reads back the disk it was made from
qemu-img convert -q -f dmg -O raw $WORK/gpt.dmg $WORK/gpt-from-dmg.img
cmp -n $(stat -c %s $WORK/gpt.img) $WORK/gpt.img $WORK/gpt-from-dmg.img
gz $WORK/gpt.dmg disk-gpt.dmg.gz

ls -l $OUT/*.gz
