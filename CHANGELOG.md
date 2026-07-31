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
  retries, and a 1.1-second per-connection request interval below the published
  60-command-per-minute ceiling.
- Firmware-38 UI limits including a 3000–6500 K main colour-temperature range,
  whole-background RGB swatches, and click-or-drag slider commits.

### Fixed

- `safe-write` now discovers through multicast by default and can bind the UDP
  request/reply socket to a selected physical LAN interface.
- Firmware-38 background-power notifications are documented as provisional:
  an independent background off action may incorrectly notify
  `bg_power=on`, while a follow-up `get_prop` returns the correct state.
- A transient reconciliation failure no longer marks a still-connected device
  offline, and rapid input no longer amplifies into an unbounded request burst.

### Security

- Diagnostic data is designed to be redacted before export.
- Network parsing is bounded and treats device data as untrusted input.

No version has been released yet. Version headings and compare links will be
added only when corresponding tags exist.
