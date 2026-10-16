# Vorssaint parity roadmap

## Scope baseline

This roadmap freezes the comparison target at Vorssaint commit
`a3161dcd2612ed70a75bcfb8fe34a2ceef77b2cd` from 2026-10-16. The upstream
catalog contains 77 independently installable features across eight groups.
Later Vorssaint changes are evaluated as new scope rather than silently added to
an in-progress WinNotch module.

The target is Windows-native functional equivalence, not a pixel-identical Mac
port. WinNotch keeps its own name, icon, visual language, signing identity, and
update channel.

## Equivalence decisions

| macOS concept | WinNotch equivalent |
| --- | --- |
| Dock | Windows taskbar and app window surfaces |
| Spaces | Windows virtual desktops where public APIs permit |
| Finder | Explorer and Shell APIs |
| Homebrew | winget first; optional adapters for Scoop or Chocolatey |
| DMG installer | MSI, MSIX, and signed EXE installers |
| menu bar metrics | island indicators, tray surfaces, and optional overlay |
| XDR extra brightness | supported HDR controls or software dimming; never claim universal hardware support |
| macOS fan control | explicit vendor/provider support matrix with safe read-only fallback |
| lock-screen island | deferred unless Windows exposes a supported surface |

Features that require undocumented shell behavior, kernel drivers, vendor EC
access, or interference with protected processes must degrade safely and show
their support boundary in the UI.

## Delivery rules

Each module is delivered as one or more focused commits, with the final module
commit satisfying all of the following:

1. Debug and Release solution builds pass.
2. Relevant automated tests pass and cover new policy or persistence logic.
3. A runtime smoke test passes for UI or service-lifecycle changes.
4. Settings migrate without discarding existing user choices.
5. Background work stops when the module is disabled.
6. Permissions, elevation, network use, and unsupported hardware are explicit.
7. Generated build output is removed before commit.

## Module sequence

### 1. Feature catalog and lifecycle

Create the stable feature identity catalog, groups, availability state,
dependency checks, start/stop lifecycle, persisted enable state, and a minimal
Features settings surface. Existing built-ins remain enabled through migration.

Status: implemented. The catalog currently exposes implemented WinNotch
capabilities and planned delivery modules; the lifecycle supports idempotent
start/stop, restart, failure state, persistence overrides, and a Features UI.

### 2. Timer, focus cycles, stopwatch, and live activities

Add durable timers, focus-cycle rules, alarms, compact indicators, and island
activity selection. Timer policy belongs in Core and must be clock-testable.

Status: implemented. Focus/short-break/long-break cycles, configurable countdown
duration, stopwatch, UTC-anchored restore, daily completion counts, alarms,
compact closed-island status, top and vertical controls, persistence, and live
feature enable/disable are included.

### 3. Download and notification activities

Monitor user-selected download folders and integrate Windows notification
listener access. Provide history, dismiss/open behavior where supported, and
clear consent states.

Status: implemented. Download monitoring includes explicit folder
selection, browser temporary-file recognition, stable-file completion,
top/vertical/compact island surfaces, persistence, and live lifecycle toggling.
The notification half uses `UserNotificationListener`, explicit consent,
foreground synchronization, per-item dismissal, revocation checks, island
surfaces, and a clear unsupported state. The current Inno build cannot receive
that Windows capability; MSIX identity and signing remain module 15 work.

### 4. Music expansion and AI agent activity

Add lyric providers with consent, playback queue adapters, source selection,
per-process equalizer input, and local agent activity for Codex, Claude,
OpenCode, and Copilot. Never upload local session logs.

Status: in progress. Local agent discovery and bounded metadata parsing are
implemented for all four providers, with coarse lifecycle state and
top/vertical/compact island surfaces. Media sessions can be enumerated and a
preferred source persisted. Opt-in LRCLIB lookup, synchronized/plain parsing,
cancellation, caching, and current-line display are implemented. Queue has a
source-adapter contract because GSMTC exposes no universal queue; provider
adapters and per-process equalizer work remain.

### 5. Clipboard and file workflow

Implement persistent clipboard history, pin/search/paste, automatic clearing,
plain-text paste, URL cleanup, and a durable Shelf. Sensitive formats and
ignored applications require explicit policy.

Status: implemented. Persistent text/image/file history, SHA-256 deduplication,
pinning, search, capacity eviction, atomic metadata/image storage, foreground
application exclusions, copy-back suppression, top/vertical/compact views, and
live lifecycle toggling are implemented. Sequence-safe automatic clearing,
clipboard-preserving plain-text paste, and manual/automatic URL cleanup are
implemented. Shelf items remain temporary by default, while explicit pinning,
atomic persistence, restart restore, missing-path state, and pin-preserving
clear behavior are implemented.

### 6. Command bar and quick tools

Add app/window/file search, calculations, unit conversion, emoji, snippets,
scripts, quick toggles, quick panel, radial menu, and scratchpad.

Status: implemented. The Command Bar shell, global hotkey, provider isolation,
Start Menu applications, visible windows, clipboard history, safe scientific
calculator, unit conversion, Emoji, keyboard navigation, and execution are
implemented. Bounded local file indexing with configurable roots, reusable
snippets, and explicitly saved `>` scripts with a management UI are
implemented. `Ctrl+Alt+Q` Quick Panel provides Command Bar, Scratchpad,
Features, mute, theme, and lock actions. Persistent multi-tab Scratchpad,
autosave, lightweight Markdown preview, and export are implemented. The
configurable `Ctrl+Alt+R` radial menu provides up to 12 ordered typed actions,
validates persisted application/file/folder/HTTP(S) targets, and never executes
labels or arbitrary query text.

### 7. Window and taskbar management

Implement window switching with previews, layouts, restore, multi-monitor
movement, taskbar previews, close/quit policy, and virtual-desktop-aware
behavior where supported.

### 8. System monitor and alerts

Add CPU, GPU, memory, network, disk, power, battery health, connected devices,
history, process attribution, and sustained-condition alerts. Sensor providers
must report confidence and availability.

### 9. Audio control

Implement per-session volume, process loopback, device switching and priority,
microphone controls, and routing only through supported APIs. Any APO or driver
work is a separately approved submodule.

### 10. Screenshot, OCR, and color capture

Implement area/window/display capture, scrolling capture where reliable,
annotation, crop, redaction, pinning, offline OCR/QR, and color formats.

### 11. Recording and media tools

Implement screen recording with system and microphone tracks, trim/cut/zoom,
blur, GIF/video export, image conversion, compression, and watermarking.

### 12. Application and package management

Add app update discovery, winget integration, installer handoff, cleaner,
uninstaller with review, messaging-download retention, port inspection, and
safe process termination. Destructive actions require previews and recoverable
defaults.

### 13. Input enhancements

Add scroll direction and shaping, focus-follows-mouse, acceleration controls,
side-button mapping, middle click, debounce, Super key, text snippets, and app
exclusions. Anti-cheat and elevated-window limitations must be documented.

### 14. Display, energy, and hardware providers

Add Keep Awake rules, external-display DDC/CI, brightness steps, software
dimming, sleep Bluetooth policy, temperature providers, and opt-in fan control
for validated hardware only.

### 15. Product hardening

Complete accessibility, localization, settings export/import, permission hub,
crash isolation, performance budgets, installer migration, signed release
automation, and supported-hardware documentation.

## Current coverage and estimate

WinNotch already covers the island shell, multi-monitor placement, media
transport, basic visualizer, master volume and brightness HUDs, battery,
Outlook calendar, transient Shelf, camera preview, themes, updates, and plugin
loading. Several are partial implementations rather than parity-complete
modules.

After the architecture split, remaining Windows-equivalent parity is estimated
at roughly 48-78 engineer-months. A literal 100% cross-hardware clone is not a
valid promise; the deliverable is a tested Windows feature set with explicit
capability and hardware fallbacks.
