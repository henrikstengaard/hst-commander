using System.Runtime.InteropServices;
using Hst.Commander.Abstractions;
using SkiaSharp;

namespace Hst.Commander.Core;

/// <summary>
///     Pixels of images shown in the viewer: PNG data decoded to 32-bit BGRA pixels (not premultiplied, top-down rows)
///     and scaling them into an area, e.g. for the TUI drawing an image with half block characters.
/// </summary>
public static class ImagePixels
{
    /// <summary>
    ///     BGRA pixels of an image, PNG data decoded. Null when the PNG data is invalid or the size doesn't match the
    ///     pixels.
    /// </summary>
    public static byte[]? ToBgra(IconImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.BgraPixels is { } pixels)
        {
            return pixels.Length >= image.Width * image.Height * 4 ? pixels : null;
        }

        if (image.PngData is not { } pngData)
        {
            return null;
        }

        using var bitmap = SKBitmap.Decode(pngData);
        return bitmap == null || bitmap.Width != image.Width || bitmap.Height != image.Height
            ? null
            : ReadPixels(bitmap);
    }

    /// <summary>
    ///     BGRA pixels of an area showing an image scaled by a factor with its upper left corner at a position in the
    ///     area (it may be partly outside, e.g. zoomed in), transparent where the image isn't. Larger by repeating
    ///     pixels (they stay sharp), smaller by averaging the pixels covered (weighted by their alpha, so transparent
    ///     pixels don't darken the colors). Only the pixels of the area are computed.
    /// </summary>
    public static byte[] Render(byte[] pixels, int width, int height, double scale, int left, int top,
        int targetWidth, int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        var target = new byte[targetWidth * targetHeight * 4];
        if (scale <= 0)
        {
            return target;
        }

        for (var y = 0; y < targetHeight; y++)
        {
            var (sourceTop, sourceBottom) = GetSourceRange(y - top, scale, height);
            if (sourceTop >= sourceBottom)
            {
                continue;
            }

            for (var x = 0; x < targetWidth; x++)
            {
                var (sourceLeft, sourceRight) = GetSourceRange(x - left, scale, width);
                if (sourceLeft >= sourceRight)
                {
                    continue;
                }

                long b = 0, g = 0, r = 0, a = 0, count = 0;
                for (var sy = sourceTop; sy < sourceBottom; sy++)
                {
                    for (var sx = sourceLeft; sx < sourceRight; sx++)
                    {
                        var index = (sy * width + sx) * 4;
                        var alpha = pixels[index + 3];
                        b += pixels[index] * alpha;
                        g += pixels[index + 1] * alpha;
                        r += pixels[index + 2] * alpha;
                        a += alpha;
                        count++;
                    }
                }

                var targetIndex = (y * targetWidth + x) * 4;
                if (a > 0)
                {
                    target[targetIndex] = (byte)(b / a);
                    target[targetIndex + 1] = (byte)(g / a);
                    target[targetIndex + 2] = (byte)(r / a);
                }

                target[targetIndex + 3] = (byte)(a / count);
            }
        }

        return target;
    }

    /// <summary>Source pixels covered by a target pixel (relative to the image), empty outside the image.</summary>
    private static (int Start, int End) GetSourceRange(int position, double scale, int length)
    {
        // a small margin, so e.g. 3 / (1 / 3.0) isn't rounded down to 2
        var start = (int)Math.Floor(position / scale + 1e-9);
        var end = Math.Max(start + 1, (int)Math.Floor((position + 1) / scale + 1e-9));
        return (Math.Max(0, start), Math.Min(length, end));
    }

    /// <summary>
    ///     Factor an image is scaled by to fit an area keeping its aspect ratio: smaller images are enlarged by a whole
    ///     factor (at most <paramref name="maxScale" />, so pixels stay sharp), larger images are made smaller to fit.
    ///     0 when the image or area is empty.
    /// </summary>
    public static double GetFitScale(double width, double height, double areaWidth, double areaHeight,
        int maxScale = 8)
    {
        if (width <= 0 || height <= 0 || areaWidth <= 0 || areaHeight <= 0)
        {
            return 0;
        }

        var scale = Math.Min(areaWidth / width, areaHeight / height);
        return scale >= 1 ? Math.Clamp(Math.Floor(scale), 1, maxScale) : scale;
    }

    /// <summary>Size an image is shown in to fit an area keeping its aspect ratio, see <see cref="GetFitScale" />.</summary>
    public static (int Width, int Height) GetFitSize(int width, int height, int areaWidth, int areaHeight,
        int maxScale = 8)
    {
        var scale = GetFitScale(width, height, areaWidth, areaHeight, maxScale);
        return scale <= 0 ? (0, 0) : (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }

    /// <summary>Text of an image's size and color depth, e.g. "46 × 46, 8-bit".</summary>
    public static string FormatSize(IconImage image) => image.BitsPerPixel is { } bits
        ? $"{image.Width} × {image.Height}, {bits}-bit"
        : $"{image.Width} × {image.Height}";

    internal static byte[] ReadPixels(SKBitmap bitmap)
    {
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var pixels = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using var pixmap = bitmap.PeekPixels();
            pixmap.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0);
        }
        finally
        {
            handle.Free();
        }

        return pixels;
    }
}
