# Troubleshooting

This guide applies to the current protocol probe. Tray UI and packaged releases
do not exist yet.

## Confirm the local toolchain

From the repository root:

```powershell
dotnet --info
dotnet restore LibraTray.slnx
dotnet build LibraTray.slnx -c Release --no-restore
dotnet test LibraTray.slnx -c Release --no-build
dotnet run --project tools/LibraTray.Probe -- --help
```

Use the .NET 10 SDK. If PowerShell cannot find `dotnet`, install the x64 SDK
from Microsoft, close and reopen the terminal, and rerun `dotnet --info`.

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

## State differs after using the physical knob

This is an expected research target and is still unverified for the user's
device/firmware. Record:

- firmware version;
- starting main and ambient state;
- exact knob action;
- all `props` notifications;
- a state query immediately after the action and again after a short delay;
- whether reconnect changes the result.

An old vendor-forum report and Home Assistant handling suggest that some
background-state notifications may be unreliable, but this is not proof for
all YLTD003 firmware. Do not treat a single notification as authoritative until
the behavior is reproduced.

## Parser reports malformed or oversized data

Do not disable the size limit. Preserve the redacted frame length, connection
state, firmware, and a safely truncated sample. A device sending invalid input
must not crash the process or cause unbounded buffering.

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
