using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog asking for the file name and the input fields of a volume creator chosen in the create dialog (F9),
///     titled e.g. "Create Amiga disk file": a text field for text and number fields, a text field with a drop-down
///     list of units for sizes, a check box for boolean fields and a drop-down list for choices, required fields marked with "*" and fields enabled by a check box disabled while
///     it's unchecked. <see cref="Result" /> holds the input on OK or null on cancel.
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

        // each field gets a text field, check box or drop-down list, its input read on OK
        var inputs = new List<(VolumeCreatorField Field, Func<object?> GetInput)>();
        var checkBoxes = new Dictionary<string, CheckBox>();
        var dependents = new List<(VolumeCreatorField Field, View[] Views)>();
        var y = 4;
        var lastIsCheckBox = false;
        foreach (var field in creator.Fields)
        {
            var hasPrevious = previousInput != null && previousInput.Values.ContainsKey(field.Name);
            var previous = hasPrevious ? previousInput!.Values[field.Name] : field.DefaultValue;
            View[] views;
            switch (field.Type)
            {
                case VolumeCreatorFieldType.Boolean:
                {
                    var checkBox = new CheckBox
                    {
                        X = 1,
                        Y = y,
                        Text = VolumeCreation.GetLabel(field),
                        CheckedState = previous is true ? CheckState.Checked : CheckState.UnChecked
                    };
                    checkBoxes[field.Name] = checkBox;
                    inputs.Add((field, () => checkBox.CheckedState == CheckState.Checked));
                    views = [checkBox];
                    y += 1;
                    lastIsCheckBox = true;
                    break;
                }
                case VolumeCreatorFieldType.Choice:
                {
                    var choices = field.Choices ?? [];
                    var label = new Label { X = 1, Y = y, Text = VolumeCreation.GetLabel(field) + ":" };
                    var choiceField = new ChoiceField(choices.Select(choice => choice.Label).ToList(),
                        VolumeCreation.GetChoiceIndex(field, previous), palette.DialogColorScheme)
                    {
                        X = 1,
                        Y = y + 1,
                        Width = Dim.Fill(1)
                    };
                    inputs.Add((field, () => choiceField.SelectedIndex >= 0 && choiceField.SelectedIndex < choices.Count
                        ? choices[choiceField.SelectedIndex].Value
                        : null));
                    views = [label, choiceField];
                    y += 3;
                    lastIsCheckBox = false;
                    break;
                }
                case VolumeCreatorFieldType.Size:
                {
                    // the number with the unit chosen next to it
                    const int unitWidth = 6;
                    var sizeInput = VolumeCreation.GetSizeInput(field, previous);
                    var units = VolumeCreation.SizeUnits;
                    var label = new Label { X = 1, Y = y, Text = VolumeCreation.GetLabel(field) + ":" };
                    var numberField = new TextField
                    {
                        X = 1,
                        Y = y + 1,
                        Width = Dim.Fill(unitWidth + 2),
                        Text = sizeInput.Number
                    };
                    var unitField = new ChoiceField(units.Select(unit => unit.Label).ToList(),
                        VolumeCreation.GetSizeUnitIndex(sizeInput.Unit), palette.DialogColorScheme)
                    {
                        X = Pos.AnchorEnd(unitWidth + 1),
                        Y = y + 1,
                        Width = unitWidth
                    };
                    inputs.Add((field, () => new VolumeCreatorSizeInput(numberField.Text,
                        units[Math.Clamp(unitField.SelectedIndex, 0, units.Count - 1)].Label)));
                    views = [label, numberField, unitField];
                    y += 3;
                    lastIsCheckBox = false;
                    break;
                }
                default:
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
                    inputs.Add((field, () => textField.Text));
                    views = [label, textField];
                    y += 3;
                    lastIsCheckBox = false;
                    break;
                }
            }

            Add(views);
            if (field.EnabledByField != null)
            {
                dependents.Add((field, views));
            }
        }

        // fields enabled by a check box follow it
        void UpdateEnabled()
        {
            foreach (var (field, views) in dependents)
            {
                var isEnabled = VolumeCreation.IsEnabled(field,
                    name => !checkBoxes.TryGetValue(name, out var checkBox) ||
                            checkBox.CheckedState == CheckState.Checked);
                foreach (var view in views)
                {
                    view.Enabled = isEnabled;
                }
            }
        }

        foreach (var checkBox in checkBoxes.Values)
        {
            checkBox.CheckedStateChanged += (_, _) => UpdateEnabled();
        }

        UpdateEnabled();

        // an empty line below check boxes like below text fields, then the dialog's border and the buttons
        if (lastIsCheckBox)
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
