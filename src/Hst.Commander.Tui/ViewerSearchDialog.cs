using Hst.Commander.Core;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog asking what to search for in the viewer (F7): the text or regular expression, case sensitive and
///     backwards. <see cref="Result" /> holds the options on OK or null on cancel.
/// </summary>
public sealed class ViewerSearchDialog : TuiDialog
{
    private readonly TextField _textField;
    private readonly CheckBox _regexCheckBox;
    private readonly CheckBox _caseSensitiveCheckBox;
    private readonly CheckBox _backwardsCheckBox;

    public ViewerSearchDialog(ViewerSearchOptions? options, TuiPalette palette)
    {
        Title = "Search";
        Width = Dim.Percent(50);
        Height = 10;
        SetScheme(palette.DialogColorScheme);

        var textLabel = new Label { X = 1, Y = 1, Text = "Search for:" };
        _textField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1), Text = options?.Text ?? string.Empty };
        _regexCheckBox = CreateCheckBox(1, "_Regular expression", options?.IsRegex == true);
        _caseSensitiveCheckBox = CreateCheckBox(Pos.Right(_regexCheckBox) + 3, "C_ase sensitive",
            options?.CaseSensitive == true);
        _backwardsCheckBox = CreateCheckBox(Pos.Right(_caseSensitiveCheckBox) + 3, "_Backwards",
            options?.Backwards == true);

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            Result = new ViewerSearchOptions(_textField.Text,
                _regexCheckBox.Value == CheckState.Checked,
                _caseSensitiveCheckBox.Value == CheckState.Checked,
                _backwardsCheckBox.Value == CheckState.Checked);
            e.Handled = true;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Application.RequestStop(this);
        };

        Add(textLabel, _textField, _regexCheckBox, _caseSensitiveCheckBox, _backwardsCheckBox);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _textField.SetFocus();
    }

    /// <summary>What to search for when OK was pressed, otherwise null.</summary>
    public new ViewerSearchOptions? Result { get; private set; }

    private static CheckBox CreateCheckBox(Pos x, string text, bool isChecked) => new()
    {
        X = x,
        Y = 4,
        Text = text,
        Value = isChecked ? CheckState.Checked : CheckState.UnChecked
    };
}
