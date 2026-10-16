# Command Bar

Press `Ctrl+Alt+Space` to open Command Bar. The hotkey uses `MOD_NOREPEAT` and
is registered only while the feature is enabled.

Current providers:

- Start Menu applications;
- visible top-level windows;
- clipboard history when that feature is enabled;
- file names from explicitly selected folders (standard user folders are used until customized);
- arithmetic and scientific functions using a dedicated parser;
- length, mass, duration, data, and temperature conversion; and
- keyword-searchable Emoji.
- user-managed text snippets; and
- explicitly saved executable/script paths, shown only for queries beginning with `>`.

The calculator does not evaluate C#, PowerShell, JavaScript, or shell text. Its
grammar is limited to numeric literals, parentheses, arithmetic operators,
`pi`, `e`, and an allowlist of mathematical functions. Provider failures are
isolated so one broken index does not close the bar or hide other results.

Application results come from the current user's and all-users Start Menu.
Window results enumerate visible top-level windows and exclude WinNotch itself.
Clipboard results use the existing privacy-filtered history; enabling Command
Bar does not implicitly enable clipboard capture.

File indexing is local, bounded to 25,000 entries, refreshed at most every ten
minutes, and skips hidden/system/reparse-point directories plus common generated
trees such as `.git`, `node_modules`, `bin`, and `obj`. The settings window lets
the user replace the default Desktop/Documents/Downloads roots.

Saved scripts must use an absolute path that exists when saved and when queried.
Command Bar never treats arbitrary query text as a shell command. A script is
visible only after entering `>` and selecting the saved result.

Quick Panel is available with `Ctrl+Alt+Q` and opens Command Bar, Scratchpad,
Features, mute, theme, and lock actions. Scratchpad stores multiple local tabs,
autosaves atomically, renders lightweight Markdown headings and lists, and can
export Markdown. The radial menu remains in the final module 6 commit.
