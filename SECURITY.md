# Security Policy

## Supported versions

LibraTray has not published a release. Security fixes currently target the
`main` branch only. A version-support table will be added when releases exist.

## Reporting a vulnerability

When the repository's GitHub **Security** tab offers “Report a vulnerability,”
use that private channel. If it is not available, open a minimal public issue
asking the maintainers for a private contact method **without including
technical details, logs, addresses, credentials, or proof-of-concept code**.

Please include privately:

- affected commit or version;
- impact and prerequisites;
- reproducible steps or a minimal proof of concept;
- whether the issue can expose local-network data or cause device commands;
- suggested mitigation, if known;
- a redacted log only when needed.

Maintainers will acknowledge a complete report as soon as practical, assess
severity, coordinate a fix and disclosure plan, and credit the reporter if
requested. Do not test against networks or devices you do not own or have
permission to use.

## Security boundaries

LibraTray communicates with untrusted device input over a local protocol that
may be plaintext and unauthenticated. A device response is data, never code.
The application must:

- use only trusted local networks;
- validate discovery endpoints and JSON types;
- bound datagram, frame, message, and log sizes;
- apply timeouts, cancellation, connection limits, and command-rate limits;
- avoid Internet listeners and local web servers;
- store no Xiaomi/Yeelight account password or cloud token;
- redact network and host identifiers in diagnostic exports by default;
- never upload telemetry or logs automatically.

These controls reduce risk but cannot make an untrusted Wi-Fi network safe.
See [privacy and security](docs/privacy-and-security.md).

## Supply chain

Dependencies must have an explicit compatible license, a documented purpose,
and a maintained upstream. Restore from committed project metadata; do not run
unreviewed scripts or publish artifacts produced from a dirty or unverifiable
source tree. Release checksums are planned but no official release exists yet.
