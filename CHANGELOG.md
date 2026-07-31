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

### Fixed

- `safe-write` now discovers through multicast by default and can bind the UDP
  request/reply socket to a selected physical LAN interface.
- Firmware-38 background-power notifications are documented as provisional:
  an independent background off action may incorrectly notify
  `bg_power=on`, while a follow-up `get_prop` returns the correct state.

### Security

- Diagnostic data is designed to be redacted before export.
- Network parsing is bounded and treats device data as untrusted input.

No version has been released yet. Version headings and compare links will be
added only when corresponding tags exist.
