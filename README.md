# LibraTray

![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)
![Platform: Windows 10 and 11](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4.svg)

LibraTray is a tray-first, local-only Windows controller being built for the
**Yeelight Libra Pro**—also sold as **Yeelight LED Screen Light Bar Pro** or
**Yeelight Monitor Light Bar Pro**, hardware model **YLTD003**, internal model
`lamp15`. It is focused on instant control and reliable two-channel state
synchronization.

> **Current status:** phases A/B are complete and Phase C is in progress. The
> repository now contains a tray-first WPF application with trusted local
> discovery, verified main/background controls, notification reconciliation,
> bounded retry/rate limiting, cold-start recovery, and fixed global shortcuts.
> implemented. Opt-in Windows lifecycle automation is implemented; packaging,
> signing, and a GitHub Release are still pending. The two-zone RGB command remains
> disabled because its relationship to the observed cold-start failure has not
> been isolated.

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
- a Windows tray icon, context menu, and live quick panel;
- verified main power, brightness, and 3000–6500 K colour-temperature control;
- verified background power, brightness, and whole-background RGB presets;
- bounded retry, notification reconciliation, and per-connection rate limiting;
- fixed global shortcuts with conflict reporting, queued input, and
  single-instance protection;
- captured shortcut rebinding, configurable adjustment steps, and device alias;
- local presets plus validated custom whole-background RGB input;
- versioned settings with corruption fallback and atomic replacement;
- versioned settings import/export with validation, preview, and a privacy
  warning;
- light, dark, and Windows-following themes plus live Simplified Chinese and
  English resources;
- opt-in lock/display power automation plus guarded shutdown/startup restore;
- an advanced device-details window and bounded redacted diagnostic summary;
- automated tests and a mock-device test surface.

Planned next:

- optional OSD;
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

The tray icon, context menu, trusted device discovery, verified controls,
physical-control reconciliation, and the following global shortcuts are
implemented:

- left-click the tray icon to show or hide the quick panel immediately;
- middle-click it to toggle the main light;
- optionally scroll over it to adjust main brightness using the configured
  step;
- right-click for current connection state, both channel switches, all-on/off,
  local presets, state refresh/reconnect, settings, safe device information,
  and explicit exit.

Double-click is intentionally not assigned: reliably distinguishing it would
delay every single-click action by the Windows double-click interval. Rapid
wheel input is coalesced before it reaches the device request queue.

| Action | Current default |
| --- | --- |
| Toggle main light | `Ctrl+Alt+L` |
| Toggle ambient light | `Ctrl+Alt+A` |
| Main brightness up/down | `Ctrl+Alt+Up` / `Ctrl+Alt+Down` |
| Main colour temperature up/down | `Ctrl+Alt+Right` / `Ctrl+Alt+Left` |

The default shortcuts use Win32 no-repeat registration and can be rebound by
focusing a capture field and pressing a new key combination. A key conflict is
reported in the panel without terminating the application. Inputs
received while a prior command is being verified are queued and evaluated
against the latest confirmed state. Only one LibraTray instance may run in a
Windows session, preventing a second instance from falsely reporting every key
as occupied. No normal UI exposes `lamp15` as the device name.

Windows automation is disabled by default and can be enabled separately for
session lock/unlock, display off/on, startup registration, and guarded
shutdown/startup restore. Lock and display blockers share one captured state,
so overlapping events restore only after every blocker is cleared. Recent
manual input and a newer device state always win.

Shutdown restore uses a one-time ticket under
`%LOCALAPPDATA%\LibraTray\shutdown-restore.json`. The ticket is written only
after both channels are confirmed off, contains a hash of the exact discovered
device ID rather than its address, expires after seven days, and is consumed or
discarded on the next eligible launch. Startup waits up to one minute for the
LAN device, re-reads it, and restores only when the exact expected off state is
still present. Session ending is never held for more than three seconds for a
best-effort device operation. Enabling “start with Windows” writes the current
user's standard `Run` entry and does not require elevation.

The Device entry in the quick panel or tray menu opens advanced identity,
firmware, capability, endpoint, and last-confirmed-state details. This is an
explicit advanced surface where the protocol identifier `lamp15` may appear.
Its Diagnostics tab generates a bounded summary that replaces the device ID,
LAN endpoint, reported name, and user alias before copy. Probe logs are separate
artifacts and must still be reviewed before sharing.

Settings are stored in `%LOCALAPPDATA%\LibraTray\settings.json`. The current
schema stores only local preferences: alias, adjustment steps, shortcut
bindings, tray-wheel preference, Windows automation switches, at most 20 local
presets, and theme/language preferences. Corrupt or unsupported settings fall
back to safe defaults. No account credential, cloud token, or device address is
stored.

Settings can be exported to a versioned `*.libratray-settings.json` file. Import
accepts UTF-8 files up to 1 MiB, validates both the export format and settings
schema, then shows a summary before loading the values into the Settings window.
Nothing is applied until **Save** is selected. Export writes through a temporary
file in the destination directory and warns that aliases, shortcuts, presets,
and automation preferences are included. Review an export before sharing it.

Appearance can follow Windows or be forced to the light or dark theme. The
interface can likewise follow the installed Windows UI language or explicitly
use Simplified Chinese or English. A saved change is applied immediately; the
Windows-following theme also reacts to later system appearance changes.

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

To run the current tray application during development:

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
