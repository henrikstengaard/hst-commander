using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hst.Commander.Gui.MacOS;

/// <summary>
///     Calls into the Objective-C runtime for the macOS interop (<see cref="MacFilePromiseDrag" />,
///     <see cref="MacDockProgress" />): classes, selectors, strings and <c>objc_msgSend</c> with the argument and return
///     types used. Methods returning a double or a small struct of doubles don't need <c>objc_msgSend_fpret</c> or
///     <c>_stret</c> on arm64 and x64.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class ObjC
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

    public static IntPtr NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(Class("NSString"), Selector("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    public static IntPtr Class(string name) => objc_getClass(name);

    public static IntPtr Selector(string name) => sel_registerName(name);

    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct CGPoint(double X, double Y);

    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct CGSize(double Width, double Height);

    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct CGRect(double X, double Y, double Width, double Height);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_getClass(string name);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_getProtocol(string name);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr sel_registerName(string name);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, IntPtr extraBytes);

    [DllImport(ObjCLibrary)]
    public static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(ObjCLibrary)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, string types);

    [DllImport(ObjCLibrary)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool class_addProtocol(IntPtr cls, IntPtr protocol);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument1, IntPtr argument2);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument1, IntPtr argument2,
        IntPtr argument3);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendError(IntPtr receiver, IntPtr selector, IntPtr domain, nint code,
        IntPtr userInfo);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector, CGPoint point, IntPtr view);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern void SendRect(IntPtr receiver, IntPtr selector, CGRect rect, IntPtr contents);

    /// <summary>Sends a message with a <c>CGSize</c> argument returning an object, e.g. <c>initWithSize:</c>.</summary>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendWithSize(IntPtr receiver, IntPtr selector, CGSize size);

    /// <summary>Sends <c>drawInRect:fromRect:operation:fraction:</c> to an image.</summary>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern void SendDrawInRect(IntPtr receiver, IntPtr selector, CGRect rect, CGRect fromRect,
        nuint operation, double fraction);

    /// <summary>Sends <c>bezierPathWithRoundedRect:xRadius:yRadius:</c> to <c>NSBezierPath</c>.</summary>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendRoundedRect(IntPtr receiver, IntPtr selector, CGRect rect, double xRadius,
        double yRadius);

    /// <summary>Sends a message with four <c>double</c> arguments returning an object, e.g. <c>colorWithSRGBRed:green:blue:alpha:</c>.</summary>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendColor(IntPtr receiver, IntPtr selector, double red, double green, double blue,
        double alpha);
}
