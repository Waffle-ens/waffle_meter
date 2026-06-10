# WinDivert binaries (drop here)

The default capture backend (`WinDivertBackend`) P/Invokes **WinDivert.dll** and the driver loads
**WinDivert64.sys**. These are external, vendor-signed binaries and are **not** committed to this repo —
place them here and the build copies them next to `WaffleMeter.CaptureHost.exe` (the only elevated
component; the unelevated UI never touches the driver).

## What to drop in this folder

From the official WinDivert release (https://github.com/basil00/WinDivert — use the latest signed
**2.2.x** x64 release, e.g. `WinDivert-2.2.2-A.zip`, `x64/` folder):

| File | Purpose |
|------|---------|
| `WinDivert.dll` | x64 user-mode DLL (loaded by `WinDivertBackend` via P/Invoke by simple name) |
| `WinDivert64.sys` | x64 WHQL/vendor-signed kernel driver (installed by `WinDivertOpen`) |
| `LICENSE` | WinDivert license (LGPLv3 / GPLv2) — ship alongside; the app links dynamically |

Do **not** add the 32-bit `WinDivert32.sys` (the app is x64-only).

## How it is bundled

`WaffleMeter.CaptureHost.csproj` copies `*.dll` + `*.sys` from this folder into the host's output
(`CopyToOutputDirectory=PreserveNewest`), so they land beside `WaffleMeter.CaptureHost.exe`.
`WinDivert.dll` resolves by simple name from that directory; `WinDivert64.sys` must be beside it for
the driver install. If the files are absent the build still succeeds, but `WinDivertBackend.Start`
fails fast with a clear "binaries missing" message (use the Npcap backend in the meantime).

## Notes

- HVCI / driver-signature-enforcement on a clean Win11 machine is the go/no-go capture spike — verify
  the `.sys` actually loads there.
- Npcap is the optional fallback (user-installed system driver); it is **not** bundled.
