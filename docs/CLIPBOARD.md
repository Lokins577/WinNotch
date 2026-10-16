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
