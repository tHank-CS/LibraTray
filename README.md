# LibraTray

![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)
![Platform: Windows 10 and 11](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4.svg)

LibraTray is a tray-first, local-only Windows controller being built for the
**Yeelight Libra Pro**—also sold as **Yeelight LED Screen Light Bar Pro** or
**Yeelight Monitor Light Bar Pro**, hardware model **YLTD003**, internal model
`lamp15`. It is focused on instant control and reliable two-channel state
synchronization.

> **Current status:** phases A/B are complete and Phase C is in progress. The
> repository now contains an initial tray-first WPF shell, a tested product
> adapter, and bounded cold-start recovery. Device discovery and control are not
> yet wired into the tray UI, so its controls intentionally remain disabled.
> Global shortcuts, Windows automation, packaging, signing, and a GitHub
> Release are still pending. The two-zone RGB command remains disabled because
> its relationship to the observed cold-start failure has not been isolated.

English | [简体中文](README.zh-CN.md)

## Screenshot

The initial quick-panel UI is implemented. A verified capture will replace this
notice after visual QA is completed; no mock-up is presented as implemented
software.

## Why LibraTray?

Existing Yeelight tools are generally smart-home integrations, generic bulb
clients, or utilities for a different monitor light. LibraTray is intentionally
narrower:

- local LAN operation without a Xiaomi or Yeelight account, cloud token, or
  public-facing service;
- separate main-light and ambient-light state;
- a future compact Windows tray workflow instead of a permanent dashboard;
- explicit reconciliation of software commands, device notifications, queries,
  and physical-knob changes;
- a diagnostic probe before product-specific behavior is promoted into the
  application.

The last three items are design goals. The current milestone provides the probe
and protocol foundation, not the finished desktop experience.

## Supported devices and identity

| Display name | Hardware model | Internal model | Status |
| --- | --- | --- | --- |
| Yeelight Libra Pro | YLTD003 | `lamp15` | Primary target; selected Phase-B behavior verified on firmware 38 |

Official Yeelight material uses more than one retail name for YLTD003. The
`lamp15` → YLTD003 → Yeelight Libra Pro relationship is a **high-confidence
cross-source inference**, not a single global naming declaration from Yeelight.
LibraTray chooses “Yeelight Libra Pro” as its friendly default and retains the
hardware and internal model separately for diagnostics. See
[product identity research](docs/research/product-identity.md).

Names such as “Libra”, “Pro”, or “Screen Light Bar” are never used as fuzzy
device identifiers. Unknown models remain unknown, and no other Libra, Pro,
Pura, or YLTD product is treated as `lamp15` without evidence.

## Features

Available at the current milestone:

- UDP Yeelight LAN discovery;
- manual-address protocol probing;
- CRLF-delimited JSON request/response framing;
- command IDs, timeouts, cancellation, and bounded input handling;
- safe product-identity mapping and unknown-device fallback;
- redaction-oriented diagnostic output;
- an initial Windows tray icon, context menu, and offline quick-panel shell;
- automated tests and a mock-device test surface.

Planned after real-device verification:

- independent main and ambient channel controls;
- reliable physical-knob state synchronization;
- live discovery/control binding, configurable global shortcuts, and optional
  OSD;
- local presets;
- opt-in lock, unlock, sleep, wake, and display-power automation;
- portable packages and an installer.

Screen sampling, music/game effects, and a general-purpose Yeelight client are
out of scope for the first stable release.

## System requirements

- Windows 10 version 21H2 (build 19044) or later, or Windows 11;
- x64 processor;
- for source builds: the .NET 10 SDK;
- the PC and light on the same trusted LAN, with multicast allowed;
- Yeelight LAN control enabled for the device, where the installed Yeelight/Mi
  Home app and regional firmware expose that setting.

Only Windows versions still serviced by Microsoft are intended to receive full
support. Later operation on an out-of-service Windows build is best effort.

## Download and installation

There is no packaged download or GitHub Release yet. Do not obtain LibraTray
binaries from unofficial download sites.

The eventual portable distribution will be a self-contained `win-x64` archive:
extract it to a user-writable folder and run the included executable without
administrator rights. These instructions become actionable only after an
official release exists. Windows may warn about unsigned builds unless the
project obtains a code-signing certificate.

## First connection and protocol probe

1. Connect the Yeelight Libra Pro and the PC to the same trusted LAN.
2. In the vendor app, enable LAN control if that option is available for the
   device and region.
3. Ensure the Windows network profile is Private. Allow the probe on private
   networks only if Windows Firewall asks.
4. Inspect the probe's available commands:

   ```powershell
   dotnet run --project tools/LibraTray.Probe -- --help
   ```

5. Start discovery:

   ```powershell
   dotnet run --project tools/LibraTray.Probe -- discover
   ```

6. Verify that the raw model is exactly `lamp15` before attempting any
   product-specific experiment. Start with read-only discovery and state
   inspection. Do not send undocumented commands.

Probe syntax may evolve before v0.1.0; `--help` is the source of truth for the
checked-out revision. See the [testing guide](docs/testing-guide.md) before
testing real hardware. The default JSONL log is created under
`%LOCALAPPDATA%\LibraTray\logs`. If `--log-path` is used, it must name a new,
nonexistent file; the probe never appends to or overwrites an existing log.

## Tray, shortcuts, and Windows automation

The tray icon, context menu, and offline quick-panel shell are implemented.
Device discovery/control binding, shortcuts, and Windows automation are not
implemented yet. Planned shortcut defaults are:

| Action | Planned default |
| --- | --- |
| Toggle main light | `Ctrl+Alt+L` |
| Toggle ambient light | `Ctrl+Alt+A` |
| Main brightness up/down | `Ctrl+Alt+Up` / `Ctrl+Alt+Down` |
| Main colour temperature up/down | `Ctrl+Alt+Right` / `Ctrl+Alt+Left` |

Shortcuts will be configurable, registration failures will not terminate the
application, and lifecycle automation will be individually opt-in and
debounced. No normal UI will expose `lamp15` as the device name.

## Build from source

From a PowerShell prompt in the repository root:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\restore.ps1 -Locked
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\verify.ps1 -SkipRestore
```

The locked restore uses committed `packages.lock.json` files. Verification
checks formatting and analyzers, builds Release, runs all test projects with
TRX/Cobertura output, and audits vulnerable dependencies. The project uses
.NET 10 LTS and targets Windows x64. No private file, device IP, account
credential, or cloud token is required to build or test it.

To inspect the current offline quick-panel shell during development:

```powershell
.\.dotnet\dotnet.exe run --project .\src\LibraTray.App\LibraTray.App.csproj -c Release -- --show
```

Without `--show`, the application starts tray-first with its window hidden.

## Troubleshooting

If discovery finds nothing, check LAN-control availability, Wi-Fi client
isolation, multicast routing, the Windows Private network profile, and local
firewall rules. On a PC with VPN/tunnel or virtual adapters, bind discovery to
the physical LAN address with `--local-address <PC_LAN_IPV4>`. Use a manual
device IP only when the address is known and trusted.
Yeelight's published protocol permits only a small number of concurrent TCP
connections and rate-limits commands, so close other LAN clients while
diagnosing.

See [troubleshooting](docs/troubleshooting.md) and never publish an unredacted
diagnostic log.

## Privacy and security

LibraTray is designed for direct, local communication. It does not require a Mi
Home account, Yeelight account, password, device token, or cloud login. The
Yeelight LAN protocol may be plaintext, so use it only on a trusted network.
The project does not provide a local web server or an Internet listener.

Diagnostic data may still reveal device IDs, firmware, IP addresses, hostnames,
or filesystem paths. Redact it before sharing. See
[privacy and security](docs/privacy-and-security.md) and [SECURITY.md](SECURITY.md).

## Roadmap

- **v0.1.0:** protocol probe;
- **v0.2.0:** verified basic two-channel control;
- **v0.3.0:** tray, quick panel, and shortcuts;
- **v0.4.0:** reconciliation and automatic reconnect;
- **v0.5.0:** Windows lifecycle automation;
- **v1.0.0:** stable public release.

These are plans, not existing releases or promised dates.

## Contributing

Start with [CONTRIBUTING.md](CONTRIBUTING.md). Device-behavior reports are
particularly useful when they include firmware version, exact steps, expected
and observed results, and a **redacted** probe log. Do not submit vendor
credentials, tokens, private network identifiers, reverse-engineered closed
source code, or code without a compatible license.

## License and acknowledgements

LibraTray is licensed under the [Apache License 2.0](LICENSE). The license
decision is documented in [ADR 0002](docs/adr/0002-license.md). Public projects
reviewed during clean-room protocol research are listed in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md); no source code was copied from
them.

## Unofficial project disclaimer

**This project is an independent, unofficial open-source project and is not
affiliated with, endorsed by, or sponsored by Yeelight or Xiaomi.**

Yeelight, Xiaomi, Mi Home, and product names are trademarks of their respective
owners and are used only to describe compatibility.
