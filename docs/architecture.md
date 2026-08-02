# Architecture

## Status and goals

This document defines the intended architecture of LibraTray. The repository
contains research, a protocol probe, a UI-independent protocol core, the
initial product-specific adapter, and a live WPF tray host with trusted
discovery and verified controls. Shortcuts and settings remain under Phase-C
development.

The architecture optimizes for:

- a fast Windows tray workflow;
- local-only operation and bounded untrusted network input;
- independent main and ambient channels;
- deterministic state reconciliation after physical-knob or software changes;
- a testable protocol core with no desktop-framework dependency;
- explicit evidence status for every device-specific behavior.

## Technology baseline

The selected baseline is C# on .NET 10 LTS with WPF for the desktop shell and
thin, isolated Win32 interop for tray mouse events, global hotkeys,
session notifications, and power notifications. The protocol and domain
assemblies target .NET APIs and must not reference WPF. See
[ADR 0001](adr/0001-technology-stack.md).

The primary runtime identifier is `win-x64`. The minimum intended OS is Windows
10 build 19044, with full support limited to Windows releases still serviced by
Microsoft.

## Layers and dependency direction

Dependencies point inward; protocol and domain layers never reference UI or
Windows integration.

1. **Transport**
   - UDP discovery;
   - TCP connection lifecycle;
   - CRLF framing across split and coalesced reads;
   - timeouts, cancellation, reconnect policy, and size limits.
2. **Generic Yeelight protocol**
   - command, result, error, and `props` notification models;
   - monotonic request IDs and pending-request correlation;
   - JSON serialization and tolerant parsing of unknown fields;
   - advertised capability checks and protocol rate limiting.
3. **Product identity**
   - normalized internal model;
   - friendly product and hardware-model mapping;
   - reported name and independent user alias;
   - unknown-model fallback.
4. **Device adapters**
   - generic capabilities;
   - a Yeelight Libra Pro adapter containing only verified behavior;
   - extension boundary for later, separately verified models.
5. **State**
   - last confirmed device state per channel;
   - optimistic/pending UI state;
   - command acknowledgement, rejection, timeout, and rollback;
   - source, confidence, sequence, and timestamp metadata.
6. **Application services**
   - device selection, settings, presets, logging, localization, startup;
   - tray, hotkeys, and Windows lifecycle orchestration.
7. **Presentation**
   - quick panel, settings, discovery, device details, OSD, and diagnostic
     views.

The product-identity layer is independent of transport. The device adapter
consumes protocol abstractions, not raw sockets. Application services consume
domain state, not JSON documents.

## Runtime components

### Protocol probe

`tools/LibraTray.Probe` is a developer-facing executable. It discovers or
connects to a device, shows both friendly and technical identity, prints exact
traffic with timestamps, enforces timeouts, and produces shareable redacted
diagnostics. It must remain usable before the WPF application exists.

Probe operations are divided into:

- read-only discovery and capability inspection;
- documented generic protocol experiments;
- explicit, separately acknowledged unverified experiments.

The probe must not silently send a write command merely because a device is
discovered.

### Protocol core

The core owns framing, messages, endpoint validation, and request correlation.
A connection has one receive loop. Parsed responses resolve pending requests by
ID; notifications are emitted independently. Unknown fields do not terminate
the connection, while invalid JSON and oversized frames produce bounded,
observable failures.

### Mock device

The test double must support normal, delayed, absent, and error responses;
split/coalesced frames; unsolicited notifications; disconnect/restart; wrong
notification values; independent channels; and unknown models. It binds only
to loopback and never needs a real home-network address.

### Desktop host

WPF owns presentation and localization. Thin interop adapters translate
`Shell_NotifyIcon`, `RegisterHotKey`, session, power, and monitor events into
application-level events. Interop handles are scoped and disposed
deterministically. A failure to register one shortcut degrades that shortcut,
not the entire process.

The host creates the tray icon through a small disposable `Shell_NotifyIcon`
adapter and exposes a compact quick panel. Its context menu is rebuilt from
confirmed presentation state whenever it opens. Left click toggles the panel
without waiting for a double-click timeout; middle click toggles main power.
An optional low-level mouse hook observes wheel input only while the pointer is
inside the icon rectangle returned by `Shell_NotifyIconGetRect`; it never
suppresses or rewrites system input, and rapid deltas are coalesced before a
device command is queued. All device controls remain visibly disabled until a
trusted device session is available.

## Identity model

Identity fields are never collapsed:

```text
FriendlyProductName  project mapping, e.g. "Yeelight Libra Pro"
HardwareModel        project mapping, e.g. "YLTD003"
InternalModel        raw protocol identity, e.g. "lamp15"
ReportedName         raw user/device-provided name
UserAlias            local optional override
```

Normal UI resolves `UserAlias`, then `FriendlyProductName`, then a safe reported
name, then “Unknown Yeelight device.” Advanced diagnostics may show every
field. Only a trimmed, case-insensitive exact internal-model match maps to
`lamp15`; retail-name substring matching is forbidden.

## Networking and framing

- Discovery sends the documented M-SEARCH datagram to
  `239.255.255.250:1982` and receives unicast responses.
- The TCP endpoint comes from a validated `yeelight://` `Location` URI; port
  55443 is a documented example/default, not a reason to ignore the response.
- JSON messages are terminated by CRLF.
- The framer preserves partial bytes between reads and emits every complete
  frame when multiple messages arrive together.
- Buffer and message size limits are enforced before JSON materialization.
- Phase-B probe writes require the method and `get_prop` to appear in the
  advertised `support` capabilities. The production session serializes every
  request, spaces messages by at least 500 ms, and enforces a rolling ceiling
  of 55 messages per minute below Yeelight's 60-command-per-minute
  per-connection limit.
- No network operation blocks the UI thread.

## State reconciliation

Each channel is modeled independently. A state property includes value, source
(`query`, `notification`, `command`, `physical-inferred`), confidence, request
ID when applicable, monotonic observation order, and UTC timestamp.

The adapter and Phase-C session:

1. query initial state after connecting;
2. subscribe to/receive `props` notifications on the same connection;
3. merge complete, valid notifications immediately;
4. keep optimistic command state pending until response or corroboration;
5. roll back or mark uncertain on error/timeout;
6. issue a low-frequency reconciliation query after reconnect, conflicting
   evidence, or a known-unreliable notification;
7. never infer device offline merely because one channel is off.

The initial adapter also detects the firmware-38 cold-start background failure
by post-write query mismatch. It performs at most one `bg_set_scene` renderer
initialization per connection epoch, restores confirmed background appearance,
and verifies that main power was preserved. It does not equate every TCP
reconnect with a cold boot.

Transient timeouts, generic device-busy responses, protocol failures, and
postcondition mismatches receive at most two delayed retries. Idempotent setting
commands are resent and re-read; retry UI remains online. Notifications caused
by an in-flight write do not add a redundant query, while rapid physical input
coalesces behind one bounded reconciliation read.

Global hotkeys use `RegisterHotKey` with no-repeat semantics. A process-scoped
operation queue retains input received while a prior write is being verified,
then computes the next target from the latest confirmed session state. A named
per-session mutex prevents duplicate application instances from competing for
the same registrations.

See [state synchronization](protocol/state-synchronization.md).

## Configuration

Configuration is stored under the current user's application-data area with
schema versioning, atomic replace, validation, and corruption recovery. The
initial schema contains a user alias, brightness/temperature increments,
captured shortcut gestures, a tray-wheel preference, and up to 20 local Libra
Pro presets. Device selection, import/export, automation, startup preference,
theme, language, and log level remain later schema additions. Settings must
not contain vendor accounts, passwords, cloud tokens, device addresses, or
unrelated personal data.

Exports warn that local network metadata may be present. Schema versioning and
migrations are required before public releases.

## Logging and diagnostics

Structured logs use UTC timestamps and event categories. Default export
redacts IP/MAC addresses, device IDs, hostnames, usernames, absolute paths,
wireless-network names, and equivalent identifiers. Raw logging is a
non-persistent developer opt-in with an explicit warning; it never triggers
upload or clipboard copy.

The phase-B probe creates a new log for each run and hard-caps one file at
10 MiB. It never appends to or overwrites an existing path. It does not yet
rotate or delete old logs automatically; users remain responsible for local
retention. A later desktop application must add an explicit, documented
retention policy before claiming automatic retention management. Message
bodies are treated as untrusted text and are never evaluated.

## Failure model

Expected failures are explicit domain results or typed exceptions at the
boundary:

- discovery timeout;
- endpoint rejected;
- connect timeout/refusal;
- protocol frame malformed/oversized;
- response error or unknown request ID;
- command timeout/cancellation;
- remote disconnect/restart;
- unsupported capability;
- state conflict;
- configuration corruption.

Broad exception swallowing is prohibited. User-facing layers translate
failures into actionable status while retaining a redacted technical cause.

## Phase boundaries

- **Phase A:** research, ADRs, governance, identity rules.
- **Phase B:** probe, minimal protocol core, mock device, automated tests.
- **Phase C (in progress):** Libra Pro adapter, bounded cold-start recovery,
  tray host, trusted discovery/control binding, notification reconciliation,
  retry, and request scheduling implemented; shortcuts, settings, and presets
  remain.
- **Phase D:** Windows lifecycle automation, packaging, CI/release hardening.

An unverified `lamp15` behavior cannot cross from the probe into the production
adapter merely because another project implements it.
