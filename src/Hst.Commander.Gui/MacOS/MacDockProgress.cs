using System.Runtime.Versioning;
using Hst.Commander.Core;
using static Hst.Commander.Gui.MacOS.ObjC;

namespace Hst.Commander.Gui.MacOS;

/// <summary>
///     Shows the progress of a file operation on the app's Dock tile on macOS: while progress is shown the tile's
///     content view is the app icon with a progress bar (<c>NSProgressIndicator</c>) along the bottom, and the Dock
///     tile is redrawn on each change; without progress the content view is removed, so the Dock shows the plain icon
///     again. The Dock has no paused or error colors, so those show as a normal bar. Must be used on the main thread,
///     which is Avalonia's UI thread on macOS.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacDockProgress : ITaskbarProgress
{
    private const nint ProgressIndicatorStyleBar = 0;

    private IntPtr _dockTile;
    private IntPtr _contentView;
    private IntPtr _progressIndicator;
    private bool _animating;

    public void Set(TaskbarProgressState state, double fraction)
    {
        try
        {
            if (state == TaskbarProgressState.None)
            {
                Remove();
                return;
            }

            if (!EnsureContentView())
            {
                return;
            }

            var indeterminate = state == TaskbarProgressState.Indeterminate;
            SendBool(_progressIndicator, Selector("setIndeterminate:"), indeterminate);
            if (indeterminate && !_animating)
            {
                Send(_progressIndicator, Selector("startAnimation:"), IntPtr.Zero);
                _animating = true;
            }
            else if (!indeterminate)
            {
                if (_animating)
                {
                    Send(_progressIndicator, Selector("stopAnimation:"), IntPtr.Zero);
                    _animating = false;
                }

                SendDouble(_progressIndicator, Selector("setDoubleValue:"), fraction);
            }

            Send(_dockTile, Selector("display"));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // progress on the Dock tile is a nicety, the operation goes on without it
        }
    }

    /// <summary>Makes the content view with the app icon and the progress bar, returning false when there's no Dock tile.</summary>
    private bool EnsureContentView()
    {
        if (_contentView != IntPtr.Zero)
        {
            return true;
        }

        var application = Send(Class("NSApplication"), Selector("sharedApplication"));
        _dockTile = application == IntPtr.Zero ? IntPtr.Zero : Send(application, Selector("dockTile"));
        if (_dockTile == IntPtr.Zero)
        {
            return false;
        }

        var size = SendSize(_dockTile, Selector("size"));
        var frame = new CGRect(0, 0, size.Width, size.Height);
        var imageView = SendRect(Send(Class("NSImageView"), Selector("alloc")), Selector("initWithFrame:"), frame);
        Send(imageView, Selector("setImage:"), Send(application, Selector("applicationIconImage")));

        // a bar along the bottom of the icon, inset like the Dock's own download progress
        var inset = size.Width * 0.1;
        var barHeight = Math.Max(12, size.Height * 0.16);
        _progressIndicator = SendRect(Send(Class("NSProgressIndicator"), Selector("alloc")),
            Selector("initWithFrame:"), new CGRect(inset, size.Height * 0.06, size.Width - 2 * inset, barHeight));
        SendInteger(_progressIndicator, Selector("setStyle:"), ProgressIndicatorStyleBar);
        SendDouble(_progressIndicator, Selector("setMinValue:"), 0);
        SendDouble(_progressIndicator, Selector("setMaxValue:"), 1);
        Send(imageView, Selector("addSubview:"), _progressIndicator);

        _contentView = imageView;
        Send(_dockTile, Selector("setContentView:"), _contentView);
        return true;
    }

    private void Remove()
    {
        if (_contentView == IntPtr.Zero)
        {
            return;
        }

        if (_animating)
        {
            Send(_progressIndicator, Selector("stopAnimation:"), IntPtr.Zero);
            _animating = false;
        }

        Send(_dockTile, Selector("setContentView:"), IntPtr.Zero);
        Send(_dockTile, Selector("display"));

        // the Dock tile released its reference, these are the ones from alloc
        Send(_progressIndicator, Selector("release"));
        Send(_contentView, Selector("release"));
        _progressIndicator = IntPtr.Zero;
        _contentView = IntPtr.Zero;
    }
}
