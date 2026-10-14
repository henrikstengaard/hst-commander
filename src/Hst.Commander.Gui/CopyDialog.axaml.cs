using Avalonia.Controls;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal confirm dialog shown before copying or moving, where the destination and the filter of the files to copy
///     or move can be changed. The dialog returns the destination and filter on OK or null on cancel.
/// </summary>
public partial class CopyDialog : Window
{
    public CopyDialog() : this(string.Empty, string.Empty)
    {
    }

    public CopyDialog(string description, string destination, string title = "Copy")
    {
        InitializeComponent();

        Title = title;
        DescriptionText.Text = $"{description} to:";
        DestinationTextBox.Text = destination;
        FilterTextBox.Text = EntryFilter.All;
        OkButton.Click += (_, _) =>
            Close(new CopyDialogResult(DestinationTextBox.Text ?? string.Empty, FilterTextBox.Text ?? string.Empty));
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) =>
        {
            DestinationTextBox.Focus();
            DestinationTextBox.SelectAll();
        };
    }
}

public sealed record CopyDialogResult(string Destination, string Filter);
