using Avalonia.Controls;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal dialog asking for the file name and the input fields of a volume creator chosen in the create dialog (F9),
///     titled e.g. "Create Amiga disk file": a text box for text and number fields, a check box for boolean fields and
///     a drop-down list for choices, required fields marked with "*" and fields enabled by a check box disabled while
///     it's unchecked. The dialog returns the <see cref="VolumeCreatorInput" /> on OK or null on cancel.
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

        // each field gets a text box, check box or drop-down list, its input read on OK
        var inputs = new List<(VolumeCreatorField Field, Func<object?> GetInput)>();
        var checkBoxes = new Dictionary<string, CheckBox>();
        var dependents = new List<(VolumeCreatorField Field, Control[] Controls)>();
        foreach (var field in creator.Fields)
        {
            var hasPrevious = previousInput != null && previousInput.Values.ContainsKey(field.Name);
            var previous = hasPrevious ? previousInput!.Values[field.Name] : field.DefaultValue;
            Control[] controls;
            switch (field.Type)
            {
                case VolumeCreatorFieldType.Boolean:
                {
                    var checkBox = new CheckBox { Content = VolumeCreation.GetLabel(field), IsChecked = previous is true };
                    checkBoxes[field.Name] = checkBox;
                    inputs.Add((field, () => checkBox.IsChecked == true));
                    controls = [checkBox];
                    break;
                }
                case VolumeCreatorFieldType.Choice:
                {
                    var choices = field.Choices ?? [];
                    var comboBox = new ComboBox
                    {
                        ItemsSource = choices.Select(choice => choice.Label).ToList(),
                        SelectedIndex = VolumeCreation.GetChoiceIndex(field, previous),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                    };
                    inputs.Add((field, () => comboBox.SelectedIndex >= 0 && comboBox.SelectedIndex < choices.Count
                        ? choices[comboBox.SelectedIndex].Value
                        : null));
                    controls = [new TextBlock { Text = VolumeCreation.GetLabel(field) + ":" }, comboBox];
                    break;
                }
                default:
                {
                    var textBox = new TextBox
                    {
                        Text = hasPrevious ? previous?.ToString() ?? string.Empty : VolumeCreation.GetDefaultText(field)
                    };
                    inputs.Add((field, () => textBox.Text ?? string.Empty));
                    controls = [new TextBlock { Text = VolumeCreation.GetLabel(field) + ":" }, textBox];
                    break;
                }
            }

            FieldsPanel.Children.AddRange(controls);
            if (field.EnabledByField != null)
            {
                dependents.Add((field, controls));
            }
        }

        // fields enabled by a check box follow it
        void UpdateEnabled()
        {
            foreach (var (field, controls) in dependents)
            {
                var isEnabled = VolumeCreation.IsEnabled(field,
                    name => !checkBoxes.TryGetValue(name, out var checkBox) || checkBox.IsChecked == true);
                foreach (var control in controls)
                {
                    control.IsEnabled = isEnabled;

                    // text blocks don't look disabled by themselves
                    if (control is TextBlock)
                    {
                        control.Opacity = isEnabled ? 1 : 0.5;
                    }
                }
            }
        }

        foreach (var checkBox in checkBoxes.Values)
        {
            checkBox.IsCheckedChanged += (_, _) => UpdateEnabled();
        }

        UpdateEnabled();

        OkButton.Click += (_, _) => Close(new VolumeCreatorInput(FileNameTextBox.Text ?? string.Empty,
            inputs.ToDictionary(input => input.Field.Name, input => input.GetInput())));
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) => FileNameTextBox.Focus();
    }
}
