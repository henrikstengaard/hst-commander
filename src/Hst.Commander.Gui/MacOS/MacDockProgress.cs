using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hst.Commander.Core;
using static Hst.Commander.Gui.MacOS.ObjC;

namespace Hst.Commander.Gui.MacOS;

/// <summary>
///     Shows the progress of a file operation on the app's Dock tile on macOS by drawing the app icon with a progress
///     bar along its bottom into an image and setting it as the application icon image, which the Dock shows; the
///     icon from before is set again when the progress is cleared. A content view on the Dock tile with an
///     <c>NSProgressIndicator</c> doesn't work: the Dock tile draws its content view into an image, which the progress
///     indicator (drawn by its own layer) doesn't show up in. The app icon is drawn from the app's own icon file, as
///     <c>applicationIconImage</c> is a folder icon when the app isn't run from its bundle (e.g. with
///     <c>dotnet run</c>). The bar is blue while running, yellow when paused and red on errors, and an empty bar when
///     indeterminate. Must be used on the main thread, which is Avalonia's UI thread on macOS.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacDockProgress(byte[] iconData) : ITaskbarProgress
{
    /// <summary>Size the image is drawn in, the largest the Dock shows.</summary>
    private const double Size = 512;

    private const nuint CompositingOperationSourceOver = 2;

    /// <summary>The app icon drawn under the bar, made once.</summary>
    private IntPtr _icon;

    /// <summary>The application icon image from before the progress was shown, null when none is shown.</summary>
    private IntPtr _originalIcon;

    public void Set(TaskbarProgressState state, double fraction)
    {
        try
        {
            var application = Send(Class("NSApplication"), Selector("sharedApplication"));
            if (application == IntPtr.Zero)
            {
                return;
            }

            if (state == TaskbarProgressState.None)
            {
                Restore(application);
                return;
            }

            if (_originalIcon == IntPtr.Zero)
            {
                var originalIcon = Send(application, Selector("applicationIconImage"));
                if (originalIcon == IntPtr.Zero)
                {
                    return;
                }

                _originalIcon = Send(originalIcon, Selector("retain"));
            }

            var image = Draw(state, fraction);
            Send(application, Selector("setApplicationIconImage:"), image);
            Send(image, Selector("release"));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // progress on the Dock tile is a nicety, the operation goes on without it
        }
    }

    /// <summary>Draws the app icon with the progress bar into a new image, released by the caller.</summary>
    private IntPtr Draw(TaskbarProgressState state, double fraction)
    {
        var image = SendWithSize(Send(Class("NSImage"), Selector("alloc")), Selector("initWithSize:"),
            new CGSize(Size, Size));
        Send(image, Selector("lockFocus"));
        try
        {
            var icon = GetIcon();
            SendDrawInRect(icon == IntPtr.Zero ? _originalIcon : icon, Selector("drawInRect:fromRect:operation:fraction:"),
                new CGRect(0, 0, Size, Size), new CGRect(0, 0, 0, 0), CompositingOperationSourceOver, 1);

            // a rounded bar along the bottom, inset like the Dock's own download progress
            const double inset = Size * 0.1;
            const double height = Size * 0.1;
            const double border = height * 0.12;
            var track = new CGRect(inset, Size * 0.07, Size - 2 * inset, height);
            FillRoundedRect(track, Color(0.1, 0.1, 0.1, 0.85));

            if (state != TaskbarProgressState.Indeterminate)
            {
                var width = (track.Width - 2 * border) * Math.Clamp(fraction, 0, 1);
                var (red, green, blue) = state switch
                {
                    TaskbarProgressState.Paused => (1.0, 0.8, 0.0),
                    TaskbarProgressState.Error => (1.0, 0.23, 0.19),
                    _ => (0.0, 0.48, 1.0)
                };
                if (width > 0)
                {
                    FillRoundedRect(new CGRect(track.X + border, track.Y + border,
                            Math.Max(width, track.Height - 2 * border), track.Height - 2 * border),
                        Color(red, green, blue, 1));
                }
            }
        }
        finally
        {
            Send(image, Selector("unlockFocus"));
        }

        return image;
    }

    /// <summary>Returns the app icon read from the app's icon file, null when it can't be read.</summary>
    private IntPtr GetIcon()
    {
        if (_icon != IntPtr.Zero || iconData.Length == 0)
        {
            return _icon;
        }

        var bytes = Marshal.AllocHGlobal(iconData.Length);
        try
        {
            Marshal.Copy(iconData, 0, bytes, iconData.Length);
            var data = Send(Class("NSData"), Selector("dataWithBytes:length:"), bytes, iconData.Length);
            _icon = data == IntPtr.Zero
                ? IntPtr.Zero
                : Send(Send(Class("NSImage"), Selector("alloc")), Selector("initWithData:"), data);
        }
        finally
        {
            Marshal.FreeHGlobal(bytes);
        }

        return _icon;
    }

    private static IntPtr Color(double red, double green, double blue, double alpha) =>
        SendColor(Class("NSColor"), Selector("colorWithSRGBRed:green:blue:alpha:"), red, green, blue, alpha);

    private static void FillRoundedRect(CGRect rect, IntPtr color)
    {
        Send(color, Selector("setFill"));
        var radius = rect.Height / 2;
        Send(SendRoundedRect(Class("NSBezierPath"), Selector("bezierPathWithRoundedRect:xRadius:yRadius:"), rect,
            radius, radius), Selector("fill"));
    }

    private void Restore(IntPtr application)
    {
        if (_originalIcon == IntPtr.Zero)
        {
            return;
        }

        Send(application, Selector("setApplicationIconImage:"), _originalIcon);
        Send(_originalIcon, Selector("release"));
        _originalIcon = IntPtr.Zero;
    }
}
