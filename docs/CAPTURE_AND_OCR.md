# Capture, OCR, QR, and color

Enable **Capture and OCR** in the feature catalog, then press `Ctrl+Alt+S`.

The capture launcher supports:

- drag-to-select region capture on the display containing the pointer;
- complete display capture;
- a selected top-level window, using `PrintWindow` with screen-copy fallback;
- scrolling capture only when the selected window exposes a vertically
  scrollable UI Automation `ScrollPattern`;
- one-click pixel sampling as HEX, RGB, HSL, and HSV;
- crop, red drawing, permanent black-box redaction, reset, clipboard copy,
  PNG/JPEG/BMP export, and session pinning;
- offline Windows OCR with the installed user language packs; and
- offline QR decoding through ZXing.Net.

Scrolling capture moves the selected control in bounded steps, captures at most
30 frames, and attempts to restore its original scroll percentage. It is not
offered as a universal browser/document trick: custom-rendered surfaces that do
not expose `ScrollPattern` return an explicit unsupported message.

Window capture uses `PrintWindow` on a background thread because Microsoft
documents it as synchronous and potentially blocking. GPU-protected,
DRM-protected, minimized, or custom-rendered windows may return blank content;
WinNotch never bypasses capture protection.

OCR, QR decoding, annotation, and redaction run locally. Captures are written
only after the user chooses Save. The optional automatic clipboard copy is a
persisted setting.

- [`PrintWindow`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-printwindow)
- [Windows OCR](https://learn.microsoft.com/windows/uwp/audio-video-camera/ocr)
- [UI Automation Scroll pattern](https://learn.microsoft.com/windows/win32/winauto/uiauto-controlpatternsoverview)
