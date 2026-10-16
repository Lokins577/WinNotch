# WinNotch AI Plugin Prompt

Use the prompt below when you want another AI to build a WinNotch plugin from zero context.

Replace the placeholder values before sending it.

## Copy-paste prompt

```text
You are building a plugin for the WinNotch project.

Repository:
- Main app repo: https://github.com/N3uralCreativity/WinNotch
- Plugin library repo: https://github.com/N3uralCreativity/WinNotch-Plugins

Your job is to create a complete, working WinNotch plugin from zero context, inside the current repository workspace, using the existing WinNotch plugin system and coding style.

Plugin request:
- Plugin name: [PLUGIN_NAME]
- Plugin id: [PLUGIN_ID in reverse-domain style, for example com.example.weather]
- Plugin type: [UI / Service / Animation / Configurable / Combination]
- Short description: [ONE SENTENCE]
- Main behavior: [DETAILED DESCRIPTION OF WHAT THE PLUGIN SHOULD DO]
- Closed notch behavior: [WHAT IT SHOULD SHOW IN REDUCED VIEW, or "nothing"]
- Expanded notch behavior: [WHAT IT SHOULD SHOW IN OPEN VIEW]
- Vertical layout behavior: [HOW IT SHOULD ADAPT WHEN THE NOTCH IS VERTICAL OR SIDE-DOCKED]
- Theme behavior: [HOW IT SHOULD ADAPT TO DARK AND LIGHT THEMES]
- Configuration requirements: [NONE or LIST OF REQUIRED TOKENS / URLS / SETTINGS]
- External services or APIs: [NONE or LIST]
- Permissions needed: [clipboard / network / filesystem / location / etc.]
- Minimum WinNotch version target: [for example 0.7.2]
- Should it be browser-listable: [yes / no]

Important WinNotch context:
- WinNotch is a Windows WPF app built on .NET 8.
- Plugins live under Examples/Plugins in this repo for source examples.
- Runtime-installed plugins are loaded from %AppData%\WinNotch\Plugins\<plugin-id>\
- A plugin can implement:
  - IPlugin
  - IUIPlugin
  - IServicePlugin
  - IAnimationPlugin
  - IConfigurablePlugin
- Most plugins should inherit from PluginBase.
- Current UI plugin locations are:
  - ClosedContent
  - OpenContent
  - OpenAccessory
  - VerticalOpenContent
  - CustomTab
  - Settings
  - Overlay
- If the plugin adds visible content, it must work correctly in both top layout and vertical / side-docked layout.
- If the plugin has any user-facing visuals, it must support both dark and light theme.
- Theme changes can be observed through Context.Theme.
- Plugin-specific files should be stored using Context.GetPluginDataPath(Id).
- If the plugin requires user setup, implement IConfigurablePlugin so WinNotch can show the built-in configuration UI in Plugin Manager.

Current project structure and conventions:
- Main app project: src/WinNotch/WinNotch.csproj
- Plugin interfaces: src/WinNotch.PluginSdk/Contracts/
- Example plugins: Examples/Plugins/
- Plugin development guide: PLUGIN_DEVELOPMENT.md
- Example browser manifest format: Examples/Plugins/example-library.json
- Target framework used by example plugins: net8.0-windows10.0.22621.0
- WPF should be enabled in plugin projects.
- If building inside this repo, reference src/WinNotch.PluginSdk/WinNotch.PluginSdk.csproj directly.

Expected project setup:
- Create a new plugin source folder at:
  - Examples/Plugins/[PLUGIN_PROJECT_FOLDER]/
- Create at minimum:
  - [PLUGIN_PROJECT_FOLDER].csproj
  - main plugin .cs file
- Add any extra helper, model, settings, or README files if useful.

Expected .csproj shape if created inside this repo:
- Use Microsoft.NET.Sdk
- TargetFramework: net8.0-windows10.0.22621.0
- UseWPF: true
- Nullable: enable
- ImplicitUsings: enable
- Reference WinNotch via ProjectReference to src/WinNotch/WinNotch.csproj

Behavior requirements:
1. The plugin must feel native to WinNotch, not like a generic floating panel.
2. The UI must be compact and clean.
3. If the plugin is visible in expanded mode, it must not break the notch layout.
4. If the plugin makes sense only in expanded mode, return nothing for reduced mode.
5. If there is a vertical-friendly alternative layout, provide one through VerticalOpenContent.
6. If the plugin is a small top-row companion, consider OpenAccessory instead of OpenContent.
7. If configuration is required, do not rely only on hand-editing JSON files:
   - implement IConfigurablePlugin
   - provide clear labels
   - provide help text
   - explain where users find required values
   - validate input in ApplyConfigurationAsync
8. If external API calls are required:
   - use async code
   - handle errors gracefully
   - do not block the UI thread
   - surface a clear empty or error state in the UI
9. If the plugin subscribes to events or timers, clean them up properly in ShutdownAsync and Dispose.
10. Use semantic versioning.

Implementation instructions:
1. First inspect the actual current interfaces and relevant examples in the repo before coding.
2. Then implement the plugin fully, not just a draft.
3. Reuse established WinNotch patterns where possible.
4. Keep comments minimal and useful.
5. Do not invent APIs that do not exist in the repo.
6. If the requested feature needs new host-side WinNotch app support, say so clearly before assuming it already exists.
7. If the request can be done as a plugin only, keep it plugin-only.

Required repository checks before coding:
- inspect src/WinNotch.PluginSdk/Contracts/IPlugin.cs
- inspect src/WinNotch.PluginSdk/Contracts/IUIPlugin.cs
- inspect src/WinNotch.PluginSdk/Contracts/IConfigurablePlugin.cs if configuration is needed
- inspect src/WinNotch.PluginSdk/Contracts/IPluginContext.cs
- inspect at least 2 relevant examples under Examples/Plugins
- inspect PLUGIN_DEVELOPMENT.md

Output requirements:
- Create the actual files in the repository.
- Then provide:
  - a short summary of what was built
  - the list of created or changed files
  - whether the plugin is plugin-only or requires app changes
  - how the plugin behaves in reduced, expanded, and vertical layouts
  - how dark and light themes are handled
  - how configuration works, if any
  - what build command was used
  - what test or validation command was used

Validation requirements:
- Build the plugin project in Release.
- If possible, also build the main solution to catch interface issues.
- At minimum run:
  - dotnet build Examples/Plugins/[PLUGIN_PROJECT_FOLDER]/[PLUGIN_PROJECT_FOLDER].csproj -c Release
- If there are tests relevant to plugin interfaces, run them too.

If browser listing is requested:
- Also prepare a manifest entry matching the WinNotch plugin library format, using fields:
  - id
  - name
  - version
  - author
  - description
  - minimumWinNotchVersion
  - downloadUrl
  - homepage
  - iconUrl
  - category
  - permissions
  - dependencies
  - sha256
  - releaseDate
  - isVerified
- Do not invent a fake final hash if the DLL was not actually built.
- If a real release URL does not exist yet, clearly mark that manifest entry as a draft example.

Quality bar:
- The plugin should be polished enough to realistically ship in WinNotch.
- The layout should not feel generic.
- The code should compile against the actual repo.
- The plugin should respect WinNotch theme, spacing, and docking behavior.

Now do the work end to end.
```

## Suggested placeholder example

```text
Plugin name: Battery Insights
Plugin id: com.winnotch.batteryinsights
Plugin type: UI + Configurable
Short description: Advanced battery widget with time remaining and charge trends.
Closed notch behavior: nothing
Expanded notch behavior: shows battery percentage, charging state, estimated remaining time, and a compact trend summary
Vertical layout behavior: use a stacked layout with the same data in narrower form
Theme behavior: adapt brushes for both dark and light theme through Context.Theme
Configuration requirements: allow user to choose refresh interval and whether trend history is saved
External services or APIs: none
Permissions needed: []
Minimum WinNotch version target: 0.7.2
Should it be browser-listable: yes
```

## Notes

- This prompt is intentionally strict so the AI does not guess old WinNotch APIs.
- If you are using the prompt inside the WinNotch repo, leave the repository paths as-is.
- If you are using it outside the repo, also give the AI either the local cloned repo or direct access to the referenced files.
