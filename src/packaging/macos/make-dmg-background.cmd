:: https://imagemagick.org/script/download.php#windows

set path=%PATH%;c:\Program Files\ImageMagick-7.1.1-Q16-HDRI
magick -background none -density 96 dmg-background.svg PNG32:dmg-background.png
magick -background none -density 192 dmg-background.svg PNG32:dmg-background@2x.png
pause
