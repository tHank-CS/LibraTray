# Troubleshooting

This guide applies to the protocol probe, tray application, and locally built
release candidates. Published GitHub Release availability is documented in the
README files.

## Confirm the local toolchain

From the repository root:

```powershell
.\scripts\restore.ps1 -Locked
.\scripts\verify.ps1 -SkipRestore
dotnet run --project tools/LibraTray.Probe -- --help
```

Use the .NET 10 SDK. If PowerShell cannot find `dotnet`, install the x64 SDK
from Microsoft, close and reopen the terminal, and rerun `dotnet --info`.
The repository scripts also use a local `.dotnet\dotnet.exe` when present. If
local execution policy blocks a checked-out script, invoke the same file with
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File`, keeping its remaining
arguments unchanged.

## Discovery returns no devices

Check in this order:

1. The PC and light are on the same trusted IP network.
2. The light is online in the vendor app.
3. LAN control is enabled if the installed app, region, and firmware expose
   that option.
4. Windows identifies the network as Private.
5. Windows Firewall allows the probe on Private networks only.
6. Wi-Fi access-point/client isolation is disabled for these two devices.
7. UDP multicast to `239.255.255.250:1982` and unicast responses are not
   blocked.
8. VPN, security suite, virtual adapters, or multiple active NICs are not
   selecting the wrong route.

Discovery responses are unicast to the source address and port of the request.
Do not create an inbound rule for all networks or expose any router port merely
to make discovery work.

On a host with VPN/tunnel, Hyper-V, WSL, VMware, or multiple physical
interfaces, explicitly bind the probe to the PC's trusted-LAN IPv4 address:

```powershell
# Example only: replace 192.168.1.20 with this PC's physical LAN address.
dotnet run --project tools/LibraTray.Probe -- discover --local-address 192.168.1.20
```

The address must belong to this PC; it is not the light's address. For
`safe-write`, discovery still validates that the UDP sender, advertised
control endpoint, and requested device address match exactly.

If the device address is already known, use the manual-address option shown by
the probe's current `--help`. A manual IP bypasses discovery only; it does not
prove the target is a Yeelight device.

## Device is found under an unexpected name

Normal display mapping occurs only when the trimmed, case-insensitive internal
model is exactly `lamp15`. Technical output should then retain:

```text
Friendly name: Yeelight Libra Pro
Hardware model: YLTD003
Internal model: lamp15
Reported name: <device supplied value>
```

An unknown model must not be relabeled. Do not infer identity from “Libra,”
“Pro,” or “Screen Light Bar” in a reported name. Include the raw model in a
redacted bug report.

## TCP connection is refused or closes

Use the endpoint advertised in the discovery `Location` header. The official
protocol shows port 55443, but an implementation must parse the actual URI.

Yeelight's published limits are up to four simultaneous TCP connections,
60 commands per minute per connection, and 144 LAN commands per minute across
connections. Close other LAN integrations temporarily, wait for stale
connections to expire, and avoid reconnect loops.

Other causes include:

- LAN control disabled;
- device reboot or firmware update;
- incorrect manual address;
- routed/firewalled VLAN;
- malformed or unsupported command;
- command bursts exceeding the device quota.

## Responses time out

- Confirm the device still responds to discovery.
- Stop rapid retrying; retries consume the same quota.
- Close other Yeelight clients.
- Increase a probe timeout only enough to diagnose latency.
- Capture timestamps, exact redacted JSON, and disconnect events.

A timeout is not a successful command. State must remain unconfirmed or roll
back until a response, notification, or reconciliation query confirms it.

## Diagnostic log path already exists

The probe deliberately opens a new JSONL file and never appends to or
overwrites an existing diagnostic log. Choose a new filename:

```powershell
dotnet run --project tools/LibraTray.Probe -- discover --log-path .\diagnostics\probe-new.jsonl
```

If that path already exists, select another new path. The default timestamped
path under `%LOCALAPPDATA%\LibraTray\logs` normally avoids collisions.

## State differs after using the physical knob

Firmware 38 has been observed sending an incorrect `bg_power=on` notification
when the ambient light was independently switched off. LibraTray reconciles
notifications with a follow-up state query instead of treating that notification
as authoritative. If the interface still differs from the physical light, use
**Refresh state** once and record:

- firmware version;
- starting main and ambient state;
- exact knob action;
- all `props` notifications;
- a state query immediately after the action and again after a short delay;
- whether reconnect changes the result.

This observation does not establish behavior for every YLTD003 firmware. A
single notification must not override a conflicting confirmed query.

## Parser reports malformed or oversized data

Do not disable the size limit. Preserve the redacted frame length, connection
state, firmware, and a safely truncated sample. A device sending invalid input
must not crash the process or cause unbounded buffering.

## Wallpaper Engine screensaver returns to lock screen incorrectly

LibraTray treats a secure Windows screensaver and a WTS-locked session as one
lock blocker. Ensure Windows marks the screensaver as requiring the sign-in
screen and that **Lock/unlock automation** is enabled. Ending Wallpaper Engine
at the lock screen must not restore the light; only an explicit unlock or two
confirmed unlocked WTS samples may restore it. Display-on is also held while a
lock or secure screensaver remains.

The bounded automation log is stored under `%LOCALAPPDATA%\LibraTray\logs` and
records event source, fused outcome, and duration without device identifiers,
network addresses, or usernames. If behavior differs, copy only the smallest
relevant excerpt and still review it before sharing.

## Lights do not restore after Windows startup

First confirm the running process was launched with `--startup`; an ordinary
manual launch intentionally does not consume shutdown state. Then inspect the
bounded automation log. A normal shutdown/startup cycle contains
`SessionEndingRequested`, `shutdown-ticket Prepared`, and after login a
`startup-restore` entry with `Ticket=True` followed by `ApplyingTicket`. If the
ticket is present but the device is unavailable, the application retries every
three seconds for up to one minute and records `DeviceUnavailableAfterTimeout`.
If `Ticket=False`, the startup connection wait is not the cause: the prior
session did not leave a restorable ticket, the lights were already off, or the
shutdown request was canceled.

Conditional shutdown restoration intentionally does not turn on lights that
were already off before shutdown. For an unconditional sign-in action, enable
**Start LibraTray when signing in to Windows** and its indented **Turn on the
main and ambient lights after signing in** option. This policy is independent
of the ticket; its log entry uses `PowerOn=True` and
`ApplyingPowerOnPolicy`.

The ticket is `%LOCALAPPDATA%\LibraTray\shutdown-restore.json`. It contains a
hashed device key rather than an address and is normally removed after a safe
restore or discard decision. Do not create or edit it manually.

If the screensaver never starts automatically, first verify that Windows still
shows it as enabled with a nonzero timeout and that the configured `.scr` file
exists. Then check whether the system idle timer advances; synthetic-input,
remote-control, macro, accessibility, or UI-automation helpers can continuously
reset it even when LibraTray is not running. LibraTray does not request display
or system execution-state overrides. Stop only a precisely identified helper
and recheck idle time before changing the screensaver or power plan.

## Experimental segment colours are not readable

The device does not expose left/right colour properties. The UI therefore shows
the last requested values and asks for visual confirmation. If a write returned
success but the appearance is wrong, select **Restore whole colour** first. Do
not infer current segment colours from `bg_rgb`; that property retains the last
whole-background colour.

On firmware 38, the Device window's **Ambient self-test and recovery** action
can initialize a silent ambient renderer and restore the prior whole or
segmented target. It may flash at 1% brightness. Run it once, not repeatedly;
failure should be followed by a state refresh and a redacted diagnostic review.

## Sharing diagnostics safely

1. Use the probe's redacted export mode.
2. Open the export in a text editor.
3. search for your IP ranges, device ID, MAC address, computer name, Windows
   username, Wi-Fi name, and absolute user-profile path;
4. replace any remaining identifier consistently, such as
   `<DEVICE_IP_1>` or `<DEVICE_ID_1>`;
5. attach only the smallest relevant excerpt.

Never post a Mi Home/Yeelight password, token, QR code, full unredacted
configuration, or raw home-network inventory. See
[the testing guide](testing-guide.md) for a report template.
