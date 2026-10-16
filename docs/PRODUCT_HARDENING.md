# Product hardening

## Accessibility and localization

All windows use a visible high-contrast-aware keyboard focus visual. Icon-only
settings, navigation, and pin controls expose automation names, while ordinary
buttons retain their visible text as accessible names. Windows high-contrast
changes continue through the theme watcher.

Language preference supports automatic system culture, English, and Simplified
Chinese. The localization service translates the shared window/action catalog
at activation and leaves unknown or dynamic source text in English rather than
guessing. A restart fully reapplies culture-sensitive date/number formatting.

## Backup, permissions, diagnostics, and performance

The **Permissions, backup & diagnostics** window is available from inline
Settings and the tray menu. It provides:

- atomic JSON settings export and normalized import with an envelope version;
- package identity, notification, camera, microphone, radio, startup, input,
  and capture capability status;
- explicit links to Windows-owned privacy pages and notification consent;
- local JSON crash reports for UI, unobserved-task, and fatal process errors,
  bounded to the newest 20 files; and
- sustained performance budgets: 250 ms UI dispatcher latency, 20% process CPU,
  and 450 MiB working set.

No report or performance sample is uploaded.
Settings exports can contain local paths, snippet content, and user-entered
script arguments, so the permission hub labels the backup as private data.

## Installer and release hardening

Releases now produce:

- a signed standalone `WinNotch.exe`;
- a signed Inno Setup installer; and
- a signed x64 MSIX with full-trust desktop identity, startup task, webcam,
  microphone, internet, and `uap3:userNotificationListener` capabilities.

The release workflow requires Azure Trusted Signing secrets and fails rather
than publishing unsigned release artifacts. The package Publisher must exactly
match the signing certificate subject. The MSIX layout is validated by
`MakeAppx` and excludes PDB/XML development artifacts.

The packaged app uses `StartupTask`; unpackaged/Inno builds retain the
current-user Run value. The migration panel detects a remaining Inno
registration, removes its duplicate startup entry only after confirmation, and
launches the registered legacy uninstaller. Shared user data is never deleted
automatically.
