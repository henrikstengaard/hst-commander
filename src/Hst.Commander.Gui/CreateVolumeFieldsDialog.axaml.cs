using Avalonia.Controls;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking for the file name and the input fields of a volume creator chosen in the create dialog (F9),
///     titled e.g. "Create Amiga disk file": a text box for text and number fields and a check box for boolean fields,
///     required fields marked with "*". The dialog returns the <see cref="VolumeCreatorInput" /> on OK or null on cancel.
/// </summary>
public partial class CreateVolumeFieldsDialog : Window
{
    public CreateVolumeFieldsDialog()
    {
        InitializeComponent();
    }

    /// <param name="creator">Volume creator to enter the fields of.</param>
    /// <param name="previousInput">Input shown instead of the defaults, e.g. after creating failed.</param>
    public CreateVolumeFieldsDialog(IVolumeCreator creator, VolumeCreatorInput? previousInput)
    {
        InitializeComponent();
        Title = $"Create {creator.Name}";
        FileNameLabel.Text = $"File name ({creator.FileExtension}) *:";
        FileNameTextBox.Text = previousInput?.FileName ?? string.Empty;

        // each field gets a text box or check box, its input read on OK
        var inputs = new List<(VolumeCreatorField Field, Func<object?> GetInput)>();
        foreach (var field in creator.Fields)
        {
            var hasPrevious = previousInput != null && previousInput.Values.ContainsKey(field.Name);
            var previous = hasPrevious ? previousInput!.Values[field.Name] : field.DefaultValue;
            if (field.Type == VolumeCreatorFieldType.Boolean)
            {
                var checkBox = new CheckBox { Content = VolumeCreation.GetLabel(field), IsChecked = previous is true };
                FieldsPanel.Children.Add(checkBox);
                inputs.Add((field, () => checkBox.IsChecked == true));
            }
            else
            {
                var textBox = new TextBox
                {
                    Text = hasPrevious ? previous?.ToString() ?? string.Empty : VolumeCreation.GetDefaultText(field)
                };
                FieldsPanel.Children.Add(new TextBlock { Text = VolumeCreation.GetLabel(field) + ":" });
                FieldsPanel.Children.Add(textBox);
                inputs.Add((field, () => textBox.Text ?? string.Empty));
            }
        }

        OkButton.Click += (_, _) => Close(new VolumeCreatorInput(FileNameTextBox.Text ?? string.Empty,
            inputs.ToDictionary(input => input.Field.Name, input => input.GetInput())));
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) => FileNameTextBox.Focus();
    }
}
