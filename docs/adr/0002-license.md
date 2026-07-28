# ADR 0002: Project License

- Status: Accepted
- Date: 2026-07-28

## Context

LibraTray is an independent interoperability project intended for broad
personal, commercial, and open-source use. It needs a clear grant, predictable
redistribution terms, contributor patent protection, and compatibility with a
small permissively licensed dependency surface.

The license does not grant rights to Yeelight/Xiaomi trademarks, firmware,
closed-source applications, or third-party code.

## Options considered

| License | Permissions | Obligations | Patent terms | Compatibility and project impact |
| --- | --- | --- | --- | --- |
| MIT | permissive use, modification, sublicensing, distribution | preserve copyright and license notice | no explicit patent grant | simplest text and broad adoption, but less explicit patent protection for contributors/users |
| Apache-2.0 | permissive use, modification, sublicensing, distribution | preserve license/notices, mark changed files, honor NOTICE when present | explicit contributor patent grant and termination on patent litigation | clear for independent protocol implementation and commercial use; compatible with MIT/BSD dependencies when their notices are retained |
| GPL-3.0-only | strong copyleft distribution | corresponding source and GPL terms for derivative distribution | explicit patent provisions | maximizes downstream source availability but prevents distributing the combined project under Apache-only terms and raises integration friction for a Windows utility |

## Decision

License LibraTray under the **Apache License, Version 2.0**.

Reasons:

- it remains permissive for users, packagers, and commercial contributors;
- its explicit patent grant is valuable for protocol and systems work;
- change/notice rules improve provenance without imposing strong copyleft;
- MIT, BSD-2-Clause, BSD-3-Clause, and Apache-2.0 dependencies can generally be
  used while preserving their individual obligations;
- it supports clean-room independent implementation without suggesting any
  right to third-party trademarks or code.

The repository's canonical text is [`LICENSE`](../../LICENSE).

## Source-use rules

- Every copied or redistributed third-party component requires an explicit
  compatible license, exact version, and notice entry.
- MIT/BSD notice text must be retained as required.
- Apache-2.0 NOTICE content, when present and relevant, must be carried in the
  documented locations.
- GPL implementation code is not copied into this Apache-2.0 project. Public
  protocol facts may be independently implemented in new code.
- Code without a license remains all-rights-reserved and is not copied.
- Conflicting repository/package license metadata is unresolved until the
  upstream clarifies it.
- Closed-source decompilation, leaked firmware, and unattributed snippets are
  prohibited.

The current investigation copied no source code. See
[`THIRD_PARTY_NOTICES.md`](../../THIRD_PARTY_NOTICES.md).

## Contributions

Unless explicitly agreed otherwise, contributions intentionally submitted to
the project are accepted under Apache-2.0 section 5. Contributors must have the
right to submit their work and disclose material third-party origin.

No Contributor License Agreement is required at this stage. If governance or
distribution needs change, that decision requires a separate public ADR and
cannot retroactively remove existing license grants.

## Trademark and disclaimer

Apache-2.0 does not grant trademark rights. “Yeelight,” “Xiaomi,” “Mi Home,”
and product names are used only to describe compatibility.

This project is an independent, unofficial open-source project and is not
affiliated with, endorsed by, or sponsored by Yeelight or Xiaomi.

## Consequences

Positive:

- explicit, standard, OSI-approved terms;
- clear patent protection;
- permissive reuse and packaging;
- compatible with the planned test and platform dependency profile.

Costs:

- redistributors must preserve the license and relevant notices;
- modified files must carry prominent change notices where section 4 requires;
- maintainers must keep dependency and notice audits current;
- Apache-2.0 code cannot simply absorb GPL-only implementation code and remain
  Apache-only.
