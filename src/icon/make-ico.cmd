:: https://imagemagick.org/script/download.php#windows

set path=%PATH%;c:\Program Files\ImageMagick-7.1.1-Q16-HDRI
:: 16x16 and 32x32 use the simplified, pixel aligned versions
magick hst-commander-16x16.png hst-commander-32x32.png ( hst-commander.png -resize 48x48 ) ( hst-commander.png -resize 64x64 ) ( hst-commander.png -resize 96x96 ) ( hst-commander.png -resize 128x128 ) ( hst-commander.png -resize 256x256 ) -define png:format=png32 -compress zip hst-commander.ico
pause
