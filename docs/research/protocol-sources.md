# Protocol Sources and Evidence Policy

Research snapshot: 2026-07-28.

## Evidence hierarchy

LibraTray records evidence per behavior, not per device name:

1. **Officially documented** — public Yeelight specification or manufacturer
   material.
2. **Device verified** — reproducible observation on stock YLTD003 hardware,
   with firmware, exact redacted traffic, and repetition recorded.
3. **Open-source reference** — a behavior found in an identified public
   implementation under a reviewed license.
4. **Inference** — a proposed interpretation joining other evidence.
5. **Unverified** — not reproduced on the target device.

An open-source implementation is a research lead. It does not become a
production command until confirmed by official documentation or safe,
repeatable real-device tests.

## Primary protocol specification

[Yeelight WiFi Light Inter-Operation Specification](https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf)
is the primary public source for the generic LAN protocol.

It documents:

- UDP M-SEARCH discovery at `239.255.255.250:1982`;
- a unicast discovery response with `Location`, `id`, `model`, `fw_ver`,
  `support`, state, and `name` fields;
- a TCP control endpoint taken from the `yeelight://` `Location` URI (the
  examples use port 55443);
- JSON command, result/error, and notification messages terminated by CRLF;
- integer request IDs echoed in results for correlation;
- `props` notifications containing partial string-valued property updates;
- generic main and background (`bg_*`) methods;
- capability gating through the discovery `support` field;
- up to four simultaneous TCP connections, 60 command messages per minute per
  connection, and 144 LAN commands per minute in total.

The specification does **not** name `lamp15`, guarantee that every listed
method is supported by that model, define all YLTD003 physical-knob behavior,
or establish notification correctness for every firmware. Those details remain
unverified until probed.

## Product and identity sources

See [product identity research](product-identity.md). The identity conclusion
uses:

- the official store and brochure for retail names and YLTD003;
- the official Japanese-hosted Libra Pro manual asset for regional naming and
  a `lamp15-...` setup identifier;
- Yeelight's public Chroma Connector source for another official `lamp15`
  device check;
- Home Assistant and python-yeelight tables as independent corroboration.

The Chroma Connector also contains a separate R2/broadcast/token-oriented
integration. LibraTray does not mix that traffic with the published Yeelight
LAN protocol.

## High-value implementation references

| Source | Evidence value | Constraint |
| --- | --- | --- |
| [Home Assistant Yeelight integration](https://github.com/home-assistant/core/tree/dev/homeassistant/components/yeelight) | model mapping, two logical lights, local push listener, reconciliation of a suspicious background-power notification | requires Home Assistant; implementation behavior is not hardware proof |
| [homebridge-yeelight-screen-light-bar](https://github.com/kyuuri10010/homebridge-yeelight-screen-light-bar) | explicitly tested YLTD003/`lamp15`, dual-channel model, update/requery pattern | deprecated; Homebridge environment |
| [python-yeelight](https://gitlab.com/stavros/python-yeelight) | generic LAN framing/API, ambient capabilities, push listener | library rather than Windows application |
| [NumberOneBot/yeelight-client](https://github.com/NumberOneBot/yeelight-client) | current TypeScript/CLI exploration of `lamp15`, ambient properties, and notifications | special segment methods are not in the reviewed official LAN spec and remain unverified |
| [h-kod/yeelight-controller-gui](https://github.com/h-kod/yeelight-controller-gui) | C#/WinForms LAN-control comparison | partial main-channel YLTD003 claim; no complete ambient/push/tray design |
| [Yeelight forum report](https://forum.yeelight.com/t/topic/23184) | historical lead that a YLTD003 firmware notification may report a wrong value | user report, old firmware, not a specification or current-device verification |

The complete comparison and license review is in
[competitive analysis](competitive-analysis.md) and
[THIRD_PARTY_NOTICES](../../THIRD_PARTY_NOTICES.md).

## Clean-room implementation rule

LibraTray code is independently written from:

- official protocol semantics;
- public product documentation;
- named, licensed implementation behavior used only as a cross-check;
- the user's own redacted, reproducible hardware observations.

No source code was copied from any reviewed project. Code with no explicit
license, conflicting license metadata, incompatible obligations, or unclear
provenance must not be copied. Closed-source decompilation and leaked firmware
are out of scope.

## Open questions for the probe

The following require stock YLTD003 testing:

- exact discovery fields and advertised `support` methods by firmware;
- initial-state property names for both channels;
- accepted main/ambient power, brightness, temperature, and colour methods;
- the semantics of global/both-channel operations;
- physical-knob notification fields and order;
- whether background-power notifications can be stale or wrong;
- query versus notification conflict resolution;
- disconnect/reboot behavior and persistence;
- real rate-limit response behavior;
- regional firmware variation;
- any segment command or private extension.

Until those results exist, [lamp15 protocol documentation](../protocol/lamp15.md)
must label these behaviors `unverified`.
