namespace Hst.Commander.Abstractions;

/// <summary>
/// Implemented by a <see cref="ITheme" /> whose buttons the user can change: the settings then have a tab of its own
/// with an editor where the user adds, moves and removes columns and buttons and chooses the command of each button.
/// </summary>
public interface ICustomizableButtonPanel
{
    /// <summary>Name of the settings tab, e.g. "Buttons".</summary>
    string SettingsTitle { get; }

    /// <summary>Buttons until the user changes them, restored with "Reset" in the editor.</summary>
    ButtonPanel DefaultLayout { get; }

    /// <summary>Buttons as the user last saved them, or <see cref="DefaultLayout" />.</summary>
    ButtonPanel Layout { get; }

    /// <summary>
    /// Saves the buttons changed by the user, e.g. in a file in the plugin's directory
    /// (<see cref="IPluginHost.GetPluginDirectory" />), so both the TUI and GUI use them.
    /// </summary>
    void SaveLayout(ButtonPanel layout);
}
