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
- the application adapter supports only exact `lamp15`; the diagnostic probe
  continues to reject direct use of the private segmented-colour method;
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

The project has one target test device and no same-model control device. The
evidence is therefore a single-device observation rather than a firmware-wide
compatibility or causality claim. Reconnect, region, and other firmware
variants remain unverified. Physical cold starts are treated as rare abnormal
events because the intended deployment keeps the device powered continuously.

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
- confirmed `bg_set_bright(50, "sudden", 0)` changed the physical background
  light from 100 to 50, returned `["ok"]`, emitted `bg_bright=50`, and passed
  a write-after-read query;
- confirmed `set_ct_abx(4000, "sudden", 0)` changed the physical main light
  from 5000 K to a visibly warmer 4000 K, returned `["ok"]`, emitted
  `ct=4000`, and passed a write-after-read query without affecting the
  background light;
- confirmed `bg_set_rgb(65280, "sudden", 0)` changed the physical background
  light to green, returned `["ok"]`, emitted `bg_rgb=65280`, `bg_hue=120`,
  and `bg_sat=100`, and passed a write-after-read query without affecting the
  main light;
- confirmed `bg_set_power("off", "sudden", 0)` independently turned the
  background channel off while the main channel remained on. The command
  returned `["ok"]`, but emitted the incorrect notification
  `bg_power=on`; the post-read correctly returned `off`;
- confirmed `bg_set_power("on", "sudden", 0)` restored the background channel,
  returned `["ok"]`, emitted `bg_power=on`, and passed the post-read;
- confirmed `set_power("on", "sudden", 0)` changed a background-only state
  from `on/off/on` to `on/on/on`, emitted `main_power=on`, and preserved the
  already-on background channel;
- the Xiaomi Home setting that makes the ambient light follow the main-light
  switch changes `set_power("off")` semantics: with following enabled both
  channels became off; with following disabled the result was
  `power=on,main_power=off,bg_power=on`;
- confirmed the advertised experimental extension
  `set_segment_rgb(leftRgb, rightRgb)` twice over separate TCP connections.
  `[16711680,255]` produced a red left zone and blue right zone, while
  `[255,16711680]` reversed them. Both requests returned `["ok"]` and left
  main power, brightness, and colour temperature unchanged;
- no segment-specific property or notification was observed. The ordinary
  `bg_rgb`, `bg_hue`, and `bg_sat` properties retained the previous whole-
  background values after a segmented write;
- after the segment experiments, subsequent cold power cycles left the
  background channel accepting commands while producing no visible light.
  In the final clean cycle, `bg_set_power("on")` returned `["ok"]` but the
  immediate query still returned `bg_power=off`. Main-light control remained
  functional;
- `bg_set_scene("color", 13395711, 50)` also restored visible output and manual
  control in the running session, but the background failed again after the
  next cold power cycle. It is therefore only a temporary recovery aid, not a
  repair for the persistent state;
- on 2026-07-31, the bounded application recovery sequence (scene
  initialization, appearance verification, and return to ordinary background
  control) was physically confirmed to restore visible output and manual
  control for the affected running session;
- a Xiaomi Home scene likewise restored the running session but did not survive
  the next cold cycle. No clean pre-experiment cold-cycle baseline or factory
  reset was performed, so the evidence does not establish whether segment
  control caused the firmware state or merely preceded an independent firmware
  defect;
- on 2026-08-04, the maintainer explicitly accepted proceeding with a guarded
  experimental implementation despite the unresolved cold-start causality and
  single-device evidence limit. This changes the product risk decision, not the
  historical evidence or its confidence. Version 1.1.0 implements that
  default-off guarded path without promoting the evidence to stable status.
- on 2026-08-09, after applying distinct left/right colours and performing a
  cold start, the ambient renderer no longer remained silent and ordinary
  program control worked. However, each ambient power-on slowly restored a
  whole-background `#CC66FF` appearance at 50% brightness. The same appearance
  returned after an independent physical remote off/on cycle, so the repeated
  restoration is device-side rather than a local preset or notification write.
  No local preset existed and the last requested segment colours were different.
  The observation is consistent with a persistent scene/template mode, but the
  initiating write remains unverified: the segment command itself and a later
  automatic `bg_set_scene` recovery were not traced separately in that cycle.
- on 2026-08-10, a power-only Windows lock entry turned both channels off
  cleanly while segmented colour was active. The former full-target unlock
  path then left the main light off and repeatedly alternated the background
  between the persistent `#CC66FF` template and the requested zones before
  ending in a single colour. The automation log confirmed an 11-second bounded
  retry sequence ending in state-verification failure. This is device evidence
  that lifecycle restoration must not replay the complete preset sequence.
- the same main-off and whole/segment oscillation was then reproduced when
  applying a segmented local preset. Presets and startup-ticket restoration
  must therefore use the same no-complete-sequence-replay boundary; this is not
  limited to WTS lifecycle events.

On 2026-08-04, the maintainer confirmed directly with Yeelight that the native
main-light colour-temperature range for Libra Pro / `lamp15` is 2700–6500 K.
This is product-specific official-support evidence. Local writes still use
post-read verification; the earlier 3000–6500 K hardware session remains the
recorded local command sample rather than the product limit.

On the only available firmware-38 test device, the maintainer also observed
that the cool LED channel turns fully off below 3000 K and the warm LED channel
turns fully off above 6400 K. This is a device observation, not an official
protocol guarantee. LibraTray therefore keeps its normal interactive range at
3000–6400 K and exposes 2700–2999 K / 6401–6500 K only through an explicit,
default-off setting. The adapter retains the full native validation range so
presets, restoration, and confirmed device state are not misrepresented.

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
| `set_power` | Officially documented | Multiple generic clients | On and off verified on firmware 38; off behavior depends on the vendor-app ambient-follow setting | Use three-property pre/post reconciliation; do not assume the background remains unchanged when follow mode is enabled |
| `set_bright` | Officially documented | Multiple generic clients | Verified once on firmware 38 with notification and post-read | Eligible for production adapter after repeat/reconnect coverage |
| `set_ct_abx` | Officially documented; maintainer confirmed the Libra Pro product floor directly with Yeelight on 2026-08-04 | Multiple generic clients | 5000 K to 4000 K locally verified; Yeelight-confirmed native range is 2700–6500 K | Production adapter permits 2700–6500 K and requires post-read verification |
| `toggle` | Officially documented | Multiple generic clients | Unverified | Avoid until toggle semantics are verified |
| `bg_set_power` | Officially documented as a generic background method | Home Assistant, python-yeelight, kyuuri, NumberOneBot | Off/on verified once on firmware 38; off notification defect confirmed | Eligible only with mandatory post-notification query reconciliation |
| `bg_set_bright` | Officially documented as a generic background method | Same sources | Verified once on firmware 38 with notification and post-read | Eligible for production adapter after repeat/reconnect coverage |
| `bg_set_rgb` / `bg_set_hsv` | Officially documented as generic background methods | Same sources | `bg_set_rgb` green verified on firmware 38; `bg_set_hsv` unverified | RGB is eligible for the production adapter; HSV remains probe-only |
| `bg_set_ct_abx` | Officially documented as a generic background method | Generic libraries | Unverified | Do not assume ambient channel supports CT |
| `bg_toggle` / `dev_toggle` | Officially documented generically | Some libraries | Unverified | Avoid until both-channel semantics are measured |
| `bg_set_scene` | Officially documented as a generic background method | Generic libraries | A colour scene restored visible output in each affected running session; ordinary writes then worked, but the next cold cycle required recovery again | Production compatibility fallback only after verified ordinary-write failure; one attempt per connection epoch, preserve main state, restore cached appearance and desired power |
| `set_segment_rgb` | Not present in reviewed official LAN specification | NumberOneBot lead | Left/right order worked twice on the only test device; a cold-start defect was later reproduced, but causality cannot be isolated without a clean control | Eligible only for an explicitly enabled experimental path on exact `lamp15`; default off, never claim readable segment state, preserve main state, and provide whole-background recovery |
| Chroma UDP R2/token commands | Separate Yeelight Chroma material | Yeelight Chroma Connector | Not the standard LAN control path | Explicitly out of the LAN adapter |

Parameters are taken only from the official specification or the probe's
evidence record. LibraTray does not invent parameter order or default values.

### Experimental segment command policy

Version 1.1.0 exposes `set_segment_rgb(leftRgb, rightRgb)` only through
the Libra Pro adapter. It requires exact `lamp15`, an advertised capability,
the default-off user setting, and a firmware gate. Firmware 38 is the recorded
device-evidence target; another known firmware requires a remembered warning,
while an unknown firmware permits only a session-confirmed manual operation and
is never replayed at startup.

The adapter snapshots ordinary readable state before the command and queries it
again afterwards. Success means only that the command returned `ok` and did not
change main power/appearance or ambient power/brightness. The left and right
colours remain local requested values and are never inserted into
`LibraProState`. Ordinary reconnect never replays them. A Windows `--startup`
launch may replay one remembered request after the device is ready; shutdown
restore takes precedence.

The manual ambient POST sends one
`bg_set_scene("color", currentWholeRgb, 1)` initialization, restores the prior
brightness and power, then restores either the whole RGB or the last segmented
request. It is serialized and bounded, preserves the main channel, warns about
a possible minimum-brightness flash, and remains a runtime recovery rather than
a firmware repair.

For isolation testing, `--segment-isolation-diagnostics` disables every
automatic verified-failure `bg_set_scene` path and pauses Windows lifecycle and
startup replay writes in memory, while retaining manual ordinary power,
whole-colour, segmented-colour, and explicitly invoked POST operations. It logs
only bounded command methods, parameters, results, failure types, and elapsed
times to `%LOCALAPPDATA%\LibraTray\logs\segment-isolation.log`; discovery
identity and network endpoints are not included. A mode header explicitly
records that automatic scene recovery and Windows automation writes were disabled.

## Property research matrix

| Property group | Generic documentation/reference | `lamp15` status |
| --- | --- | --- |
| main `power`, `main_power`, `bright`, `ct`, `rgb`, `hue`, `sat`, `color_mode` | Official generic protocol plus device-observed `main_power` | Queried on firmware 38; empty inactive colour fields retained as empty |
| ambient `bg_power`, `bg_bright`, `bg_ct`, `bg_rgb`, `bg_hue`, `bg_sat`, `bg_lmode` | Official generic background-property model and open-source references | Queried on firmware 38 |
| firmware, model, name, support | Official discovery protocol | Exact model, firmware, endpoint, and capability list observed on firmware 38 |
| segment fields/indices | Open-source lead plus firmware-38 writes | Two fixed zones were observed on the only test device and no readable segment property exists; values can only be represented as last requested, not confirmed state |

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
actions both notified `bg_power=on`, making the transition indistinguishable
from the notification alone. The subsequent query returned the physically
correct `off` or `on` value.

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

The production session provides one receive loop, request correlation,
timeouts, cancellation, bounded reconnect support, a serialized 500 ms request
interval, and a rolling ceiling of 55 messages per minute. Notification reads
are coalesced and redundant write notifications are absorbed by mandatory
post-read verification. Rapid unlimited requests caused firmware 38 to stop
responding temporarily; stopping the client restored immediate read-only
responses. The exact device-side limit error behavior remains unverified.

## Probe acceptance criteria

A behavior may become “device verified” only when the record includes:

- hardware label YLTD003 and discovery model `lamp15`;
- firmware and region/app context;
- exact redacted request bytes;
- exact result/error and notification order with timestamps;
- physical before/after state for both channels;
- a follow-up state query;
- a repeat after reconnect;
- a cold-power repeat only when evaluating POST, cold-start recovery, or
  persistence behavior under an explicitly approved test;
- failure behavior and command interval.

One successful response is not enough to establish notification reliability,
persistence, or firmware independence.

## Explicit unknowns

- `support` and initial properties on firmware versions other than 38;
- whether main and ambient channels accept every generic `bg_*` method;
- ambient HSV/CT behavior and boundary behavior;
- both-channel toggle/power semantics;
- repeatability of physical rotary-control notifications after reconnect and
  power cycle;
- whether the cold-start renderer failure occurs on a clean device that has
  never received a segment write; this cannot be isolated with the one
  currently available device, which has already received segment writes;
- bad-notification scope outside the observed firmware-38 session;
- fallback behavior for every unsupported method.

These unknowns block describing segmented RGB as stable, causally isolated, or
protocol-confirmed. Under the explicit 2026-08-04 risk decision, they do not
block a default-off experimental path that exposes the evidence limitation,
preserves rollback, and does not represent requested segment colours as
confirmed device state.
