# Recording and media tools

Enable **Recording and media tools** in the feature catalog, then press
`Ctrl+Alt+V`.

## Recording pipeline

WinNotch records a selected display or region into a local project directory:

- timed JPEG screen frames at 2–30 frames per second;
- an optional WASAPI loopback system-audio WAV track;
- an optional microphone WASAPI WAV track; and
- an atomic JSON project manifest.

The system and microphone remain separate until export. If either endpoint is
unavailable, screen recording continues and the project records an explicit
warning. Disabling the feature stops active capture and closes both audio
clients.

Projects are stored under `%LocalAppData%\WinNotch\Recordings`. WinNotch
does not silently remove them because they may contain the only copy of a
recording; the project folder button exposes them for review.

## Non-destructive editing and export

The editor supports trim points, multiple cut ranges, timed zoom, timed
pixelated blur, and a text watermark. Source frames and WAV files remain
unchanged. During export WinNotch:

1. selects frames and audio ranges from the edit plan;
2. renders zoom, blur, and watermark changes into temporary frames;
3. exports an animated GIF with WPF imaging, or builds an MP4 with Windows
   `MediaComposition`; and
4. removes only its generated export-temporary directory.

The image tools batch-convert PNG/JPEG/BMP/GIF inputs to PNG or JPEG, optionally
resize the largest dimension, apply JPEG quality compression, and add a
watermark.

This implementation intentionally uses Windows media codecs instead of
downloading or invoking an unmanaged FFmpeg binary.

- [Media compositions and editing](https://learn.microsoft.com/windows/apps/develop/media-authoring-processing/media-compositions-and-editing)
- [`MediaClip.CreateFromImageFileAsync`](https://learn.microsoft.com/uwp/api/windows.media.editing.mediaclip.createfromimagefileasync)
- [`MediaComposition.RenderToFileAsync`](https://learn.microsoft.com/uwp/api/windows.media.editing.mediacomposition.rendertofileasync)
