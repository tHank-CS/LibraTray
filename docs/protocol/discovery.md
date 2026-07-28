# Yeelight LAN Discovery

Evidence snapshot: 2026-07-28.

## Status

The wire format below is **officially documented** by the
[Yeelight WiFi Light Inter-Operation Specification](https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf).
Its behavior on the user's stock YLTD003 / `lamp15` firmware is **unverified**
until a redacted discovery capture is supplied.

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

The current probe binds one UDP socket to `IPAddress.Any` and lets Windows
select the outbound interface. On a machine with several active adapters or a
VPN, the default route may not be the LAN containing the light. If the device
address is already known, use unicast discovery:

```powershell
dotnet run --project tools/LibraTray.Probe -- discover --target <DEVICE_IP>
```

Enumerating and sending from every eligible interface is a known limitation of
the phase-B probe. Discovery results are still deduplicated without discarding
newer state.

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

It may be sent when a device joins the network and periodically afterwards. A
listener parses and updates known device state but does not reply. Advertisement
timing and availability on `lamp15` are currently **unverified**.

## Parsing rules

1. Apply a strict maximum datagram size before allocating or logging.
2. Accept only the documented response or advertisement start line.
3. Split headers at the first colon; trim optional whitespace around values.
4. Compare header names case-insensitively and retain unknown headers.
5. Reject duplicate security-sensitive fields such as conflicting `Location`
   values.
6. Parse `Location` as an absolute `yeelight://` URI.
7. Validate host and port; do not execute or dereference any other URI scheme.
8. Store raw `model` separately; normalize only a copy for exact identity
   matching.
9. Treat numeric-looking header values as untrusted until range-checked.
10. Redact endpoint and device ID in shareable logs.

Malformed responses are ignored with a bounded diagnostic event. They never
cause a connection to an arbitrary URL or command execution.

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
  user must confirm the exact model and endpoint.
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
