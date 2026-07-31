# Contributing to LibraTray

Thank you for helping build a focused, reliable Windows controller for the
Yeelight Libra Pro.

## Before opening an issue

- Search existing issues.
- Read [troubleshooting](docs/troubleshooting.md) and the
  [testing guide](docs/testing-guide.md).
- Confirm whether the report concerns stock YLTD003 firmware. Replacement
  firmware and similar-looking Yeelight products have different protocols.
- Remove IP addresses, MAC addresses, device IDs, hostnames, Wi-Fi names,
  usernames, and absolute paths from logs.

Use the structured bug or feature form where possible. A device report should
include the Windows version, app/commit version, firmware version, discovery
model, exact reproduction steps, expected result, observed result, and a
redacted log.

## Development setup

Requirements:

- Windows 10 build 19044 or later, or Windows 11;
- .NET 10 SDK;
- PowerShell;
- Git.

From the repository root:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\restore.ps1 -Locked
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\verify.ps1 -SkipRestore
```

The verification script checks formatting/analyzers, builds Release, runs all
test projects with reports and coverage, and performs the dependency audit.
Run the narrowest relevant test first while developing. Before submitting a
pull request, run the complete commands above and report exactly what ran.

## Design and scope rules

- Keep transport, Yeelight protocol, product identity, device adaptation,
  state reconciliation, application services, and UI concerns separated.
- Do not make the protocol core depend on WPF.
- Do not infer identity from partial retail names. Only a trimmed,
  case-insensitive exact `lamp15` internal model maps to Yeelight Libra Pro.
- Do not show `lamp15` in ordinary user-facing UI.
- Preserve unknown fields; reject malformed or oversized messages safely.
- Never assume one TCP read equals one JSON message.
- Use timeouts and cancellation for network operations.
- Coalesce high-frequency control changes and send the final value.
- Do not add a production dependency when the .NET or Windows platform already
  provides a reasonable implementation.

## Real-device protocol contributions

Public protocol documents and independently observed device behavior are
welcome. Before proposing a `lamp15` command:

1. record the firmware version and whether LAN control was enabled;
2. capture the exact redacted request, response, and any `props` notification;
3. repeat the observation after reconnect and device restart where safe;
4. distinguish official documentation, open-source implementation reference,
   real-device confirmation, and inference;
5. document failure and rate-limit behavior;
6. add mock-device and parser tests.

Do not submit code copied from software without a compatible, explicit license;
closed-source decompilation; leaked firmware; credentials; or unexplained
protocol snippets. Protocol behavior may be independently reimplemented from
public specifications and reproducible observations.

## Code and documentation

- Follow `.editorconfig` and existing naming conventions.
- Use English for code identifiers, technical comments, commit messages, and
  pull-request titles.
- Keep commits small and coherent.
- Add tests for behavior changes and regression fixes.
- Update public documentation when setup, protocol status, configuration, or
  user-visible behavior changes.
- Mark hardware behavior `unverified` until reproduced on a real YLTD003.

## Pull requests

Complete the pull-request template. Explain scope, risk, validation, and
hardware status. A reviewer must be able to distinguish automated checks from
real-device checks. Maintainers may request changes for security, scope,
licensing, or reproducibility reasons.

By contributing, you agree that your contribution is licensed under the
project's Apache License 2.0 and that you have the right to submit it.
