# Yeelight LAN Discovery

Evidence snapshot: 2026-07-31.

## Status

The wire format below is **officially documented** by the
[Yeelight WiFi Light Inter-Operation Specification](https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf).
Its active-search behavior has been reproduced on one YLTD003 / `lamp15`
device running firmware 38. Firmware and network variants remain separately
scoped.

## Search request

Send this UDP datagram to multicast endpoint `239.255.255.250:1982`:

```http
M-SEARCH * HTTP/1.1
HOST: 239.255.255.250:1982
MAN: "ssdp:discover"
ST: wifi_bulb
```

Each of the four lines is terminated by CRLF (`\r\n`). The datagram ends
immediately after the single CRLF terminating `ST: wifi_bulb`; LibraTray does
not append an additional blank line.
The official specification states:

- the start line is exact and case-sensitive;
- `MAN` and `ST` are required, with case-sensitive values;
- header names are case-insensitive;
- `HOST` is optional, but if present has the value above;
- a valid response is unicast to the request's source IP and UDP port.

By default the probe binds one UDP socket to `IPAddress.Any` and lets Windows
select the outbound interface. On a machine with several active adapters or a
VPN, explicitly bind the request/reply socket and multicast interface to the
PC's physical LAN IPv4 address:

```powershell
dotnet run --project tools/LibraTray.Probe -- discover --local-address <PC_LAN_IPV4>
```

`--local-address` is the PC address, not the light address. It accepts only a
literal loopback, RFC1918, or IPv4 link-local address; binding also fails if the
address is not assigned locally. Enumerating and sending from every eligible
interface remains outside the phase-B probe.

The probe returns at most 64 unique, valid records from one discovery run. It
deduplicates device IDs and advertised control endpoints independently, and
retains the first record that passes all validation. A later record is ignored
if either its non-empty ID or its endpoint was already accepted, so changing an
ID cannot make the probe query one TCP endpoint repeatedly.

Every received datagram, including one later rejected as oversized or invalid,
counts toward per-run processing budgets of 512 datagrams and 4 MiB of
cumulative received data. A single datagram larger than 16 KiB is rejected
before decoding or structured logging. If receiving another datagram would
exceed either total processing budget, the probe warns and stops receiving;
reaching 64 unique valid records also warns and stops. These bounds reduce
resource exposure but are not a complete UDP flood defense. The discovery
timeout remains the outer time bound.

## Response

The documented shape is:

```http
HTTP/1.1 200 OK
Cache-Control: max-age=3600
Date:
Ext:
Location: yeelight://192.0.2.10:55443
Server: POSIX UPnP/1.0 YGLC/1
id: 0x0000000000000000
model: <internal model>
fw_ver: <firmware>
support: <space-separated methods>
power: <value>
bright: <value>
name: <reported name>

```

The address and ID above are documentation placeholders, not captured user
data. Fields vary by device. Unknown headers are retained for diagnostics and
must not cause failure.

Important semantics:

- `Location` supplies the TCP endpoint. Port 55443 is the official example,
  not permission to ignore a different valid advertised port.
- `id` is the persistent protocol identity used for deduplication.
- `model` is the internal model used for product mapping.
- `support` is the authoritative advertised method list for capability gating.
- `fw_ver` scopes hardware evidence.
- `name` is device-reported presentation data, not a safe model identifier.

## Advertisements

The official specification also defines a multicast advertisement beginning:

```http
NOTIFY * HTTP/1.1
Host: 239.255.255.250:1982
NTS: ssdp:alive
...
```

It may be sent when a device joins the network and periodically afterwards.
The phase-B probe does **not** subscribe to or parse this `NOTIFY` form; it only
sends an active M-SEARCH and accepts the documented `HTTP/1.1 200` response.
Advertisement listening belongs to a later device-lifecycle implementation.
Its timing and availability on `lamp15` are currently **unverified**.

## Parsing rules

1. Reject a datagram larger than 16 KiB before decoding or structured logging.
2. Process at most 512 datagrams and 4 MiB of cumulative received data per
   discovery run. Warn and stop when accepting another datagram would exceed
   either total budget.
3. In the phase-B active probe, accept only exact `HTTP/1.1 200` or
   `HTTP/1.1 200 OK` response start lines, case-insensitively. A future
   advertisement parser must use a separate path that accepts only the
   documented `NOTIFY` form.
4. Require valid UTF-8, split headers at the first colon, and trim optional
   whitespace around values.
5. Require ASCII HTTP token syntax for header names, reject control characters
   in values, compare names case-insensitively, and retain valid unknown
   headers.
6. Reject duplicate security-sensitive fields such as conflicting `Location`
   values.
7. Parse `Location` as an absolute `yeelight://` URI and reject user info,
   paths other than `/`, queries, and fragments.
8. Validate host and port; accept only loopback, RFC1918, or IPv4 link-local
   endpoints in the phase-B probe.
9. Require the UDP sender IP to equal the IP in the advertised `Location`
   endpoint. A response cannot redirect inspection or a write to another LAN
   host.
10. Store raw `model` separately; normalize only a copy for exact identity
   matching.
11. Treat numeric-looking header values as untrusted until range-checked.
12. Keep the first fully validated record for each unique device ID and control
    endpoint and return at most 64 valid records.
13. Redact endpoint and device ID in shareable logs.

Malformed responses are represented only by a byte-count placeholder, even in
raw-log mode, and ignored with a bounded diagnostic event. They never cause a
connection to an arbitrary URL or command execution.

## Product mapping

If and only if the trimmed internal model equals `lamp15` using ordinal
case-insensitive comparison, display:

```text
Friendly name: Yeelight Libra Pro
Hardware model: YLTD003
Internal model: lamp15
Reported name: <raw device name>
Address: <redacted or local address>
Capabilities: <advertised support list>
```

Unknown models keep their raw identity and reported name. No retail-name
substring activates the Libra Pro adapter.

## Windows and network considerations

- Windows Firewall may prompt for UDP access; allow Private networks only.
- Wi-Fi client isolation and VLAN rules often block multicast or the unicast
  reply.
- VPN and virtual adapters can alter multicast routing.
- Discovery does not authenticate a response. Before a write operation, the
  user must confirm the exact model and endpoint. The probe additionally
  requires the UDP sender IP, `Location` IP, and selected target IP to match.
- The protocol may be plaintext and should be used only on a trusted LAN.

## Hardware verification record

Capture, redact, and document:

- Windows build and active interface category;
- destination address and port;
- response source endpoint;
- complete headers;
- exact `Location`, internal model, firmware, and support list;
- response count and latency;
- whether periodic `NOTIFY` appears;
- behavior with other LAN clients closed.

Until this record exists, every `lamp15` discovery statement beyond the generic
wire specification remains **unverified**.
