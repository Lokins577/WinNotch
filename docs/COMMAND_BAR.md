# Command Bar

Press `Ctrl+Alt+Space` to open Command Bar. The hotkey uses `MOD_NOREPEAT` and
is registered only while the feature is enabled.

Current providers:

- Start Menu applications;
- visible top-level windows;
- clipboard history when that feature is enabled;
- arithmetic and scientific functions using a dedicated parser;
- length, mass, duration, data, and temperature conversion; and
- keyword-searchable Emoji.

The calculator does not evaluate C#, PowerShell, JavaScript, or shell text. Its
grammar is limited to numeric literals, parentheses, arithmetic operators,
`pi`, `e`, and an allowlist of mathematical functions. Provider failures are
isolated so one broken index does not close the bar or hide other results.

Application results come from the current user's and all-users Start Menu.
Window results enumerate visible top-level windows and exclude WinNotch itself.
Clipboard results use the existing privacy-filtered history; enabling Command
Bar does not implicitly enable clipboard capture.

File search, snippets, scripts, Quick Panel, radial menu, and Scratchpad are
delivered in the remaining module 6 commits.
