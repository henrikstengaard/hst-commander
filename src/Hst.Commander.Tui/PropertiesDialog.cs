using Hst.Commander.Core;
using Hst.Commander.Core.Volumes;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal properties dialog (Alt+Enter) showing the name and the sections of lines of <see cref="EntryProperties" />
///     separated by lines, the used space of drives as a progress bar and the check boxes of
///     <see cref="EntryProperties.FlagGroups" /> (attributes and Amiga protection bits), enabled when they can be
///     changed. The size and contents are counted in the background and shown with <see cref="Update" />; closing the
///     dialog (OK, Cancel or Esc) cancels counting through <see cref="CancellationToken" />. OK sets
///     <see cref="CheckedFlags" /> when check boxes can be changed.
/// </summary>
public sealed class PropertiesDialog : Dialog
{
    private readonly EntryProperties _properties;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<Label> _valueLabels = [];
    private readonly List<List<CheckBox>> _flagCheckBoxes = [];
    private bool _closed;

    public PropertiesDialog(EntryProperties properties, TuiPalette palette)
    {
        _properties = properties;
        Title = "Properties";
        ColorScheme = palette.DialogColorScheme;

        var sections = properties.GetSections();
        var labelWidth = sections.SelectMany(section => section).Select(line => line.Label)
            .Concat(properties.FlagGroups.Select(group => group.Label))
            .Select(label => label.Length).DefaultIfEmpty(0).Max() + 2;

        // wide enough for the check boxes (glyph, space, label and a space between them), within the borders
        var checkBoxesWidth = properties.FlagGroups
            .Select(group => group.Flags.Sum(flag => flag.Label.Length + 3) + group.Flags.Count - 1)
            .DefaultIfEmpty(0).Max();
        var minimumWidth = 1 + labelWidth + checkBoxesWidth + 1 + 2;
        Width = Dim.Func(() => Math.Min(Application.Screen.Width,
            Math.Max(Application.Screen.Width * 70 / 100, minimumWidth)));
        var y = 1;
        Add(new Label { X = 1, Y = y++, Width = Dim.Fill(1), Text = properties.Name });
        foreach (var section in sections)
        {
            Add(new Line { X = 1, Y = y++, Width = Dim.Fill(1) });
            foreach (var line in section)
            {
                var valueLabel = new Label { X = 1 + labelWidth, Y = y, Width = Dim.Fill(1), Text = line.Value };
                _valueLabels.Add(valueLabel);
                Add(new Label { X = 1, Y = y++, Text = $"{line.Label}:" }, valueLabel);
            }
        }

        if (properties.FlagGroups.Count > 0)
        {
            Add(new Line { X = 1, Y = y++, Width = Dim.Fill(1) });
            foreach (var group in properties.FlagGroups)
            {
                Add(new Label { X = 1, Y = y, Text = $"{group.Label}:" });
                var checkBoxes = new List<CheckBox>();
                foreach (var flag in group.Flags)
                {
                    var checkBox = new CheckBox
                    {
                        X = checkBoxes.Count == 0 ? 1 + labelWidth : Pos.Right(checkBoxes[^1]) + 1,
                        Y = y,
                        Text = flag.Label,
                        CheckedState = flag.IsSet ? CheckState.Checked : CheckState.UnChecked,
                        Enabled = group.CanChange
                    };
                    checkBoxes.Add(checkBox);
                    Add(checkBox);
                }

                _flagCheckBoxes.Add(checkBoxes);
                y++;
            }
        }

        if (properties.UsedFraction is { } usedFraction)
        {
            Add(new ProgressBar
            {
                X = 1,
                Y = ++y,
                Width = Dim.Fill(1),
                Height = 1,
                Fraction = (float)usedFraction,
                ProgressBarStyle = ProgressBarStyle.Continuous,
                ProgressBarFormat = ProgressBarFormat.SimplePlusPercentage
            });
            y++;
        }

        // borders and the button row below the lines
        Height = y + 5;

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            if (properties.CanChangeFlags)
            {
                CheckedFlags = _flagCheckBoxes
                    .Select(checkBoxes => (IReadOnlyList<bool>)checkBoxes
                        .Select(checkBox => checkBox.CheckedState == CheckState.Checked)
                        .ToList())
                    .ToList();
            }

            e.Cancel = true;
            Application.RequestStop(this);
        };
        Closing += (_, _) => Close();

        AddButton(okButton);
        if (properties.CanChangeFlags)
        {
            var cancelButton = new Button { Text = "Cancel" };
            cancelButton.Accepting += (_, e) =>
            {
                e.Cancel = true;
                Application.RequestStop(this);
            };
            AddButton(cancelButton);
        }

        Initialized += (_, _) => okButton.SetFocus();
    }

    /// <summary>
    ///     Whether each check box of each flag group is checked when OK was pressed and check boxes can be changed,
    ///     for <see cref="NavigationContext.ChangeFlagsAsync" />. Null otherwise.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<bool>>? CheckedFlags { get; private set; }

    /// <summary>Cancelled when the dialog closes, stopping counting the size and contents.</summary>
    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    /// <summary>Shows the size and contents counted so far. Must be called on the UI thread.</summary>
    public void Update(EntryContents contents)
    {
        if (_closed)
        {
            return;
        }

        var lines = _properties.GetSections(contents).SelectMany(section => section).ToList();
        for (var index = 0; index < lines.Count && index < _valueLabels.Count; index++)
        {
            _valueLabels[index].Text = lines[index].Value;
        }
    }

    /// <summary>Shows why counting the size and contents failed. Must be called on the UI thread.</summary>
    public void ShowError(string message)
    {
        if (!_closed)
        {
            MessageBox.ErrorQuery("Error", message, "OK");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
            _cancellationTokenSource.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _cancellationTokenSource.Cancel();
    }
}
