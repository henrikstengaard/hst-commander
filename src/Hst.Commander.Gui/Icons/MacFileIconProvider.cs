using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hst.Commander.Core;

namespace Hst.Commander.Gui.Icons;

/// <summary>
///     Icons registered in macOS, read with NSWorkspace through the Objective-C runtime and converted to PNG.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacFileIconProvider : IFileIconProvider
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const nuint PngFileType = 4; // NSBitmapImageFileTypePNG

    private static readonly Lazy<bool> AppKitLoaded = new(() =>
        NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _));

    public bool RequiresUiThread => true;

    public IconImage? GetFileTypeIcon(string extension, int size) =>
        GetIcon("iconForFileType:", extension.Length > 1 ? extension[1..] : "public.data", size);

    public IconImage? GetFolderIcon(int size) => GetIcon("iconForFileType:", "public.folder", size);

    public IconImage? GetDriveIcon(string rootPath, int size) => GetIcon("iconForFile:", rootPath, size);

    // application bundles are directories, so only icon files and aliases are shown with their own icon
    public bool HasFileIcon(string name) =>
        Path.GetExtension(name).Equals(".icns", StringComparison.OrdinalIgnoreCase);

    public IconImage? GetFileIcon(string path, int size) => GetIcon("iconForFile:", path, size);

    private static IconImage? GetIcon(string selector, string argument, int size)
    {
        if (!AppKitLoaded.Value)
        {
            return null;
        }

        var pool = objc_autoreleasePoolPush();
        try
        {
            var workspace = Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            var image = Send(workspace, sel_registerName(selector), CreateString(argument));
            if (image == 0)
            {
                return null;
            }

            // the image representation best fitting the size, rendered as a bitmap and encoded as PNG
            var rect = new CGRect { Width = size, Height = size };
            var cgImage = SendRect(image, sel_registerName("CGImageForProposedRect:context:hints:"), ref rect, 0, 0);
            if (cgImage == 0)
            {
                return null;
            }

            var bitmapRep = Send(Send(objc_getClass("NSBitmapImageRep"), sel_registerName("alloc")),
                sel_registerName("initWithCGImage:"), cgImage);
            if (bitmapRep == 0)
            {
                return null;
            }

            try
            {
                var data = SendPng(bitmapRep, sel_registerName("representationUsingType:properties:"), PngFileType, 0);
                if (data == 0)
                {
                    return null;
                }

                var length = (int)SendLength(data, sel_registerName("length"));
                var bytes = new byte[length];
                Marshal.Copy(Send(data, sel_registerName("bytes")), bytes, 0, length);
                return new IconImage(size, size, bytes, null);
            }
            finally
            {
                Send(bitmapRep, sel_registerName("release"));
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private static nint CreateString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [DllImport(ObjectiveCLibrary)]
    private static extern nint objc_getClass(string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint sel_registerName(string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint objc_autoreleasePoolPush();

    [DllImport(ObjectiveCLibrary)]
    private static extern void objc_autoreleasePoolPop(nint pool);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nuint SendLength(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendRect(nint receiver, nint selector, ref CGRect rect, nint context, nint hints);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendPng(nint receiver, nint selector, nuint fileType, nint properties);
}
