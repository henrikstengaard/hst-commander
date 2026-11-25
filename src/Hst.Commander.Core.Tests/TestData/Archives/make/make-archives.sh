#!/bin/sh
# Makes the tar, gzip, xz, bzip2 and zstd files in TestData/Archives with GNU tar, gzip, xz, bzip2 and zstd. Run it in
# an Ubuntu 24.04 container from the TestData/Archives folder:
#
#   docker run --rm -v "$PWD":/archives ubuntu:24.04 sh /archives/make/make-archives.sh
#
# The tar archives hold the dirs-files tree of the other archives (see ArchiveTestData.DirsFiles): the directories
# dir1/dir3/ and dir2/ and the file dir1/file1.txt ("a"), without a dir1/ entry. Every file and entry is dated
# 2024-05-06 07:08:10 UTC.
set -e

OUT=/archives
WORK=/work
DATE="2024-05-06 07:08:10 UTC"

apt-get update -qq >/dev/null
DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends xz-utils bzip2 zstd >/dev/null

rm -rf $WORK && mkdir -p $WORK/dirs-files/dir1/dir3 $WORK/dirs-files/dir2
printf a > $WORK/dirs-files/dir1/file1.txt
find $WORK/dirs-files -exec touch -d "$DATE" {} +
cd $WORK/dirs-files

TAR="tar --no-recursion --owner=0 --group=0 --numeric-owner --mtime=@1714979290 --sort=name"
# GNU, POSIX (pax) and old v7 tar (no "ustar" magic, detected by the header checksum)
$TAR --format=gnu -cf $OUT/dirs-files.tar dir1/dir3/ dir2/ dir1/file1.txt
$TAR --format=posix --pax-option=delete=atime,delete=ctime -cf $OUT/dirs-files-posix.tar dir1/dir3/ dir2/ dir1/file1.txt
$TAR --format=v7 -cf $OUT/dirs-files-v7.tar dir1/dir3/ dir2/ dir1/file1.txt

# compressed tar archives
gzip -9 -n -c $OUT/dirs-files.tar > $OUT/dirs-files.tar.gz
cp $OUT/dirs-files.tar.gz $OUT/dirs-files.tgz
xz -9 -c $OUT/dirs-files.tar > $OUT/dirs-files.tar.xz
bzip2 -9 -c $OUT/dirs-files.tar > $OUT/dirs-files.tar.bz2
zstd -q -19 -c $OUT/dirs-files.tar > $OUT/dirs-files.tar.zst

# a tar archive with a symbolic link and a hard link to dir1/file1.txt
ln -s ../dir1/file1.txt dir2/symlink.txt
ln dir1/file1.txt dir2/hardlink.txt
$TAR --format=gnu -cf $OUT/links.tar dir1/file1.txt dir2/symlink.txt dir2/hardlink.txt
rm dir2/symlink.txt dir2/hardlink.txt

# compressed files: hello.txt.gz stores its name ("hello.txt") and date, noname.txt.gz neither (gzip -n), the xz files
# never do, nor bzip2 and zstd; hello.txt.zst stores its size, nosize.txt.zst doesn't (compressed from a pipe); the
# multi.txt files are two compressed files joined ("Hello\n" and "World\n")
cd $WORK
printf 'Hello\n' > hello.txt
printf 'World\n' > world.txt
touch -d "$DATE" hello.txt world.txt
gzip -9 -c hello.txt > $OUT/hello.txt.gz
gzip -9 -n -c hello.txt > $OUT/noname.txt.gz
xz -9 -c hello.txt > $OUT/hello.txt.xz
gzip -9 -n -c hello.txt > multi1.gz && gzip -9 -n -c world.txt > multi2.gz && cat multi1.gz multi2.gz > $OUT/multi.txt.gz
xz -9 -c hello.txt > multi1.xz && xz -9 -c world.txt > multi2.xz && cat multi1.xz multi2.xz > $OUT/multi.txt.xz
bzip2 -9 -c hello.txt > $OUT/hello.txt.bz2
bzip2 -9 -c hello.txt > multi1.bz2 && bzip2 -9 -c world.txt > multi2.bz2 && cat multi1.bz2 multi2.bz2 > $OUT/multi.txt.bz2
zstd -q -19 -c hello.txt > $OUT/hello.txt.zst
cat hello.txt | zstd -q -19 -c > $OUT/nosize.txt.zst
zstd -q -19 -c hello.txt > multi1.zst && zstd -q -19 -c world.txt > multi2.zst && cat multi1.zst multi2.zst > $OUT/multi.txt.zst

ls -l $OUT/*.tar $OUT/*.gz $OUT/*.tgz $OUT/*.xz $OUT/*.bz2 $OUT/*.zst
