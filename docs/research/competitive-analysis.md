# Competitive Analysis

Research snapshot: 2026-07-28. Maintenance labels are a point-in-time review of
the upstream history and may change.

Legend: **Yes** means explicitly implemented/documented by the reviewed source;
**Partial** means only part of the requirement; **No** means absent from the
reviewed implementation; **Unverified** means claimed behavior was not
reproduced on local hardware.

## Comparison matrix

| Project | Type / stack | Maintenance at review | Target/support | Stock `lamp15` two-channel | Windows-native desktop | Server/platform/token | Tray / hotkeys | Physical-control sync | License |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| [Home Assistant Yeelight](https://github.com/home-assistant/core/tree/dev/homeassistant/components/yeelight) | Smart-home integration; Python | Active | broad Yeelight; explicit `lamp15`/YLTD003 mapping | Yes in code; hardware unverified here | No | Home Assistant server | No / no | Local push plus corrective re-query | Apache-2.0 |
| [homebridge-yeelight-screen-light-bar](https://github.com/kyuuri10010/homebridge-yeelight-screen-light-bar) | Homebridge plugin; TypeScript | Explicitly deprecated | YLTD003 / `lamp15` | Yes, described as device-tested | No | Homebridge | No / no | Device updates plus re-query | MIT |
| [python-yeelight](https://gitlab.com/stavros/python-yeelight) | Protocol library; Python | Maintained release 0.7.16 (2025-01-28) | broad Yeelight; `lamp15`/ambient support | Yes at library model level; unverified here | No | Embeddable library | No / no | Listener support; host must reconcile | BSD-2-Clause |
| [Ryszard-S/yeelight-GUI](https://github.com/Ryszard-S/yeelight-GUI) | Generic GUI; Python/Tkinter | Stalled around 2021 | generic Yeelight | No | Cross-platform toolkit, not native integration | Standalone LAN | No / no | No persistent push design found | No LICENSE found |
| [BrockDeveloper/y2e-Yeelight-controller](https://github.com/BrockDeveloper/y2e-Yeelight-controller) | Generic GUI; Java/JavaFX | Stalled around 2021 | generic Yeelight | No | No | Standalone LAN | No / no | No | No valid LICENSE found |
| [vanyason/yeelight](https://github.com/vanyason/yeelight) | Desktop GUI; Go/Wails/React | Weak/stalled around 2023 | generic Yeelight, main channel | No | Webview desktop | Standalone LAN | No / no | No continuous push found | WTFPL |
| [dckiller51 ESPHome screen bar](https://github.com/dckiller51/esphome-yeelight-led-screen-light-bar) | Replacement firmware/config; ESPHome | Limited | YLTD001, not YLTD003 | No | No | Home Assistant/ESPHome | No / no | Device-side, different hardware | Apache-2.0 |
| [MiMonitorLightTray](https://github.com/Martlnez/MiMonitorLightTray) | Windows tray utility; Python/pystray | Active at review | Xiaomi monitor light, single channel | No evidence | Windows-oriented, not native UI stack | miIO token required | Yes / yes | Poll/device-specific; not `lamp15` | MIT |
| [NumberOneBot/yeelight-client](https://github.com/NumberOneBot/yeelight-client) | Library and CLI; TypeScript | Active at review | includes `lamp15` claims | Yes in code; hardware unverified here | No | Standalone library/CLI | No / no | `props` handling | MIT |
| [h-kod Yeelight Controller GUI](https://github.com/h-kod/yeelight-controller-gui) | Windows GUI; C#/WinForms | Limited | generic; partial YLTD003 claim | Partial main channel; no complete ambient path | Yes | Standalone LAN | No / no | No complete push reconciliation | MIT |
| [Zooblik YLTD003 ESPHome](https://github.com/Zooblik/Zooblik_Yeelight_Screen-Light-Bar) | Replacement firmware; ESPHome | Limited | YLTD003 hardware and remote | Not stock protocol | No | ESPHome/Home Assistant | No / no | Physical remote handled by replacement firmware | MIT |
| [SR2k Homebridge screen bar](https://github.com/SR2k/homebridge-yeelight-screen-bar-pro) | Homebridge plugin; TypeScript | Limited | screen bar dual channel | Partial; miIO rather than stock LAN evidence | No | Homebridge, IP and token | No / no | Polling | Conflicting: LICENSE Apache-2.0, package metadata MIT |

No reviewed project simultaneously provides:

- stock `lamp15` LAN control with independently verified two-channel behavior;
- reliable physical-knob reconciliation;
- a focused Windows tray, hotkeys, OSD, and lifecycle integration;
- local operation without a server, smart-home platform, or device token.

## Project notes

### Home Assistant

Strengths:

- mature discovery/integration lifecycle;
- two entities for front/background channels;
- local-push listener;
- explicit workaround that re-queries after a suspicious background-power
  notification.

Limitations:

- requires a Home Assistant installation;
- not a compact Windows desktop workflow;
- general integration abstractions do not define LibraTray's interaction model.

Reference only the ideas of capability-driven entities, push-first updates, and
targeted reconciliation. Do not transplant component code.

### homebridge-yeelight-screen-light-bar

Strengths:

- explicitly reports YLTD003 / `lamp15` testing;
- models both channels;
- combines update notifications with state refresh.

Limitations:

- deprecated;
- requires Homebridge;
- no Windows tray or lifecycle integration.

Its strongest value is as a behavior lead for the probe. Every command still
needs exact stock-firmware confirmation.

### python-yeelight

Strengths:

- established generic Yeelight protocol library;
- documented `lamp15`/YLTD003 mapping;
- ambient and notification concepts;
- permissive BSD-2-Clause license.

Limitations:

- not a desktop application;
- host application remains responsible for reconciliation, Windows integration,
  and product-safe naming.

LibraTray follows public protocol semantics but implements its .NET core
independently.

### Generic desktop clients

Ryszard-S, BrockDeveloper, vanyason, and h-kod demonstrate that a simple LAN GUI
is feasible. They do not solve complete `lamp15` dual-channel synchronization,
tray-first Windows interaction, and lifecycle automation together. Ryszard-S
and BrockDeveloper lack a usable explicit repository license, so their code
must not be copied.

### Windows tray comparison

MiMonitorLightTray is the most relevant interaction reference for a lightweight
Windows tray, hotkeys, and power-event workflow. Its miIO-token requirement,
single-channel target, and different hardware/protocol prevent it from serving
as the protocol basis. LibraTray can independently adopt the high-level
principle of immediate tray controls and graceful hotkey failure.

### Replacement-firmware projects

dckiller51 targets YLTD001. Zooblik targets YLTD003 hardware but replaces stock
firmware. These are useful for understanding product differences and physical
remote capabilities, not for claiming behavior of the original Yeelight LAN
firmware.

### Newer `lamp15` leads

NumberOneBot's current TypeScript client provides useful leads for two-channel
properties, `props`, and segment operations. Segment methods absent from the
reviewed official LAN specification remain unverified and must stay in the
probe/research boundary. SR2k's plugin uses token-based polling and has
conflicting repository license declarations; its code is excluded.

## Differentiation

LibraTray is not intended to win on the number of supported bulbs. Its
differentiation is a narrow, testable contract:

1. exact identity rather than retail-name guessing;
2. verified stock YLTD003 behavior rather than generic-bulb assumptions;
3. independently reconciled front and ambient state;
4. physical-knob changes treated as first-class state sources;
5. fast Windows tray controls, hotkeys, optional OSD, and lifecycle events;
6. no smart-home server, cloud login, or device token;
7. redacted diagnostic evidence and an explicit unverified/verified boundary.

## Source-use policy

All reviewed projects were used to compare public behavior and architecture.
No source code was copied. License and provenance details are recorded in
[THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md).
