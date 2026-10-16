# Clipboard history and privacy

Clipboard history is opt-in and local. WinNotch registers for
`WM_CLIPBOARDUPDATE` only while the feature is enabled and removes the listener
when disabled.

Supported content:

- Unicode text, limited to 100,000 characters per item;
- file lists, limited to 50 distinct paths per item; and
- PNG images, limited to 5 MiB per item.

The history deduplicates content by SHA-256, preserves pin state when an item is
copied again, keeps pinned items outside the normal capacity limit, and evicts
the oldest unpinned entries first. Search is local and matches text or file
names.

Metadata is written atomically under `%AppData%\WinNotch\Clipboard`. Images are
stored as separate PNG files and orphaned images are removed only after the new
metadata file is committed. No clipboard content is sent over the network.

The Settings panel can disable image or file capture, change the unpinned
history limit, and exclude foreground process names. Password managers and
other sensitive applications should be added to that exclusion list.

Re-copying an item is suppressed from capture by comparing the Windows
clipboard sequence number, so using history does not create duplicate loops.

Optional behaviors are off by default:

- auto-clear removes the clipboard only when its sequence number still matches
  the captured item, so a newer copy is never erased by an older timer;
- automatic URL cleaning removes common advertising parameters while
  preserving functional query parameters and fragments; and
- `Ctrl+Shift+V` temporarily places Unicode text on the clipboard, sends paste,
  then restores a cloned multi-format clipboard only if no other application
  changed it in the meantime.

URL cleanup is also available as an explicit action on matching history rows.

## Shelf persistence

Shelf drops remain temporary unless explicitly pinned from the item menu. Only
pinned paths are written to `%AppData%\WinNotch\Shelf\pinned.json`, using an
atomic replacement. Clear removes unpinned items and leaves pinned items in
place. If a pinned file or folder moves or is deleted, the entry remains visible
as missing so the user can unpin it deliberately rather than losing state.
