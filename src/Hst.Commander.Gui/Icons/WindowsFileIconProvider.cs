using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hst.Commander.Core;

namespace Hst.Commander.Gui.Icons;

/// <summary>
///     Icons registered in the Windows shell, read with SHGetFileInfo.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsFileIconProvider : IFileIconProvider
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;
    private const uint ShgfiSmallIcon = 0x1;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;

    /// <summary>
    ///     Extensions of files the shell shows with an icon of their own. Programs are included for those without an
    ///     icon resource, e.g. system programs with the icon in a resource file in C:\Windows\SystemResources.
    /// </summary>
    private static readonly HashSet<string> FileIconExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".scr", ".lnk", ".url", ".appref-ms", ".cpl", ".msc"
    };

    public bool RequiresUiThread => false;

    public IconImage? GetFileTypeIcon(string extension, int size) =>
        GetIcon($"file{extension}", FileAttributeNormal, ShgfiUseFileAttributes, size);

    public IconImage? GetFolderIcon(int size) =>
        GetIcon("folder", FileAttributeDirectory, ShgfiUseFileAttributes, size);

    public IconImage? GetDriveIcon(string rootPath, int size) => GetIcon(rootPath, 0, 0, size);

    public bool HasFileIcon(string name) => FileIconExtensions.Contains(Path.GetExtension(name));

    public IconImage? GetFileIcon(string path, int size) => GetIcon(path, 0, 0, size);

    private static IconImage? GetIcon(string path, uint attributes, uint flags, int size)
    {
        var fileInfo = new ShFileInfo();
        flags |= ShgfiIcon | (size > 24 ? ShgfiLargeIcon : ShgfiSmallIcon);
        if (SHGetFileInfo(path, attributes, ref fileInfo, (uint)Marshal.SizeOf<ShFileInfo>(), flags) == 0 ||
            fileInfo.hIcon == 0)
        {
            return null;
        }

        try
        {
            return ConvertIcon(fileInfo.hIcon);
        }
        finally
        {
            DestroyIcon(fileInfo.hIcon);
        }
    }

    /// <summary>Converts an icon handle to BGRA pixels, using the mask for icons without alpha channel.</summary>
    private static IconImage? ConvertIcon(nint icon)
    {
        if (!GetIconInfo(icon, out var iconInfo))
        {
            return null;
        }

        try
        {
            // monochrome icons have no color bitmap
            if (iconInfo.hbmColor == 0 || GetObject(iconInfo.hbmColor, Marshal.SizeOf<BitmapData>(), out var bitmap) == 0)
            {
                return null;
            }

            var pixels = GetPixels(iconInfo.hbmColor, bitmap.bmWidth, bitmap.bmHeight);
            if (pixels == null)
            {
                return null;
            }

            var hasAlpha = false;
            for (var index = 3; index < pixels.Length && !hasAlpha; index += 4)
            {
                hasAlpha = pixels[index] != 0;
            }

            if (!hasAlpha)
            {
                // the mask read as 32 bits per pixel is white where transparent
                var mask = GetPixels(iconInfo.hbmMask, bitmap.bmWidth, bitmap.bmHeight);
                for (var index = 0; index < pixels.Length; index += 4)
                {
                    pixels[index + 3] = mask != null && mask[index] != 0 ? (byte)0 : (byte)255;
                }
            }

            return new IconImage(bitmap.bmWidth, bitmap.bmHeight, null, pixels);
        }
        finally
        {
            DeleteObject(iconInfo.hbmColor);
            DeleteObject(iconInfo.hbmMask);
        }
    }

    private static byte[]? GetPixels(nint bitmap, int width, int height)
    {
        var header = new BitmapInfoHeader
        {
            biSize = Marshal.SizeOf<BitmapInfoHeader>(),
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32
        };
        var pixels = new byte[width * height * 4];
        var deviceContext = GetDC(0);
        try
        {
            return GetDIBits(deviceContext, bitmap, 0, (uint)height, pixels, ref header, 0) == height ? pixels : null;
        }
        finally
        {
            ReleaseDC(0, deviceContext);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapData
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi,
        uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(nint hIcon, out IconInfo piconinfo);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDc);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(nint h, int c, out BitmapData pv);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, [Out] byte[] lpvBits,
        ref BitmapInfoHeader lpbmi, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint ho);
}
