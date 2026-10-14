# Hst Commander TUI

Hst Commander TUI is a terminal based file manager inspired by Norton and Midnight Commander.

## Usage

On startup left and right navigation panes are set to current directory.

With ALT+F1 a popup appears to select a media for left navigation pane.
With ALT+F2 a popup appears to select a media for right navigation pane.

The media selection popup lists all available medias from build-in volumes and from plugins.
With ENTER the selected media is set to the respective navigation pane.

With ENTER on a program that runs in the terminal, e.g. a console program or batch file, the TUI is hidden while the program runs and shown again when it exits.
With CTRL+O the output screen with the output of programs run is shown. Pressing any key returns to the TUI.
