# CLAUDE.md

The project specification, architecture, conventions and build commands live in `src/agents.md`:

@src/agents.md

## Notes for Claude

- Both front-ends (`Hst.Commander.Tui` Terminal.Gui app and `Hst.Commander.Gui` Avalonia desktop app) must keep the same functionality. Shared navigation logic belongs in `Hst.Commander.Core`.
- The old ASP.NET Core + React web prototype has been removed; ignore any references to it in history.
- Keep `src/agents.md` up to date when changing architecture, front-end behavior or dependencies.
