using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog shown while a file inside an image or another volume is edited in an external editor as a temporary
///     copy. It closes by itself when the editor exits, unless the editor exited at once (see
///     <see cref="ExternalEditor.HandOffTime" />, e.g. a running instance opened the file), then Done closes it. The dialog
///     returns true when the changes are to be written back, false on discard.
/// </summary>
public partial class ExternalEditDialog : Window
{
    private readonly CancellationTokenSource _cancellation = new();

    public ExternalEditDialog() : this(string.Empty, string.Empty, null)
    {
    }

    public ExternalEditDialog(string name, string editorName, Process? process)
    {
        InitializeComponent();
        var started = DateTime.Now;
        MessageText.Text =
            $"'{name}' is edited in {editorName}. Save it in the editor, then close the editor or click Done to write the changes back.";
        DoneButton.Click += (_, _) => Close(true);
        DiscardButton.Click += (_, _) => Close(false);
        Closed += (_, _) => _cancellation.Cancel();
        Opened += async (_, _) =>
        {
            DoneButton.Focus();
            if (process == null)
            {
                return;
            }

            try
            {
                await process.WaitForExitAsync(_cancellation.Token);
                if (DateTime.Now - started < ExternalEditor.HandOffTime)
                {
                    MessageText.Text =
                        $"'{name}' is edited in {editorName}, which is still open. Save it in the editor, then click Done to write the changes back.";
                    return;
                }

                Dispatcher.UIThread.Post(() => Close(true));
            }
            catch (OperationCanceledException)
            {
            }
        };
    }
}
