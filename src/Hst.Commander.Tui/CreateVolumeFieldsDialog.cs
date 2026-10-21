using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog asking for the file name and the input fields of a volume creator chosen in the create dialog (F9),
///     titled e.g. "Create Amiga disk file": a text field for text and number fields and a check box for boolean fields,
///     required fields marked with "*". <see cref="Result" /> holds the input on OK or null on cancel.
/// </summary>
public sealed class CreateVolumeFieldsDialog : Dialog
{
    private readonly TextField _fileNameField;

    public CreateVolumeFieldsDialog(IVolumeCreator creator, VolumeCreatorInput? previousInput, TuiPalette palette)
    {
        Title = $"Create {creator.Name}";
        Width = Dim.Percent(50);
        ColorScheme = palette.DialogColorScheme;

        var fileNameLabel = new Label { X = 1, Y = 1, Text = $"File name ({creator.FileExtension}) *:" };
        _fileNameField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1), Text = previousInput?.FileName ?? string.Empty };
        Add(fileNameLabel, _fileNameField);

        // each field gets a text field or check box, its input read on OK
        var inputs = new List<(VolumeCreatorField Field, Func<object?> GetInput)>();
        var y = 4;
        foreach (var field in creator.Fields)
        {
            var hasPrevious = previousInput != null && previousInput.Values.ContainsKey(field.Name);
            var previous = hasPrevious ? previousInput!.Values[field.Name] : field.DefaultValue;
            if (field.Type == VolumeCreatorFieldType.Boolean)
            {
                var checkBox = new CheckBox
                {
                    X = 1,
                    Y = y,
                    Text = VolumeCreation.GetLabel(field),
                    CheckedState = previous is true ? CheckState.Checked : CheckState.UnChecked
                };
                Add(checkBox);
                inputs.Add((field, () => checkBox.CheckedState == CheckState.Checked));
                y += 1;
            }
            else
            {
                var label = new Label { X = 1, Y = y, Text = VolumeCreation.GetLabel(field) + ":" };
                var textField = new TextField
                {
                    X = 1,
                    Y = y + 1,
                    Width = Dim.Fill(1),
                    Text = hasPrevious
                        ? previous?.ToString() ?? string.Empty
                        : VolumeCreation.GetDefaultText(field)
                };
                Add(label, textField);
                inputs.Add((field, () => textField.Text));
                y += 3;
            }
        }

        // an empty line below check boxes like below text fields, then the dialog's border and the buttons
        if (creator.Fields.Count > 0 && creator.Fields[^1].Type == VolumeCreatorFieldType.Boolean)
        {
            y++;
        }

        Height = y + 4;

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            Result = new VolumeCreatorInput(_fileNameField.Text,
                inputs.ToDictionary(input => input.Field.Name, input => input.GetInput()));
            e.Cancel = true;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => _fileNameField.SetFocus();
    }

    /// <summary>File name and input of the fields when OK was pressed, otherwise null.</summary>
    public VolumeCreatorInput? Result { get; private set; }
}
