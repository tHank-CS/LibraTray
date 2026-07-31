# State Synchronization

## Status

This is the synchronization contract for the future Libra Pro adapter. The
generic request/result/`props` model is officially documented. Physical-knob
behavior, two-channel property reliability, and firmware-specific conflicts on
stock YLTD003 are currently **unverified**.

## Goals

- represent main and ambient channels independently;
- reflect physical and software changes promptly;
- never mistake “off” for “offline”;
- avoid high-frequency polling;
- distinguish observed, optimistic, stale, and unknown state;
- recover deterministically after timeout, conflict, reconnect, or restart;
- preserve enough evidence to diagnose incorrect firmware notifications.

## State record

Each property stores:

```text
Value
Channel
Source          query | notification | command-pending | command-result
Confidence      confirmed | provisional | stale | unknown
ObservedAtUtc
MonotonicOrder
RequestId       optional
ConnectionEpoch
```

Whole-device connectivity is separate from channel power. For example,
main-off/ambient-on is online, as are main-on/ambient-off and both-off when the
TCP connection/queries remain healthy.

## Sources and precedence

Precedence is contextual rather than a permanent ranking:

1. A valid response resolves only its matching request ID.
2. A complete, valid notification newer than the current observation may update
   state immediately.
3. An optimistic command value is provisional until response or corroborating
   observation.
4. A reconciliation query supersedes older conflicting provisional data.
5. Data from a previous connection epoch cannot overwrite newer data after
   reconnect.
6. Invalid or out-of-range fields are recorded as errors and never become
   domain state.

Arrival order alone is insufficient because responses and notifications may
interleave. Timestamps are diagnostic; monotonic local order and request IDs
drive deterministic merging.

## Connection lifecycle

### Connect

1. Enter `Connecting`.
2. Establish one bounded TCP connection.
3. Increment the connection epoch.
4. Start the receive loop.
5. Query the verified initial property set.
6. Publish `Online` only after a valid protocol exchange.

### Disconnect

1. Atomically mark the connection unavailable.
2. Fail or cancel pending requests with a disconnect reason.
3. Retain last confirmed values as stale rather than converting them to off.
4. Schedule bounded exponential reconnect with jitter.
5. On reconnect, query full state before treating notifications as complete.

### Device restart

A new connection, changed uptime/firmware evidence, or abrupt reset clears
pending command assumptions. Last state remains displayable as stale until a
fresh query.

## Command flow

1. Validate capability and range.
2. Assign request ID.
3. Record prior confirmed state and provisional target.
4. Serialize one CRLF-terminated command.
5. Wait with timeout and cancellation.
6. On success, retain provisional state briefly while waiting for notification
   or query evidence.
7. On device error, roll back to prior confirmed state and expose the error.
8. On timeout, mark uncertain and schedule one reconciliation query; do not
   report success.

The production adapter never sends an unverified `lamp15` command solely
because it exists in a different project.

## High-frequency controls

Brightness, temperature, and colour sliders use a replaceable latest-value
queue:

- cap send frequency below protocol quotas;
- while a command is in flight, replace queued intermediate values;
- on pointer/key release, enqueue the final value even if equal to the latest
  provisional state;
- cancellation removes obsolete queued values but not an already written
  command;
- a failure marks uncertainty and triggers bounded reconciliation.

This prevents backlog and ensures the physical final position is sent.

## Notifications

`props` is a partial update. Missing fields mean unchanged/unknown, never zero.
Unknown fields are retained for diagnostics. Each known field is independently
parsed and range-checked, so one invalid property does not discard valid peers.

Until hardware tests establish reliability:

- a main-channel notification may be applied provisionally;
- background-power updates associated with known open-source suspicion trigger
  a debounced query;
- physical-knob inference is never based solely on timing;
- the log records exact redacted notification and later query result.

## Conflict policy

A conflict exists when two valid sources in the same connection epoch claim
different values without an intervening known command.

1. Preserve both observations in the diagnostic event.
2. Mark the affected property provisional/uncertain.
3. Debounce repeated conflicts.
4. Send one verified read-only query.
5. Apply the query as confirmed if valid.
6. If the query conflicts repeatedly or times out, retain last confirmed value
   as stale and expose “state may be out of sync.”

Never spin in a query loop. Rate limiting and backoff apply to reconciliation.

## Windows lifecycle automation

Future lock/display/sleep actions enter the same command pipeline with source
metadata and conservative precedence:

- automation is individually opt-in;
- duplicate sleep/display events are debounced;
- recent explicit user action wins within a suppression window;
- sleep stores intent but does not wait indefinitely on network I/O;
- wake waits for network recovery, reconnects, then queries before restoring;
- restore is skipped if newer user/device state exists.

This behavior is planned and unimplemented at the current milestone.

## Planned tests

The Phase-C state implementation must add automated tests using loopback test
doubles for:

- split/coalesced response and notification frames;
- out-of-order responses;
- notification between command and result;
- stale epoch data;
- timeout/cancellation/disconnect;
- incorrect background notification followed by query correction;
- independent channel combinations;
- slider coalescing and final-value delivery;
- reconnect/restart.

Real-device tests must repeat physical-knob, simultaneous input, reconnect, and
reboot scenarios for every firmware claimed as verified. See
[testing guide](../testing-guide.md).

At Phase B, automated coverage is limited to generic framing, parsing, request
correlation, timeout/cancellation/disconnect behavior, explicit reconnect,
identity mapping, probe options/address policy, and diagnostic redaction. The
separately runnable mock device exposes notification, channel, disconnect,
restart, and incorrect-`props` modes as test surfaces; their presence is not a
completed state engine or automated state-reconciliation suite.
