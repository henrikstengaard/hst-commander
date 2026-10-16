:: https://imagemagick.org/script/download.php#windows

set path=%PATH%;c:\Program Files\ImageMagick-7.1.1-Q16-HDRI
magick -background none -density 384 hst-commander.svg -trim +repage -resize 1024x1024 -gravity center -extent 1024x1024 PNG32:hst-commander.png
magick hst-commander.png -resize 512x512 PNG32:hst-commander-512x512.png
magick hst-commander.png -resize 192x192 PNG32:hst-commander-192x192.png
:: simplified, pixel aligned versions for small sizes
magick -background none -density 96 +antialias hst-commander-16x16.svg PNG32:hst-commander-16x16.png
magick -background none -density 96 +antialias hst-commander-32x32.svg PNG32:hst-commander-32x32.png
pause
