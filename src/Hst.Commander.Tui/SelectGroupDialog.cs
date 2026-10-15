using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog asking for the filter of the entries to select (mark) with + or unselect with -, titled Select group
///     or Unselect group: wildcards like "*.txt" or a regular expression, case sensitive and files only.
///     <see cref="Result" /> holds the options on OK or null on cancel.
/// </summary>
public sealed class SelectGroupDialog : Dialog
{
    private readonly TextField _filterField;
    private readonly CheckBox _caseSensitiveCheckBox;
    private readonly CheckBox _filesOnlyCheckBox;

    public SelectGroupDialog(SelectGroupOptions options, TuiPalette palette, string title = "Select group")
    {
        Title = title;
        Width = Dim.Percent(50);
        Height = 10;
        ColorScheme = palette.DialogColorScheme;

        var filterLabel = new Label { X = 1, Y = 1, Text = "Filter (wildcards or regular expression):" };
        _filterField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1), Text = options.Filter };
        _caseSensitiveCheckBox = new CheckBox
        {
            X = 1,
            Y = 4,
            Text = "C_ase sensitive",
            CheckedState = options.CaseSensitive ? CheckState.Checked : CheckState.UnChecked
        };
        _filesOnlyCheckBox = new CheckBox
        {
            X = Pos.Right(_caseSensitiveCheckBox) + 3,
            Y = 4,
            Text = "_Files only",
            CheckedState = options.FilesOnly ? CheckState.Checked : CheckState.UnChecked
        };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            Result = new SelectGroupOptions(_filterField.Text,
                _caseSensitiveCheckBox.CheckedState == CheckState.Checked,
                _filesOnlyCheckBox.CheckedState == CheckState.Checked);
            e.Cancel = true;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(filterLabel, _filterField, _caseSensitiveCheckBox, _filesOnlyCheckBox);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _filterField.SetFocus();
    }

    /// <summary>Filter and options when OK was pressed, otherwise null.</summary>
    public SelectGroupOptions? Result { get; private set; }
}
