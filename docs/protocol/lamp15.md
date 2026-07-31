# `lamp15` / YLTD003 Protocol Research

Evidence snapshot: 2026-07-31.

## Scope and current status

`lamp15` is the Yeelight LAN internal model mapped by LibraTray to hardware
model YLTD003 and friendly name Yeelight Libra Pro. The identity mapping is a
high-confidence cross-source inference documented in
[product identity research](../research/product-identity.md).

One redacted real-device session on firmware 38 has now been reviewed.
Therefore:

- generic protocol syntax listed in Yeelight's public specification is
  **officially documented**;
- only the exact properties and operations recorded below are device-observed;
- no production `lamp15` adapter exists yet, and the probe exposes no
  `lamp15`-specific private method;
- a method advertised in `support` is eligible for a careful probe, not
  automatically trusted as semantically correct.

## Evidence labels

| Label | Meaning |
| --- | --- |
| Officially documented | Present in Yeelight's public generic LAN specification |
| Open-source reference | Present in a named, licensed public implementation |
| Device verified | Reproduced on stock YLTD003 with firmware and redacted trace |
| Unverified | Not yet reproduced on the target hardware |
| Known firmware issue | Reproduced and scoped to a firmware version |
| Fallback required | Must reconcile by query/reconnect rather than trust one signal |

The evidence is one device/session rather than a firmware-wide compatibility
claim. Reconnect, power-cycle, region, and other firmware variants remain
unverified.

## Firmware 38 session evidence

The 2026-07-31 session established:

- multicast discovery reported exact model `lamp15`, firmware `38`, TCP port
  55443, `get_prop`, generic main commands, and documented `bg_*` capabilities;
- the default 17-property snapshot succeeded in two requests of at most 15
  properties;
- custom queries confirmed all three channel combinations: both on, main only,
  and background only;
- `power` remained `on` whenever either channel was on, while `main_power` and
  `bg_power` represented the independent channels;
- physical main brightness and colour-temperature changes produced prompt
  partial `props` notifications;
- an independent background off action could incorrectly notify
  `bg_power=on`; a follow-up `get_prop` returned the correct `off` value;
- confirmed `set_bright(50, "sudden", 0)` changed the physical main light,
  returned `["ok"]`, emitted `bright=50`, and passed a write-after-read query.

The Windows host had multiple physical, VPN/tunnel, Hyper-V, WSL, and VMware
interfaces. Binding discovery to the physical LAN IPv4 made multicast
discovery reliable. No device address, persistent ID, or user-supplied name is
stored in this document.

## Generic control channel

The official protocol defines a TCP stream of CRLF-terminated JSON objects:

```json
{"id":1,"method":"get_prop","params":["power","bright"]}
```

followed by `\r\n`. A result echoes the integer request ID:

```json
{"id":1,"result":["on","100"]}
```

A notification is independent of a request:

```json
{"method":"props","params":{"power":"on","bright":"100"}}
```

The property example is generic, not a YLTD003 capture. Responses and
notifications may be interleaved, split across reads, or coalesced in one
read. Values in `props` are documented as partial updates and commonly strings.

## Command research matrix

| Area / method | Generic source status | Open-source `lamp15` lead | Stock YLTD003 status | Production policy |
| --- | --- | --- | --- | --- |
| `get_prop` | Officially documented | Home Assistant, python-yeelight, kyuuri | Verified in one firmware-38 session | Production adapter may query only the verified bounded property set and must tolerate empty values |
| `set_power` | Officially documented | Multiple generic clients | Unverified | Disabled in product adapter until verified |
| `set_bright` | Officially documented | Multiple generic clients | Verified once on firmware 38 with notification and post-read | Eligible for production adapter after repeat/reconnect coverage |
| `set_ct_abx` | Officially documented | Multiple generic clients | Unverified | Same |
| `toggle` | Officially documented | Multiple generic clients | Unverified | Avoid until toggle semantics are verified |
| `bg_set_power` | Officially documented as a generic background method | Home Assistant, python-yeelight, kyuuri, NumberOneBot | Capability advertised; write unverified; notification defect confirmed | Probe only; reconcile by query during research |
| `bg_set_bright` | Officially documented as a generic background method | Same sources | Unverified | Probe only |
| `bg_set_rgb` / `bg_set_hsv` | Officially documented as generic background methods | Same sources | Unverified | Probe only; validate colour model/ranges |
| `bg_set_ct_abx` | Officially documented as a generic background method | Generic libraries | Unverified | Do not assume ambient channel supports CT |
| `bg_toggle` / `dev_toggle` | Officially documented generically | Some libraries | Unverified | Avoid until both-channel semantics are measured |
| `set_segment_rgb` or similar segment extension | Not present in reviewed official LAN specification | NumberOneBot lead | Unverified | Research only; no production/UI exposure |
| Chroma UDP R2/token commands | Separate Yeelight Chroma material | Yeelight Chroma Connector | Not the standard LAN control path | Explicitly out of the LAN adapter |

Parameters are taken only from the official specification or the probe's
evidence record. LibraTray does not invent parameter order or default values.

## Property research matrix

| Property group | Generic documentation/reference | `lamp15` status |
| --- | --- | --- |
| main `power`, `main_power`, `bright`, `ct`, `rgb`, `hue`, `sat`, `color_mode` | Official generic protocol plus device-observed `main_power` | Queried on firmware 38; empty inactive colour fields retained as empty |
| ambient `bg_power`, `bg_bright`, `bg_ct`, `bg_rgb`, `bg_hue`, `bg_sat`, `bg_lmode` | Official generic background-property model and open-source references | Queried on firmware 38 |
| firmware, model, name, support | Official discovery protocol | Exact model, firmware, endpoint, and capability list observed on firmware 38 |
| segment fields/indices | Open-source lead only | Unverified; do not formalize |

Unknown properties are retained as diagnostic data and ignored by domain
mapping until typed and verified. An empty `get_prop` result may mean unknown
property under the generic specification; it is not coerced to zero or off.

The phase-B probe's default known-property snapshot requests these 17 names:

```text
power, bright, ct, rgb, hue, sat, color_mode, name, model, fw_ver,
bg_power, bg_bright, bg_ct, bg_rgb, bg_hue, bg_sat, bg_lmode
```

It sends at most 15 property names in one `get_prop` request, so the default
snapshot is split into two sequential, separately logged requests. A custom
`--props` list is partitioned the same way. This is a bounded snapshot of known
generic property names, not proof that the list is complete for every YLTD003
firmware; unsupported properties may return empty values or an error.

## Notification reliability lead

Home Assistant contains a targeted re-query when a background-power update is
suspect, and an older
[Yeelight forum report](https://forum.yeelight.com/t/topic/23184) describes an
incorrect status value on YLTD003 firmware. The same class of defect is now
confirmed in the firmware-38 session: independent background off and on
actions could both notify `bg_power=on`, while the subsequent query returned
the physically correct value.

Required policy:

1. record the exact notification, source connection, sequence, and timestamp;
2. update UI only with an “unconfirmed” confidence where appropriate;
3. perform one debounced reconciliation query;
4. prefer a valid query result over a conflicting older notification;
5. record both values and firmware in diagnostics.

## Rate and connection constraints

The official generic limits are:

- four simultaneous TCP connections;
- 60 command messages per minute on one connection;
- 144 LAN command messages per minute overall.

The phase-B core provides one receive loop per connection, request correlation,
timeouts, cancellation, disconnect handling, and an explicit reconnect
operation. It does not yet implement production command-rate scheduling, slider
coalescing/final-value delivery, or automatic reconnect backoff. Those are
Phase-C state/application responsibilities and must remain below the published
ceilings when implemented. Actual `lamp15` limit error behavior is unverified.

## Probe acceptance criteria

A behavior may become “device verified” only when the record includes:

- hardware label YLTD003 and discovery model `lamp15`;
- firmware and region/app context;
- exact redacted request bytes;
- exact result/error and notification order with timestamps;
- physical before/after state for both channels;
- a follow-up state query;
- a repeat after reconnect, and where safe after power cycle;
- failure behavior and command interval.

One successful response is not enough to establish notification reliability,
persistence, or firmware independence.

## Explicit unknowns

- `support` and initial properties on firmware versions other than 38;
- whether main and ambient channels accept every generic `bg_*` method;
- ambient colour model and boundaries;
- both-channel toggle/power semantics;
- repeatability of physical rotary-control notifications after reconnect and
  power cycle;
- reboot persistence;
- bad-notification scope outside the observed firmware-38 session;
- segment support;
- fallback behavior for every unsupported method.

These unknowns block the production Libra Pro adapter and formal UI controls,
but not the safe protocol probe or generic core.
