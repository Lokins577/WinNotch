# Local AI agent activity

The AI agent island surface is local-only. It does not sign in to providers,
call provider APIs, upload logs, or display prompts, tool arguments, responses,
or other conversation content.

WinNotch discovers these standard local stores:

| Provider | Default location |
| --- | --- |
| Codex | `%CODEX_HOME%\sessions`, or `%USERPROFILE%\.codex\sessions` |
| Claude Code | `%CLAUDE_CONFIG_DIR%\projects`, or `%USERPROFILE%\.claude\projects` |
| OpenCode | `%XDG_DATA_HOME%\opencode\log`, or `%USERPROFILE%\.local\share\opencode\log` |
| GitHub Copilot CLI | `%COPILOT_HOME%\session-state`, or `%USERPROFILE%\.copilot\session-state` |

For each provider, at most five recently modified session/log files are
considered during a scan. WinNotch reads the first metadata line and at most
128 KiB from the tail. It extracts only known metadata keys such as session ID,
working directory, model, event type, status, outcome, and phase. Text fields
containing prompts or answers are never copied into the activity model.

The compact island shows only a working provider and project name. The expanded
view adds model and coarse state: Idle, Working, Completed, or Failed. File
freshness is a fallback when a provider does not expose structured lifecycle
events.

Provider storage formats can change. Parsers are deliberately tolerant and
degrade to freshness-only status rather than treating arbitrary text as trusted
instructions or exposing it in the UI.
