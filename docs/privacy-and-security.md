# Privacy and Security

## Summary

LibraTray is designed to control a light directly on a trusted local network.
It does not require a Xiaomi/Mi Home account, Yeelight account, password,
device token, or cloud login. No telemetry, automatic log upload, analytics
service, public listener, or local web server is part of the design.

The current milestone is a protocol probe and minimal core, not a production
desktop application. Statements below marked “design requirement” describe
controls that every later component must satisfy.

## Trust boundary

Yeelight's published LAN protocol uses UDP discovery and a TCP JSON control
channel and may communicate in plaintext without peer authentication. Anyone
with suitable access to the same network may be able to observe, spoof, or
interfere with traffic. LibraTray cannot make an untrusted LAN trustworthy.

Use the project only when:

- the router and Wi-Fi access point are administered by someone you trust;
- WPA2 or WPA3 and a strong passphrase protect wireless access;
- guest/client isolation and VLAN rules are understood;
- the Windows network is marked Private only when it truly is private;
- no router port-forward exposes the device or LibraTray traffic to the
  Internet.

Do not use the probe on public Wi-Fi, hotel networks, or an untrusted shared
LAN.

## Data processed locally

The application may process:

- local device IP address and TCP port;
- protocol device ID, internal model, reported name, capabilities, firmware,
  and current light state;
- a user-selected alias, manual address, shortcuts, presets, and automation
  settings;
- timestamps and bounded diagnostic events.

These data stay on the local machine unless the user explicitly exports and
shares a file. LibraTray does not need unrelated personal information.

Configuration fields keep friendly product name, hardware model, internal
model, reported name, and user alias separate. Configuration export must warn
that it can contain local-network metadata.

## Diagnostic logs

Shareable logs are redacted by default. Redaction covers at least:

- IPv4 and IPv6 addresses;
- MAC addresses and device IDs;
- hostnames and Wi-Fi network names;
- Windows usernames and user-profile paths;
- other absolute local paths;
- identifiers with equivalent privacy impact.

Firmware version, internal model, command method, response class, timing, and
error category normally remain because they are useful for diagnosis. Review
every exported file manually before publication.

An advanced raw-log mode may exist for local debugging, but it must:

- display a prominent privacy warning;
- apply only to the current run unless explicitly re-enabled;
- never upload or copy data automatically;
- label its output as sensitive;
- use bounded file size and retention.

No password, cloud token, or account credential should ever be logged, even in
raw mode.

## Network-input requirements

Device and network data are untrusted. Implementations must:

- validate response start lines, headers, `Location` scheme, IP, and port;
- cap discovery datagrams, buffered bytes, JSON frame length, nesting, and log
  entry length;
- parse JSON as data and never execute a returned command, path, or code;
- tolerate unknown fields but reject invalid required types;
- separate unsolicited notifications from request-ID responses;
- use connect/read/command timeouts and cooperative cancellation;
- cap concurrent connections and stay below published command quotas;
- close sockets and event registrations deterministically;
- bind mock/test services to loopback.

Manual addresses may target only valid local endpoints by default. A later
escape hatch for unusual routed LANs must be explicit and warn the user.

## Local storage

Design requirements:

- store data in the current user's application-data directory;
- use user-only access where the platform supports it;
- write configuration atomically and recover safely from corruption;
- rotate and bound logs;
- never require administrator privileges;
- never store Xiaomi/Yeelight credentials, cloud tokens, or Wi-Fi passwords;
- do not place real addresses or logs in the repository or build artifacts.

Configuration import is validated before replacing current settings. Unknown
fields are preserved or ignored according to schema policy; they never become
executable input.

## Windows integration

Tray, hotkey, session, and power integration requires Windows message handling,
not elevated privileges. The application must not install a service, driver, or
public network listener. Startup registration and lifecycle automation are
optional, individually controllable, and removable.

Unsigned preview builds may trigger Windows warnings. A checksum detects
accidental or malicious modification after publication, but it does not
provide publisher identity. The project must never describe an unsigned build
as trusted or signed.

## Vulnerability reporting

Follow [SECURITY.md](../SECURITY.md). Do not put exploit details or unredacted
home-network data in a public issue.
