# Music integration

WinNotch uses Windows Global System Media Transport Controls (GSMTC). It
enumerates every available session with `GetSessions()`, follows the system
current session by default, and lets the user pin a preferred source. If that
source disappears, WinNotch temporarily falls back to the current Windows
session without discarding the preference.

## Lyrics and consent

Online lyrics are off by default. Enabling the setting sends only the current
track title, artist, album, and duration to LRCLIB. WinNotch identifies itself
with the `Lrclib-Client` header, caches up to 64 results in memory, cancels stale
lookups when the track changes, and never uploads playback history.

LRCLIB's public API and response fields are documented at
[lrclib.net/docs](https://lrclib.net/docs). Synced LRC timestamps are parsed in
Core and followed using the local GSMTC playback position. Plain lyrics are
used when synchronized text is unavailable. Instrumental and not-found results
are represented explicitly.

## Queue capability

GSMTC exposes transport, playback, media properties, and timeline information,
but no cross-player queue API. WinNotch therefore reports queue capability as
unsupported unless a source-specific `IMediaQueueAdapter` is installed. It does
not infer or fabricate a queue from track history.
