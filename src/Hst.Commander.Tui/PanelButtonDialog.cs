using System.Collections.ObjectModel;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal dialog changing a button of the button editor (<see cref="ButtonPanelEditorView" />): its label, the
///     command it runs and its colors as "#RRGGBB" (empty for the theme's colors). <see cref="Result" /> holds the
///     changed button on OK, otherwise null.
/// </summary>
public sealed class PanelButtonDialog : TuiDialog
{
    public PanelButtonDialog(PanelButton button, TuiPalette palette)
    {
        ArgumentNullException.ThrowIfNull(button);
        Title = "Button";
        Width = 52;
        Height = 20;
        SetScheme(palette.DialogColorScheme);

        var labelLabel = new Label { X = 1, Y = 1, Text = "Label:" };
        var labelField = new TextField { X = 13, Y = 1, Width = Dim.Fill(1), Text = button.Label };
        var actionLabel = new Label { X = 1, Y = 3, Text = "Command:" };
        var actions = CommanderActions.All;
        // an unfocused list draws its selected item in the Active role; the dialog's colors inverted show the button's
        // command while the label is edited
        var actionList = new ListView
        {
            X = 13,
            Y = 3,
            Width = Dim.Fill(1),
            Height = 8
        };
        var selected = new Attribute(palette.Dialog.Background, palette.Dialog.Foreground);
        actionList.SetScheme(TuiPalette.CreateScheme(palette.Dialog, palette.DialogFocus, active: selected));
        actionList.SetSource(new ObservableCollection<string>(actions.Select(action => action.Description)));
        actionList.SelectedItem = Math.Max(0, actions.ToList().FindIndex(action => action.Id == button.Action));
        var backgroundLabel = new Label { X = 1, Y = 12, Text = "Background:" };
        var backgroundField = new TextField { X = 13, Y = 12, Width = 10, Text = button.Background ?? string.Empty };
        var foregroundLabel = new Label { X = 1, Y = 13, Text = "Text:" };
        var foregroundField = new TextField { X = 13, Y = 13, Width = 10, Text = button.Foreground ?? string.Empty };
        var colorHelp = new Label { X = 25, Y = 12, Width = Dim.Fill(1), Height = 2, Text = "#RRGGBB, empty for\nthe theme's color" };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (!ButtonPanelEditor.TryParseColor(backgroundField.Text, out var background) ||
                !ButtonPanelEditor.TryParseColor(foregroundField.Text, out var foreground))
            {
                ErrorBox.ShowNotice("Enter the colors as #RRGGBB, or leave them empty");
                return;
            }

            var index = Math.Clamp(actionList.SelectedItem ?? 0, 0, actions.Count - 1);
            Result = button with
            {
                Label = labelField.Text.Trim(), Action = actions[index].Id, Background = background,
                Foreground = foreground
            };
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Application.RequestStop(this);
        };

        Add(labelLabel, labelField, actionLabel, actionList, backgroundLabel, backgroundField, foregroundLabel,
            foregroundField, colorHelp);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) => labelField.SetFocus();
    }

    /// <summary>The changed button when OK was pressed, otherwise null.</summary>
    public new PanelButton? Result { get; private set; }
}
