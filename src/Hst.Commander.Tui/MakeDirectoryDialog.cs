
namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog asking for a name: of the directory to create (F7), or the new name of an entry to rename
///     (Shift+F6) with its name filled in. <see cref="Result" /> holds the name on OK or null on cancel.
/// </summary>
public sealed class MakeDirectoryDialog : TuiDialog
{
    private readonly TextField _nameField;

    public MakeDirectoryDialog(TuiPalette palette, string title = "Make directory",
        string prompt = "Enter directory name:", string name = "")
    {
        Title = title;
        Width = Dim.Percent(50);
        Height = 8;
        SetScheme(palette.DialogColorScheme);

        var nameLabel = new Label { X = 1, Y = 1, Text = prompt };
        _nameField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1), Text = name };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            Result = _nameField.Text;
            e.Handled = true;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Application.RequestStop(this);
        };

        Add(nameLabel, _nameField);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _nameField.SetFocus();
    }

    /// <summary>Name entered when OK was pressed, otherwise null.</summary>
    public new string? Result { get; private set; }
}
