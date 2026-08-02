# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project intends to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) once releases begin.

## [Unreleased]

### Added

- Phase-A product-identity, protocol, competitive, architecture, and licensing
  research.
- Phase-B Yeelight LAN protocol probe and minimal protocol core.
- Product identity mapping for `lamp15`, YLTD003, and the friendly name
  Yeelight Libra Pro.
- Initial automated protocol, identity, probe-option, address-policy, and
  diagnostic-redaction tests, plus a separately runnable mock-device surface.
- Open-source governance, security, privacy, testing, and release
  documentation.
- Explicit local-IPv4 binding for reliable Yeelight multicast discovery on
  Windows hosts with VPN, tunnel, Hyper-V, WSL, VMware, or multiple NICs.
- A redacted firmware-38 YLTD003 evidence record covering exact identity,
  two-channel reads, physical notifications, and verified `set_bright`,
  `bg_set_bright`, and `bg_set_power` operations.
- Firmware-38 evidence for `set_segment_rgb`, including verified left/right RGB
  ordering and a cold-start ambient-output failure observed in the same test
  sequence. The causal relationship remains unresolved, so the command stays
  disabled.
- A capability-gated `bg_set_scene` recovery probe for exact `lamp15` devices,
  explicitly documented as temporary recovery rather than a persistent fix.
- The initial Libra Pro adapter core, including exact identity/capability
  gating, complete two-channel state validation, one recovery attempt per
  connection epoch, and restoration of confirmed background appearance.
- A `cold-start-silent` mock mode and real-TCP integration coverage for the
  firmware-38 recovery path.
- An initial .NET 10 WPF tray application shell with native
  `Shell_NotifyIcon` integration, a compact offline quick panel, explicit
  unavailable states, tray-first startup, and graceful shutdown.
- Trusted multi-interface LAN discovery, an exact-`lamp15` device session, and
  live main/background controls in the quick panel.
- Verified command post-read, physical-notification reconciliation, bounded
  retries, a 500 ms request interval, and a rolling 55-command-per-minute
  ceiling below the published per-connection limit.
- Firmware-38 UI limits including a 3000–6500 K main colour-temperature range,
  whole-background RGB swatches, and click-or-drag slider commits.
- Fixed global shortcuts for main/background power, main brightness, and main
  colour temperature, including conflict reporting and no-repeat registration.
- Single-instance protection and queued shortcut input evaluated from the
  latest confirmed device state.
- Versioned local settings with atomic replacement, corruption fallback,
  device alias, adjustment steps, and captured shortcut rebinding.
- Up to 20 local dual-channel presets, custom whole-background RGB input, and
  verified preset application with cold-start recovery integration.
- A state-aware tray menu with independent channel switches, all-on/off,
  presets, refresh/reconnect, settings, safe device information, and explicit
  exit.
- Immediate left-click panel toggling, middle-click main-power control, and an
  optional coalesced tray-wheel brightness gesture.
- Individually opt-in Windows lock/display automation, current-user startup
  registration, and guarded shutdown/startup restore using a short-lived,
  exact-device, one-time state ticket.
- An advanced device-details and diagnostics window with live confirmed state,
  firmware/capability metadata, masked local identifiers, and a bounded copied
  summary that redacts names, device ID, and LAN endpoint.
- Versioned settings import/export with bounded UTF-8 input, schema validation,
  a pre-apply summary, explicit save, privacy warning, and atomic destination
  replacement.
- Light, dark, and system-following themes with live resource updates, plus
  system-following, Simplified Chinese, and English interface languages.
- An optional no-activate, click-through OSD for confirmed shortcut and
  tray-wheel brightness/colour-temperature feedback, with active-monitor
  placement and automatic hiding.

### Fixed

- `safe-write` now discovers through multicast by default and can bind the UDP
  request/reply socket to a selected physical LAN interface.
- Firmware-38 background-power notifications are documented as provisional:
  an independent background off action may incorrectly notify
  `bg_power=on`, while a follow-up `get_prop` returns the correct state.
- A transient reconciliation failure no longer marks a still-connected device
  offline, and rapid input no longer amplifies into an unbounded request burst.
- A duplicate application instance no longer causes every global shortcut to
  appear occupied, and shortcuts pressed during verification are no longer
  silently discarded.

### Security

- Diagnostic data is designed to be redacted before export.
- Network parsing is bounded and treats device data as untrusted input.

No version has been released yet. Version headings and compare links will be
added only when corresponding tags exist.
