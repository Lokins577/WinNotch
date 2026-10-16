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

## Provider playback queues

GSMTC exposes transport, playback, media properties, and timeline information,
but no cross-player queue API. WinNotch therefore chooses a source-specific
`IMediaQueueAdapter` for the active media session. It does not infer or
fabricate a queue from track history.

Two adapters ship with WinNotch and are disabled until explicitly configured:

- Spotify reads `GET https://api.spotify.com/v1/me/player/queue`. The supplied
  access token must grant `user-read-playback-state` and
  `user-read-currently-playing`. Tokens use Windows Credential Manager, with an
  atomic current-user DPAPI file fallback when the vault is unavailable. They
  are sent only as a bearer credential to the fixed Spotify API endpoint and
  may need replacement when they expire.
- VLC reads `/requests/playlist.json` from its Web interface. WinNotch accepts
  only an HTTP(S) loopback URL, blocks redirects, and keeps the password in
  the same Windows user-bound credential storage. This deliberately prevents a
  VLC password from being sent to a LAN or Internet address.

The queue service follows the selected GSMTC source, refreshes at a bounded
interval, publishes explicit disabled/configuration/authentication/network
states, and caps the rendered queue at 100 entries. Provider responses are
bounded to 4 MiB. Open Settings and choose **Configure Spotify / VLC playback
queue** to opt in.
