using Hst.Commander.Core;
using Hst.Commander.Core.Volumes;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal properties dialog (Alt+Enter) showing the name and the sections of lines of <see cref="EntryProperties" />
///     separated by lines, and the used space of drives as a progress bar. The size and contents are counted in the
///     background and shown with <see cref="Update" />; closing the dialog (OK or Esc) cancels counting through
///     <see cref="CancellationToken" />.
/// </summary>
public sealed class PropertiesDialog : Dialog
{
    private readonly EntryProperties _properties;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<Label> _valueLabels = [];
    private bool _closed;

    public PropertiesDialog(EntryProperties properties, TuiPalette palette)
    {
        _properties = properties;
        Title = "Properties";
        Width = Dim.Percent(70);
        ColorScheme = palette.DialogColorScheme;

        var sections = properties.GetSections();
        var labelWidth = sections.SelectMany(section => section).Select(line => line.Label.Length).DefaultIfEmpty(0)
            .Max() + 2;
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
            e.Cancel = true;
            Application.RequestStop(this);
        };
        Closing += (_, _) => Close();

        AddButton(okButton);
        Initialized += (_, _) => okButton.SetFocus();
    }

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
