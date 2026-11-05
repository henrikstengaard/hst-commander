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

    /// <summary>Sends a message with an <c>NSInteger</c> or <c>NSUInteger</c> argument, e.g. <c>setStyle:</c>.</summary>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern void SendInteger(IntPtr receiver, IntPtr selector, nint argument);

    /// <summary>Sends a message with a <c>double</c> argument, e.g. <c>setDoubleValue:</c>.</summary>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern void SendDouble(IntPtr receiver, IntPtr selector, double argument);

    /// <summary>Sends a message with a <c>BOOL</c> argument, e.g. <c>setIndeterminate:</c>.</summary>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern void SendBool(IntPtr receiver, IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool argument);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendError(IntPtr receiver, IntPtr selector, IntPtr domain, nint code,
        IntPtr userInfo);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector, CGPoint point, IntPtr view);

    /// <summary>Sends a message returning a <c>CGSize</c>, e.g. <c>size</c> of a Dock tile.</summary>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern CGSize SendSize(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern void SendRect(IntPtr receiver, IntPtr selector, CGRect rect, IntPtr contents);

    /// <summary>Sends a message with a <c>CGRect</c> argument returning an object, e.g. <c>initWithFrame:</c>.</summary>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendRect(IntPtr receiver, IntPtr selector, CGRect rect);
}
