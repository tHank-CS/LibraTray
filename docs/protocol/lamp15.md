# `lamp15` / YLTD003 Protocol Research

Evidence snapshot: 2026-07-28.

## Scope and current status

`lamp15` is the Yeelight LAN internal model mapped by LibraTray to hardware
model YLTD003 and friendly name Yeelight Libra Pro. The identity mapping is a
high-confidence cross-source inference documented in
[product identity research](../research/product-identity.md).

No user-supplied stock-device trace has yet been reviewed. Therefore:

- generic protocol syntax listed in Yeelight's public specification is
  **officially documented**;
- claims that a particular method/property works on `lamp15` are
  **unverified** unless explicitly noted otherwise;
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

At this snapshot there are no “device verified” or “known firmware issue”
entries.

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
| `get_prop` | Officially documented | Home Assistant, python-yeelight, kyuuri | Unverified | Probe read-only; the safe-write path additionally requires `get_prop` in advertised capabilities |
| `set_power` | Officially documented | Multiple generic clients | Unverified | Disabled in product adapter until verified |
| `set_bright` | Officially documented | Multiple generic clients | Unverified | Same |
| `set_ct_abx` | Officially documented | Multiple generic clients | Unverified | Same |
| `toggle` | Officially documented | Multiple generic clients | Unverified | Avoid until toggle semantics are verified |
| `bg_set_power` | Officially documented as a generic background method | Home Assistant, python-yeelight, kyuuri, NumberOneBot | Unverified; notification reliability lead exists | Probe only; reconcile by query during research |
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
| main `power`, `bright`, `ct`, `rgb`, `hue`, `sat`, `color_mode` | Official generic protocol | Unverified on target firmware |
| ambient `bg_power`, `bg_bright`, `bg_ct`, `bg_rgb`, `bg_hue`, `bg_sat`, `bg_lmode` | Official generic background-property model and open-source references | Unverified on target firmware |
| firmware, model, name, support | Official discovery protocol | Exact target response unverified |
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
incorrect status value on YLTD003 firmware. These are **open-source/user-report
leads**, not a confirmed defect in the user's current firmware.

Safe policy until verified:

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

- the exact `support` list by firmware;
- accepted initial-state property set;
- whether main and ambient channels accept every generic `bg_*` method;
- ambient colour model and boundaries;
- both-channel toggle/power semantics;
- physical rotary-control notification timing and completeness;
- reboot persistence;
- bad-notification firmware scope;
- segment support;
- fallback behavior for every unsupported method.

These unknowns block the production Libra Pro adapter and formal UI controls,
but not the safe protocol probe or generic core.
