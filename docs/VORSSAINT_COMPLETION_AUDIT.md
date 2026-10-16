# Vorssaint parity completion audit

Audit date: 2026-10-17
Frozen comparison target: Vorssaint `a3161dcd2612ed70a75bcfb8fe34a2ceef77b2cd`

## Result

All 15 Windows-equivalent delivery modules in
`VORSSAINT_PARITY_ROADMAP.md` are implemented. The built-in feature catalog has
no `Planned` or `Unsupported` entries. Platform-dependent behavior reports an
explicit capability state instead of pretending that Windows or unsupported
hardware exposes a feature that it does not.

This result means functional equivalence to the frozen catalog under the
documented Windows mappings. It does not claim pixel parity, undocumented API
access, universal hardware support, or coverage of later Vorssaint changes.

## Delivery history

| Area | Commits |
| --- | --- |
| Architecture and roadmap | `07ea6f9`, `11571b2` |
| 1. Catalog and lifecycle | `1a4d2c6` |
| 2. Timers, focus, stopwatch | `d5dda1a` |
| 3. Downloads and notifications | `6e758a8`, `e580551` |
| 4. Agents, media, lyrics, queues | `08a8113`, `571345b`, `81cf999` |
| 5. Clipboard and Shelf | `4cffceb`, `feafa0f` |
| 6. Command Bar and quick tools | `65469b7`, `9cbb3bc`, `2e3d0a4`, `b3c06a7` |
| 7. Window management | `2b3eebd` |
| 8. System monitor | `aab2c95` |
| 9. Audio control | `89d7818` |
| 10. Capture, OCR, QR, color | `024ed19` |
| 11. Recording and media tools | `bce6c1b` |
| 12. Application management | `ed59998` |
| 13. Input enhancements | `71a1797` |
| 14. Display, energy, hardware | `9cb75a4` |
| 15. Product hardening | `65d2d83` |

## Final verification

- Debug solution build: passed.
- Release solution build: passed.
- Release automated suite: 180/180 passed, including the final catalog
  completion assertion.
- Scoped formatter verification for every file touched by the final media
  module: passed.
- Isolated-AppData application startup: remained healthy for the five-second
  smoke interval and shut down cleanly.
- Windows Credential Manager round trip: passed with a random value that was
  deleted immediately.
- Current-user DPAPI fallback round trip: passed with a random temporary file
  that was deleted immediately.
- Spotify and VLC protocol parsing, authorization, loopback restrictions,
  persistence migration, queue UI construction, and queue-service bounds are
  covered by automated tests.
- NuGet direct and transitive vulnerability query: no vulnerable packages were
  reported for any solution project. The NuGet service emitted intermittent
  `NU1900` TLS audit warnings while restoring; a separate package query still
  completed and returned the project-by-project result.
- Self-contained publish and MakeAppx smoke: passed; the audit MSIX contained
  the app plus manifest assets and was removed afterward.
- Git working tree and generated-output cleanup are checked after the final
  commit.

The remaining `WFAC010` compiler warning is intentional: WinNotch retains its
manifest-based Per-Monitor V2 DPI declaration for its WPF/WinForms composition.

## Explicit boundaries

- The MSIX build is structurally validated locally. Actual release signing
  requires the repository's Azure Trusted Signing secrets and runs only in the
  release workflow.
- Spotify and VLC adapters are protocol-tested without using a real user's
  account or password. Live provider access requires explicit user setup.
- Notification access requires MSIX identity and user consent.
- Virtual desktop, audio routing, protected/elevated input, thermal, fan,
  DDC/CI, OCR language, and process-loopback behavior remain limited to public
  Windows APIs and the supported-hardware matrix.
- No generic embedded-controller writer, protected-process bypass,
  anti-cheat bypass, private audio routing interface, or fabricated media queue
  is included.
