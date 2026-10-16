# Audio control

Enable **Per-app audio** in the feature catalog, then press `Ctrl+Alt+A` to
open the control surface.

## Supported controls

- enumerate active render and capture endpoints;
- control volume and mute for sessions on the current default multimedia output;
- show each session's live peak level;
- control capture-endpoint (microphone) volume and mute;
- choose and persist the output WinNotch uses for its loopback visualizer;
- retain previously selected outputs as an ordered fallback list; and
- analyze the audio spectrum produced by one selected process and its children
  without writing audio to disk.

Process analysis uses the documented `ActivateAudioInterfaceAsync` application
loopback contract and requires Windows build 20348 or later. Closing the mixer,
disabling the feature, or quitting WinNotch stops analysis and releases WASAPI.

## Routing boundary

WinNotch does not call the undocumented `IPolicyConfig` family to replace the
system default endpoint. The **Sound settings** and **App routing** buttons open
Windows-owned settings surfaces. The output selector affects only WinNotch's
visualizer capture and falls back to the next active priority device or the
Windows default.

Endpoint volume is a shared system control and can affect other applications.
Session and microphone changes therefore occur only after direct user input;
enumeration and process-spectrum analysis are read-only.

- [`IAudioSessionManager2`](https://learn.microsoft.com/windows/win32/api/audiopolicy/nn-audiopolicy-iaudiosessionmanager2)
- [`IAudioEndpointVolume`](https://learn.microsoft.com/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolume)
- [Application loopback sample](https://learn.microsoft.com/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)
