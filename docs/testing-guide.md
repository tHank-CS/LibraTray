# Testing Guide

## Purpose

Current automated tests establish generic framing/parsing, request correlation,
timeouts/cancellation/disconnect and explicit reconnect, product identity,
probe option/address policy, diagnostic redaction, the Phase-C state engine,
configuration, automation, and mock-device integration. No automated test can
establish how a particular YLTD003 firmware responds to a command or
physical-knob action. This guide keeps those evidence types separate.

Status vocabulary used in protocol documents:

- **officially documented** — stated in Yeelight's public LAN specification;
- **open-source reference** — observed in a named public implementation;
- **device verified** — reproduced on stock YLTD003 hardware with firmware and
  evidence recorded;
- **unverified** — not yet reproduced on the target device;
- **known firmware issue** — reproducible and scoped to recorded firmware;
- **fallback required** — the application must use a safer query/reconnect path.

Product-specific command and physical-knob behavior is considered verified only
where the protocol evidence documents exact hardware, firmware, and results.

## Automated checks

From the repository root:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\restore.ps1 -Locked
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\verify.ps1 -SkipRestore
```

The verification script checks formatting/analyzers, builds Release, runs all
test projects with TRX/Cobertura output, and audits vulnerable dependencies.

Current automated coverage includes:

### Protocol

- request serialization and monotonically unique IDs;
- result, error, and `props` parsing;
- unknown fields and invalid JSON;
- maximum-message rejection;
- one frame split across reads and multiple frames in one read;
- out-of-order ID correlation;
- timeout, cancellation, disconnect, reconnect, and concurrent disposal;
- strict discovery status/header/UTF-8/`Location` validation, bounded property
  queries, and endpoint/ID deduplication;
- default PII redaction plus permanent credential redaction for escaped,
  duplicate, nested, malformed-container, header, and assignment forms.

### Product identity

- trimmed, case-insensitive exact `lamp15` mapping;
- `lamp15` → Yeelight Libra Pro and YLTD003;
- ordinary display never exposes the internal model;
- advanced diagnostics retain it;
- unknown, Libra 2, Pro2, Pura, Pura Pro, and YLTD001 inputs do not map;
- user alias never overwrites original identity;
- clearing an alias or recovering corrupt configuration restores the friendly
  default.

### State and configuration coverage

The current automated suite includes:

- channel-independent power, brightness, temperature, and color boundaries;
- bounded scheduling, throttling/coalescing, retry, and final-state verification;
- queue and state merge ordering;
- physical-control notification simulation;
- main-off/ambient-on, main-on/ambient-off, and both-off cases;
- stale/incorrect notification reconciliation;
- configuration read/write, corruption recovery, migration, import/export,
  presets, automation tickets, and absence of account credentials.

Native tray/window interactions, global registration with the live Windows
desktop, packaged-icon rendering, and MSI install/upgrade/uninstall remain
manual Windows smoke-test responsibilities.

### Mock device

The separately runnable mock binds to loopback and exposes success, delay,
silence, errors, unsolicited `props`, split/coalesced messages, disconnect,
restart, independent channels, misleading notification, and unknown-model
modes. Phase-B integration tests launch the normal, delay, error, no-response,
split, coalesce, `props`, disconnect, restart, and bad-`props` modes. They also
verify per-client oversized-frame isolation and the confirmed safe-write happy
path from discovery through pre-read, write, and post-read.

The current suite does not yet drive the standalone mock with an unknown model,
nor does it cover every post-write timeout/rejection/mismatch path. The mock
covers exact-`lamp15` main-power reconciliation and a `cold-start-silent` mode
where `bg_set_power("on")` returns success without changing readable state.
The production adapter's bounded `bg_set_scene` recovery is exercised over a
real loopback TCP connection. `set_segment_rgb` remains rejected by the parser
and is not implemented by the mock.

Passing a mock smoke or future automated mock test does not change a protocol
item to “device verified.”

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

   On a multi-adapter host, add `--local-address <PC_LAN_IPV4>` to force the
   UDP multicast request and its reply socket onto the intended physical LAN.

4. Confirm friendly name, YLTD003 mapping, exact raw model, address, device ID,
   firmware, and advertised `support` list.
5. Export a redacted discovery log and manually audit its redaction.
6. Connect and perform only the probe's read-only known-property snapshot:

   ```powershell
   dotnet run --project tools/LibraTray.Probe -- inspect --host <DEVICE_IP>
   ```

   The default snapshot contains 17 known generic properties and is sent as two
   sequential `get_prop` requests of at most 15 properties each. Each request
   and response is logged. This does not prove the property set is complete for
   the tested firmware.
7. Compare response fields with physical main/ambient state.
8. Test one documented write operation at a time, only when the method appears
   in the device's advertised capabilities and the probe presents the exact
   JSON for confirmation.
   For exact `lamp15` identity, a `set_power` probe reads
   `power,main_power,bg_power` before and after the command. It verifies
   `main_power` against the requested value, recomputes aggregate `power`, and
   rejects any unexpected change to `bg_power`.
   The current probe still rejects `set_segment_rgb`, even when the device
   advertises it. A future implementation may expose it only through a separate,
   default-off experimental path with explicit risk confirmation, exact
   `lamp15`/firmware/capability gates, before/after dual-channel capture, and a
   whole-background recovery path. Because no segment property is readable,
   physical confirmation is required and requested colours must not be reported
   as confirmed state.
   The exact-`lamp15` `bg_set_scene` path is retained only as a temporary
   recovery diagnostic. It initializes the current running session but does
   not persist across a cold cycle.
9. Between operations, record response and all notifications, then query state.
10. Test physical-knob actions without simultaneous software commands.
11. Test simultaneous input, reconnect, and reboot only after basic behavior is
    understood.

To capture notifications without writing device state:

```powershell
dotnet run --project tools/LibraTray.Probe -- listen --host <DEVICE_IP> --listen-seconds 30
```

Use conservative intervals. Yeelight's published ceiling is 60 commands per
minute per connection and 144 LAN commands per minute overall; LibraTray tests
should remain comfortably below it.

## Product-specific matrix to fill

Record every row separately for the tested firmware:

| Area | Evidence to capture | Current status |
| --- | --- | --- |
| discovery address/port and TCP `Location` | raw redacted response | Verified once on firmware 38; multi-NIC host required explicit local binding |
| message terminator and request ID | exact request/response bytes | Verified for `get_prop`, basic main/background writes, and the now-disabled `set_segment_rgb` on firmware 38 |
| initial main properties | response and physical state | Verified once on firmware 38 |
| initial ambient properties | response and physical state | Verified once on firmware 38 |
| main power/brightness/temperature | request, result, notification, query | `set_power` on/off, `set_bright`, and 5000→4000 K `set_ct_abx` locally verified; the 2700–6500 K native range was confirmed directly with Yeelight by the maintainer on 2026-08-04; off follows the vendor-app ambient-follow policy |
| ambient power/brightness/color | request, result, notification, query | `bg_set_power` off/on, `bg_set_bright`, and `bg_set_rgb` verified; off notification is unreliable |
| ambient left/right segments | exact request, result, physical side mapping | Immediate left/right mapping verified twice on the only test device; eligible for a guarded experimental path, but causality and readable state remain unresolved |
| both-channel power interaction | before/after state | Three power combinations queried on firmware 38 |
| one channel off, other on | before/after state | `main_power`/`bg_power` query semantics verified |
| physical-knob updates | notification plus reconciliation query | Brightness/CT observed; independent background off can notify `bg_power=on` |
| reconnect/reboot persistence | ordered timestamps and queries | Cold-start background failure reproduced; both local and vendor-app scenes restored only the running session; ordinary `bg_set_power` returned `ok` but remained `off` until scene refresh |
| malformed/unknown notification behavior | mock first; safe observation only | Unverified |
| firmware-specific defects | repeat count and firmware | Firmware 38 background-power notification defect observed; cold-start renderer initialization failure reproduced and recoverable with one `bg_set_scene`; segment causality unresolved |

Never promote a special method from an open-source lead or immediate visual
success alone. The segment experiment passed command, ordinary-state readback,
reconnect, and physical side checks, but later shared a test sequence with a
cold-start failure. With one already-exposed device and no control unit, the
relationship cannot be isolated. The maintainer accepts this limitation only
for a clearly labelled, default-off experimental path; it must not be described
as stable or causally proven safe.

## Single-device and cold-start policy

- Only one exact-`lamp15` test device is available. There is no parallel control
  device, so before/after comparisons are single-device observations and cannot
  establish population-wide behavior or causality.
- The target deployment keeps the device powered and online continuously.
  Physical cold starts are rare abnormal events, not a routine gate for every
  ordinary control change.
- Cold-power tests require a task-specific reason and explicit approval. They
  are required when validating a future POST, cold-start recovery, or claimed
  persistence behavior.
- A future POST must be tested independently for trigger accuracy, one-attempt
  bounds, main-channel preservation, background appearance/power restoration,
  visible-flash behavior, and failure reporting.

## Evidence record

Use this template:

```text
Hardware label: YLTD003
Discovery internal model: lamp15
Firmware:
Available target devices: 1
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
Cold-power test required and approved: yes/no/reason
POST invoked and result: yes/no/not applicable/result
Conclusion:
Confidence:
```

Observation is not causation: note timing and conflicts instead of assuming
that the last packet caused a physical state.

## Main colour-temperature range UI check

With the extra-warm / extra-cool option disabled, verify that the quick-panel
slider and colour-temperature shortcuts stop at 3000 K and 6400 K, use the
configured tick interval, and never move in the opposite direction when the
device begins outside that range. Enable the option in Settings and verify that
the same controls expand to 2700–6500 K. Saving, reopening Settings, exporting,
and importing the configuration must preserve the option.

The only available firmware-38 device has visually shown that the cool LED
channel turns off below 3000 K and the warm channel turns off above 6400 K.
Record endpoint observations separately from command success and post-read
state; do not generalize this visual behavior to another firmware without new
device evidence.

## OSD smoke check

From a built application directory, run:

```powershell
.\LibraTray.exe --show --osd-preview
```

The explicit preview displays a 50% main-brightness OSD without issuing an
adjustment command. Verify that it appears near the lower-right corner of the
monitor containing the pointer, does not become the foreground window, ignores
pointer input, and hides after about 1.4 seconds. If Windows blocks pointer
position access, placement falls back to the foreground window's monitor and
then the primary work area. Normal OSD feedback is shown only after a shortcut
or tray-wheel adjustment completes without a reported device error, and it can
be disabled in Settings.

## Log handling

The probe's default diagnostic directory is:

```text
%LOCALAPPDATA%\LibraTray\logs
```

The tool must print the exact file path after export. A caller-selected output
path may override the default when the current `--help` documents it.
`--log-path` must identify a new, nonexistent file; the probe refuses to append
to or overwrite an existing file. Parent directories may be created. Redacted
export remains the default.

Each JSONL file has a 10 MiB hard cap. When the cap is reached, logging stops
with a warning while the network operation may continue. Phase B does not
rotate or delete old logs automatically; review and remove files manually when
they are no longer needed.

Before sharing, search the file for IP/MAC addresses, device IDs, hostname,
username, Wi-Fi name, and absolute paths. Replace remaining values with stable
placeholders so packet relationships remain understandable. Never share
credentials, cloud tokens, or a complete home-network inventory.
