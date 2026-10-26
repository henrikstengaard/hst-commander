using System.Runtime.InteropServices;
using Hst.Commander.Abstractions;
using SkiaSharp;

namespace Hst.Commander.Core;

/// <summary>
///     Built-in image provider reading the standard image formats with SkiaSharp, detected by their magic signature:
///     PNG, JPEG, GIF, BMP and WebP. Each frame of an animated GIF or WebP is an image (at most <see cref="MaxFrames" />).
///     Images larger than <see cref="MaxPixels" /> are decoded smaller where the format allows it (JPEG, WebP),
///     otherwise not shown.
/// </summary>
public sealed class StandardImageProvider : IImageProvider
{
    public const int MaxFrames = 256;

    /// <summary>Most pixels decoded, 64 megapixels (256 MB of BGRA pixels).</summary>
    public const long MaxPixels = 64L * 1024 * 1024;

    private static readonly SKEncodedImageFormat[] Formats =
    [
        SKEncodedImageFormat.Png, SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Gif, SKEncodedImageFormat.Bmp,
        SKEncodedImageFormat.Webp
    ];

    public string Name => "Images";

    public string Description => "PNG, JPEG, GIF, BMP and WebP images";

    public Task<IReadOnlyList<EntryImage>> ReadImages(string name, Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var codec = SKCodec.Create(stream);
        if (codec == null || !Formats.Contains(codec.EncodedFormat))
        {
            return Task.FromResult<IReadOnlyList<EntryImage>>([]);
        }

        var format = codec.EncodedFormat.ToString().ToUpperInvariant();
        var frameCount = Math.Clamp(codec.FrameCount, 1, MaxFrames);
        var info = codec.Info;
        if ((long)info.Width * info.Height > MaxPixels)
        {
            // only the first frame of a large image, decoded smaller
            var scale = (float)Math.Sqrt((double)MaxPixels / ((long)info.Width * info.Height));
            var size = codec.GetScaledDimensions(scale);
            if ((long)size.Width * size.Height > MaxPixels)
            {
                return Task.FromResult<IReadOnlyList<EntryImage>>([]);
            }

            info = new SKImageInfo(size.Width, size.Height);
            frameCount = 1;
        }

        info = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var frames = new List<byte[]>();
        var images = new List<EntryImage>();
        for (var index = 0; index < frameCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // a frame of an animation can be drawn over a frame before it
            var requiredFrame = frameCount > 1 && index < codec.FrameInfo.Length
                ? codec.FrameInfo[index].RequiredFrame
                : -1;
            var pixels = requiredFrame >= 0 && requiredFrame < frames.Count
                ? (byte[])frames[requiredFrame].Clone()
                : new byte[info.BytesSize];
            var result = Decode(codec, info, pixels, new SKCodecOptions(index, requiredFrame));
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                if (index == 0)
                {
                    return Task.FromResult<IReadOnlyList<EntryImage>>([]);
                }

                break;
            }

            frames.Add(pixels);
            images.Add(new EntryImage(frameCount > 1 ? $"{format} frame {index + 1}" : format,
                new IconImage(info.Width, info.Height, null, pixels)));
        }

        return Task.FromResult<IReadOnlyList<EntryImage>>(images);
    }

    private static SKCodecResult Decode(SKCodec codec, SKImageInfo info, byte[] pixels, SKCodecOptions options)
    {
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return codec.GetPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, options);
        }
        finally
        {
            handle.Free();
        }
    }
}
