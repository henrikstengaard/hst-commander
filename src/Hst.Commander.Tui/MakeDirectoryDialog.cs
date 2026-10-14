using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog asking for the name of the directory to create (F7). <see cref="Result" /> holds the name on OK or
///     null on cancel.
/// </summary>
public sealed class MakeDirectoryDialog : Dialog
{
    private readonly TextField _nameField;

    public MakeDirectoryDialog(TuiPalette palette)
    {
        Title = "Make directory";
        Width = Dim.Percent(50);
        Height = 8;
        ColorScheme = palette.DialogColorScheme;

        var nameLabel = new Label { X = 1, Y = 1, Text = "Enter directory name:" };
        _nameField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1) };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            Result = _nameField.Text;
            e.Cancel = true;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(nameLabel, _nameField);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _nameField.SetFocus();
    }

    /// <summary>Directory name when OK was pressed, otherwise null.</summary>
    public string? Result { get; private set; }
}
