# Product Identity Research

Research snapshot: 2026-07-28.

## Decision

LibraTray records the following exact project mapping:

| Internal model | Hardware model | Default friendly product name | Known retail aliases |
| --- | --- | --- | --- |
| `lamp15` | YLTD003 | Yeelight Libra Pro | Yeelight LED Screen Light Bar Pro; Yeelight Monitor Light Bar Pro; Yeelight Screen Light Bar Pro |

This is a **high-confidence cross-source inference**, not a claim that one
official source declares all three identifiers equivalent or that “Yeelight
Libra Pro” is the only official retail name worldwide.

## Source trail

### Official Yeelight sources

1. [Yeelight Monitor Light Bar Pro store page](https://store.yeelight.com/products/yeelight-monitor-light-bar-pro-flagship-edition)
   - page title/retail presentation: “Yeelight Monitor Light Bar Pro”;
   - specification name: “Yeelight LED Screen Light Bar Pro”;
   - model number: YLTD003.
   - This single page directly establishes two retail-name variants for
     YLTD003.
2. [LED Screen Light Bar Pro product page](https://en.yeelight.com/product/led-screen-light-bar-pro/)
   - official product-family page using “LED Screen Light Bar Pro”;
   - describes tunable front light, 16-million-colour ambient light, and the
     physical rotary switch.
3. [Yeelight 2022 product brochure](https://en.yeelight.com/wp-content/uploads/sites/4/2022/06/Yeelight-product-brochure-2022-5-31.pdf)
   - lists the LED Screen Light Bar Pro product family and its two lighting
     functions;
   - corroborates official international product naming. The brochure's
     extracted catalogue notation may include an additional prefix, so the
     current store specification is the primary YLTD003 model source.
4. [Japanese Yeelight Libra Pro instruction PDF](https://japan.yeelight.com/wp-content/uploads/sites/5/2021/11/%E3%82%B9%E3%82%AF%E3%83%AA%E3%83%BC%E3%83%B3%E3%83%8F%E3%83%B3%E3%82%B0%E3%83%A9%E3%82%A4%E3%83%88_%E5%8F%96%E3%82%8A%E6%89%B1%E3%81%84%E8%AA%AC%E6%98%8E%E6%9B%B8-Libra-Pro.pdf)
   - official Japanese-hosted filename uses “Libra-Pro”;
   - instructions identify the device's setup connector/network with a
     `lamp15-...` identifier.
5. [Yeelight Chroma Connector source at reviewed commit](https://github.com/Yeelight/Yeelight-Chroma-Connector/blob/769a616b9012877baf7fa3df426b38ca6697a167/ChromaBroadcastSampleApplication/ChromaBroadcastSampleApplicationDlg.cpp)
   - Yeelight-owned connector code explicitly checks `lamp15` in its
     screen-light-bar path.
   - The connector's Chroma broadcast protocol is not the standard Yeelight
     LAN protocol and is not used as command documentation.

Together, these official sources connect the retail family to YLTD003, show
official regional use of “Libra Pro,” and show `lamp15` as the device-facing
identifier. No reviewed official page expresses the exact sentence
“Yeelight Libra Pro = YLTD003 = lamp15.”

### Independent open-source corroboration

- [Home Assistant Yeelight device table](https://github.com/home-assistant/home-assistant.io/blob/current/source/_integrations/yeelight.markdown)
  maps `lamp15` to YLTD003 and Yeelight LED Screen Light Bar Pro.
- [python-yeelight supported-device documentation](https://yeelight.readthedocs.io/en/stable/devices.html)
  lists the same internal and hardware model relationship.
- [homebridge-yeelight-screen-light-bar](https://github.com/kyuuri10010/homebridge-yeelight-screen-light-bar)
  reports tests with YLTD003 / `lamp15`.

These are strong corroborating implementation sources but are not manufacturer
statements and do not replace real-device verification for protocol behavior.

## Regional and channel naming

The reviewed official global store simultaneously uses “Monitor Light Bar Pro”
in its title and “LED Screen Light Bar Pro” in specifications. The global
product site prefers “LED Screen Light Bar Pro,” while the Japanese-hosted
manual asset uses “Libra Pro.” Retailer listings may omit “LED,” add
“Monitor,” or translate “Screen Light Bar.”

Consequently:

- retail names are presentation metadata, not protocol identity;
- documentation and UI may mention aliases for discoverability;
- code must use the internal model and verified capabilities;
- LibraTray uses “Yeelight Libra Pro” because it is concise, requested by the
  target users, and supported by an official regional asset—not because it is
  globally unique.

## Similar products are not aliases

LibraTray intentionally supports only exact `lamp15` / YLTD003 / Yeelight
Libra Pro. The products below are not merely awaiting future identity evidence;
they are outside the compatibility scope unless the maintainer explicitly
changes the project baseline.

The following names/models are explicitly excluded from the mapping:

- Yeelight Libra and Libra 2;
- Yeelight Pro2;
- Yeelight Pura, Pura Pro, and Pura variants;
- YLTD001;
- any other product whose name merely contains “Libra,” “Pro,” “Screen Light
  Bar,” or “Monitor Light Bar.”

For example, the reviewed
[dckiller51 ESPHome project](https://github.com/dckiller51/esphome-yeelight-led-screen-light-bar)
targets YLTD001, while the reviewed
[Zooblik YLTD003 project](https://github.com/Zooblik/Zooblik_Yeelight_Screen-Light-Bar)
replaces firmware and therefore does not establish stock LAN behavior.

## Mapping algorithm

1. Preserve the raw discovery `model` as `InternalModel`.
2. Trim leading/trailing Unicode whitespace for matching.
3. Compare the result to `lamp15` using ordinal case-insensitive equality.
4. On an exact match, set:
   - `FriendlyProductName = "Yeelight Libra Pro"`;
   - `HardwareModel = "YLTD003"`;
   - the documented alias list.
5. Never use a substring, token, edit-distance, or reported-name match.
6. For an unknown internal model:
   - keep hardware/friendly mapping unset;
   - show a safe device-reported name if present;
   - otherwise show “Unknown Yeelight device”;
   - expose the raw model only in advanced information;
   - do not enable the `lamp15` adapter.

Case and surrounding-space normalization handles discovery formatting
anomalies; it does not modify the stored raw value.

## Display-name resolution

Identity remains recoverable as separate fields:

```text
UserAlias > FriendlyProductName > safe ReportedName > "Unknown Yeelight device"
```

A user alias changes presentation only. Device details still retain the
friendly product name, YLTD003, raw `lamp15`, reported name, firmware, endpoint,
and capabilities. Clearing an alias returns to the friendly default. The
project never sends the alias to overwrite the device-reported name.

## Uncertainty and validation plan

Unresolved items:

- no reviewed single official declaration states the complete three-part
  equivalence;
- “Yeelight Screen Light Bar Pro” without “LED” or “Monitor” is a common short
  alias but was not the primary title in the reviewed official sources;
- stock firmware may vary by region;
- no user-supplied discovery packet has yet confirmed `model: lamp15` and
  firmware behavior for the target physical unit.

The mapping is therefore suitable as a conservative default, while commands,
capabilities, state fields, and notification reliability remain independently
gated by discovery and real-device evidence.
