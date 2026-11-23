using Hst.Commander.Core;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal properties dialog (Alt+Enter) showing the name and, in the General tab, the sections of lines of
///     <see cref="EntryProperties" /> separated by lines, the used space of drives as a progress bar and the check boxes
///     of <see cref="EntryProperties.FlagGroups" /> (attributes, permissions and Amiga protection bits), enabled when they can be
///     changed, followed by the tabs of the plugins (<see cref="EntryProperties.Tabs" />, see
///     <see cref="PropertiesTabView" />). The size and contents are counted in the background and shown with
///     <see cref="Update" />; closing the dialog (Save, OK, Cancel or Esc) cancels counting through
///     <see cref="CancellationToken" />. When something can be changed the dialog has Save and Cancel, and Save sets
///     <see cref="CheckedFlags" /> and <see cref="TabTexts" />; nothing is changed before.
/// </summary>
public sealed class PropertiesDialog : TuiDialog
{
    private readonly EntryProperties _properties;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<Label> _valueLabels = [];
    private readonly List<List<CheckBox>> _flagCheckBoxes = [];
    private readonly List<PropertiesTabView> _tabViews = [];
    private bool _closed;

    public PropertiesDialog(EntryProperties properties, TuiPalette palette)
    {
        _properties = properties;
        Title = "Properties";
        SetScheme(palette.DialogColorScheme);

        var sections = properties.GetSections();
        var labelWidth = sections.SelectMany(section => section).Select(line => line.Label)
            .Concat(properties.FlagGroups.Select(group => group.Label))
            .Select(label => label.Length).DefaultIfEmpty(0).Max() + 2;

        // wide enough for the check boxes (glyph, space, label and a space between them), within the borders of the
        // dialog and the tab view
        var checkBoxesWidth = properties.FlagGroups
            .Select(group => group.Flags.Sum(flag => flag.Label.Length + 3) + group.Flags.Count - 1)
            .DefaultIfEmpty(0).Max();
        var minimumWidth = 1 + 1 + labelWidth + checkBoxesWidth + 1 + 1 + 2;
        Width = Dim.Func(_ => Math.Min(Application.Screen.Width,
            Math.Max(Application.Screen.Width * 70 / 100, minimumWidth)));
        Add(new Label { X = 1, Y = 1, Width = Dim.Fill(1), Text = properties.Name });

        var general = new View { Title = "General", Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
        var y = 0;
        foreach (var section in sections)
        {
            // separators between the sections, not above the first one
            if (y > 0)
            {
                general.Add(CreateSeparator(y++));
            }

            foreach (var line in section)
            {
                var valueLabel = new Label { X = labelWidth, Y = y, Width = Dim.Fill(), Text = line.Value };
                _valueLabels.Add(valueLabel);
                general.Add(new Label { X = 0, Y = y++, Text = $"{line.Label}:" }, valueLabel);
            }
        }

        if (properties.FlagGroups.Count > 0)
        {
            if (y > 0)
            {
                general.Add(CreateSeparator(y++));
            }

            foreach (var group in properties.FlagGroups)
            {
                general.Add(new Label { X = 0, Y = y, Text = $"{group.Label}:" });
                var checkBoxes = new List<CheckBox>();
                foreach (var flag in group.Flags)
                {
                    var checkBox = new CheckBox
                    {
                        X = checkBoxes.Count == 0 ? labelWidth : Pos.Right(checkBoxes[^1]) + 1,
                        Y = y,
                        Text = flag.Label,
                        Value = flag.IsSet ? CheckState.Checked : CheckState.UnChecked,
                        Enabled = group.CanChange
                    };
                    checkBoxes.Add(checkBox);
                    general.Add(checkBox);
                }

                _flagCheckBoxes.Add(checkBoxes);
                y++;
            }
        }

        if (properties.UsedFraction is { } usedFraction)
        {
            general.Add(new ProgressBar
            {
                X = 0,
                Y = ++y,
                Width = Dim.Fill(),
                Height = 1,
                Fraction = (float)usedFraction,
                ProgressBarStyle = ProgressBarStyle.Continuous,
                ProgressBarFormat = ProgressBarFormat.SimplePlusPercentage
            });
            y++;
        }

        // the tab view is as high as its highest tab, with the 3 rows of the tabs and the bottom border
        var contentRows = properties.Tabs.Count > 0 ? Math.Max(y, PropertiesTabView.Rows) : y;
        var tabView = new Tabs { X = 1, Y = 2, Width = Dim.Fill(1), Height = contentRows + 4 };
        tabView.Add(general);
        foreach (var tab in properties.Tabs)
        {
            var tabContent = new PropertiesTabView(tab);
            _tabViews.Add(tabContent);
            tabContent.Title = tab.Title;
            tabView.Add(tabContent);
        }

        Add(tabView);

        // the name, the tab view, the borders and the button row
        Height = 2 + contentRows + 4 + 5;

        var okButton = new Button { Text = properties.CanChange ? "Save" : "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            if (properties.CanChange)
            {
                CheckedFlags = _flagCheckBoxes
                    .Select(checkBoxes => (IReadOnlyList<bool>)checkBoxes
                        .Select(checkBox => checkBox.Value == CheckState.Checked)
                        .ToList())
                    .ToList();
                TabTexts = _tabViews.Select(tabContent => tabContent.EditedText).ToList();
            }

            e.Handled = true;
            Application.RequestStop(this);
        };
        IsRunningChanging += (_, e) =>
        {
            if (!e.NewValue)
            {
                Close();
            }
        };

        AddButton(okButton);
        if (properties.CanChange)
        {
            var cancelButton = new Button { Text = "Cancel" };
            cancelButton.Accepting += (_, e) =>
            {
                e.Handled = true;
                Application.RequestStop(this);
            };
            AddButton(cancelButton);
        }

        Initialized += (_, _) => okButton.SetFocus();
    }

    /// <summary>
    ///     Whether each check box of each flag group is checked when Save was pressed, for
    ///     <see cref="NavigationContext.ChangePropertiesAsync" />. Null when not saved.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<bool>>? CheckedFlags { get; private set; }

    /// <summary>
    ///     Text of each of <see cref="EntryProperties.Tabs" /> when Save was pressed, null for a tab without text, for
    ///     <see cref="NavigationContext.ChangePropertiesAsync" />. Null when not saved.
    /// </summary>
    public IReadOnlyList<string?>? TabTexts { get; private set; }

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
            ErrorBox.Show(message);
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

    /// <summary>
    ///     A horizontal line drawn by itself: a <see cref="Line" /> is otherwise drawn by its superview's line canvas,
    ///     which the tab view draws over all tabs, so it would show through the other tabs.
    /// </summary>
    private static Line CreateSeparator(int y) =>
        new() { X = 0, Y = y, Width = Dim.Fill(), SuperViewRendersLineCanvas = false };
}
