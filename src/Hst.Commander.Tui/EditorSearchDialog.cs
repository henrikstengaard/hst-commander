using Hst.Commander.Core;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog asking what to search for in the editor (F7) or what to replace (F4): the text or regular
///     expression, the replacement, case sensitive, whole words and backwards. Replacing has Replace and All buttons
///     (<see cref="ReplaceAll" />). <see cref="Result" /> holds the options on OK or null on cancel.
/// </summary>
public sealed class EditorSearchDialog : TuiDialog
{
    private readonly TextField _textField;
    private readonly TextField? _replacementField;
    private readonly CheckBox _regexCheckBox;
    private readonly CheckBox _caseSensitiveCheckBox;
    private readonly CheckBox _wholeWordsCheckBox;
    private readonly CheckBox _backwardsCheckBox;

    public EditorSearchDialog(EditorSearchOptions? options, bool replace, TuiPalette palette)
    {
        Title = replace ? "Replace" : "Search";
        Width = Dim.Percent(60);
        Height = replace ? 13 : 11;
        SetScheme(palette.DialogColorScheme);

        var textLabel = new Label { X = 1, Y = 1, Text = "Search for:" };
        _textField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1), Text = options?.Text ?? string.Empty };
        Add(textLabel, _textField);
        var y = 4;
        if (replace)
        {
            var replacementLabel = new Label { X = 1, Y = 3, Text = "Replace with:" };
            _replacementField = new TextField
            {
                X = 1, Y = 4, Width = Dim.Fill(1), Text = options?.Replacement ?? string.Empty
            };
            Add(replacementLabel, _replacementField);
            y = 6;
        }

        _regexCheckBox = CreateCheckBox(1, y, "_Regular expression", options?.IsRegex == true);
        _caseSensitiveCheckBox = CreateCheckBox(Pos.Right(_regexCheckBox) + 3, y, "C_ase sensitive",
            options?.CaseSensitive == true);
        _wholeWordsCheckBox = CreateCheckBox(1, y + 1, "_Whole words", options?.WholeWords == true);
        _backwardsCheckBox = CreateCheckBox(Pos.Left(_caseSensitiveCheckBox), y + 1, "_Backwards",
            options?.Backwards == true);
        Add(_regexCheckBox, _caseSensitiveCheckBox, _wholeWordsCheckBox, _backwardsCheckBox);

        var okButton = new Button { Text = replace ? "Replace" : "OK", IsDefault = true };
        okButton.Accepting += (_, e) => Accept(e, false);
        AddButton(okButton);
        if (replace)
        {
            var allButton = new Button { Text = "A_ll" };
            allButton.Accepting += (_, e) => Accept(e, true);
            AddButton(allButton);
        }

        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Application.RequestStop(this);
        };
        AddButton(cancelButton);
        Initialized += (_, _) => _textField.SetFocus();
    }

    /// <summary>What to search for or replace when OK, Replace or All was pressed, otherwise null.</summary>
    public new EditorSearchOptions? Result { get; private set; }

    /// <summary>True when All was pressed, replacing every match.</summary>
    public bool ReplaceAll { get; private set; }

    private void Accept(CommandEventArgs e, bool replaceAll)
    {
        Result = new EditorSearchOptions(_textField.Text, _replacementField?.Text,
            _regexCheckBox.Value == CheckState.Checked,
            _caseSensitiveCheckBox.Value == CheckState.Checked,
            _wholeWordsCheckBox.Value == CheckState.Checked,
            _backwardsCheckBox.Value == CheckState.Checked);
        ReplaceAll = replaceAll;
        e.Handled = true;
        Application.RequestStop(this);
    }

    private static CheckBox CreateCheckBox(Pos x, int y, string text, bool isChecked) => new()
    {
        X = x,
        Y = y,
        Text = text,
        Value = isChecked ? CheckState.Checked : CheckState.UnChecked
    };
}
