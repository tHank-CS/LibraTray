# Testing Guide

## Purpose

Automated tests establish parser, identity, and state-machine behavior. They
cannot establish how a particular YLTD003 firmware responds to a command or
physical-knob action. This guide keeps those evidence types separate.

Status vocabulary used in protocol documents:

- **officially documented** — stated in Yeelight's public LAN specification;
- **open-source reference** — observed in a named public implementation;
- **device verified** — reproduced on stock YLTD003 hardware with firmware and
  evidence recorded;
- **unverified** — not yet reproduced on the target device;
- **known firmware issue** — reproducible and scoped to recorded firmware;
- **fallback required** — the application must use a safer query/reconnect path.

At this milestone, product-specific command and physical-knob behavior is
**unverified**.

## Automated checks

From the repository root:

```powershell
dotnet restore LibraTray.slnx
dotnet build LibraTray.slnx -c Release --no-restore
dotnet test LibraTray.slnx -c Release --no-build
```

The test suite should cover:

### Protocol

- request serialization and monotonically unique IDs;
- result, error, and `props` parsing;
- unknown fields and invalid JSON;
- maximum-message rejection;
- one frame split across reads and multiple frames in one read;
- out-of-order ID correlation;
- timeout, cancellation, disconnect, and reconnect.

### Product identity

- trimmed, case-insensitive exact `lamp15` mapping;
- `lamp15` → Yeelight Libra Pro and YLTD003;
- ordinary display never exposes the internal model;
- advanced diagnostics retain it;
- unknown, Libra 2, Pro2, Pura, Pura Pro, and YLTD001 inputs do not map;
- user alias never overwrites original identity;
- clearing an alias or recovering corrupt configuration restores the friendly
  default.

### State and configuration

- channel-independent power, brightness, temperature, and color boundaries;
- throttling/coalescing with a final-value send;
- queue and state merge ordering;
- physical-control notification simulation;
- main-off/ambient-on, main-on/ambient-off, and both-off cases;
- stale/incorrect notification reconciliation;
- configuration read/write, corruption recovery, migration, import/export,
  and absence of account credentials.

### Mock device

The mock binds to loopback and exercises success, delay, silence, errors,
unsolicited `props`, split/coalesced messages, disconnect, restart, independent
channels, misleading notification, and unknown model.

Passing mock tests does not change a protocol item to “device verified.”

## Real-device prerequisites

- Stock Yeelight Libra Pro / YLTD003 hardware that reports `lamp15`;
- firmware version recorded before testing;
- PC and light on the same trusted private LAN;
- LAN control enabled where available;
- other Yeelight LAN clients closed to avoid connection/quota interference;
- no irreplaceable preset or state that a test could disrupt.

Do not test replacement-firmware devices as evidence for the stock protocol.

## Safe test order

1. Run automated tests without hardware.
2. Inspect probe help:

   ```powershell
   dotnet run --project tools/LibraTray.Probe -- --help
   ```

3. Run UDP discovery:

   ```powershell
   dotnet run --project tools/LibraTray.Probe -- discover
   ```

4. Confirm friendly name, YLTD003 mapping, exact raw model, address, device ID,
   firmware, and advertised `support` list.
5. Export a redacted discovery log and manually audit its redaction.
6. Connect and perform only the probe's read-only state query.
7. Compare response fields with physical main/ambient state.
8. Test one documented write operation at a time, only when the method appears
   in the device's advertised capabilities and the probe presents the exact
   JSON for confirmation.
9. Between operations, record response and all notifications, then query state.
10. Test physical-knob actions without simultaneous software commands.
11. Test simultaneous input, reconnect, and reboot only after basic behavior is
    understood.

Use conservative intervals. Yeelight's published ceiling is 60 commands per
minute per connection and 144 LAN commands per minute overall; LibraTray tests
should remain comfortably below it.

## Product-specific matrix to fill

Record every row separately for the tested firmware:

| Area | Evidence to capture | Current status |
| --- | --- | --- |
| discovery address/port and TCP `Location` | raw redacted response | Generic protocol documented; `lamp15` unverified |
| message terminator and request ID | exact request/response bytes | Generic protocol documented; `lamp15` unverified |
| initial main properties | response and physical state | Unverified |
| initial ambient properties | response and physical state | Unverified |
| main power/brightness/temperature | request, result, notification, query | Unverified |
| ambient power/brightness/color | request, result, notification, query | Unverified |
| both-channel power interaction | before/after state | Unverified |
| one channel off, other on | before/after state | Unverified |
| physical-knob updates | notification plus reconciliation query | Unverified |
| reconnect/reboot persistence | ordered timestamps and queries | Unverified |
| malformed/unknown notification behavior | mock first; safe observation only | Unverified |
| firmware-specific defects | repeat count and firmware | Unverified |

Never promote a special method such as a segment-color command from an
open-source lead into production solely because one implementation exposes it.

## Evidence record

Use this template:

```text
Hardware label: YLTD003
Discovery internal model: lamp15
Firmware:
Vendor app and region:
Windows build:
LibraTray commit:
Other LAN clients closed: yes/no
Starting physical state:
Action:
Exact redacted request:
Exact redacted result/error:
Notifications in timestamp order:
Follow-up query and result:
Repeated after reconnect: yes/no/result
Repeated after power cycle: yes/no/result
Conclusion:
Confidence:
```

Observation is not causation: note timing and conflicts instead of assuming
that the last packet caused a physical state.

## Log handling

The probe's default diagnostic directory is:

```text
%LOCALAPPDATA%\LibraTray\logs
```

The tool must print the exact file path after export. A caller-selected output
path may override the default when the current `--help` documents it. Redacted
export remains the default.

Before sharing, search the file for IP/MAC addresses, device IDs, hostname,
username, Wi-Fi name, and absolute paths. Replace remaining values with stable
placeholders so packet relationships remain understandable. Never share
credentials, cloud tokens, or a complete home-network inventory.
