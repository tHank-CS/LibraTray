## Summary

<!-- What user-visible or engineering outcome does this change provide? -->

## Scope

<!-- List the smallest coherent areas changed and explicitly name anything deferred. -->

## Evidence and verification

Commands actually run:

```text
<command and result>
```

- [ ] `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\restore.ps1 -Locked`
- [ ] `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\verify.ps1 -SkipRestore`
- [ ] Relevant error and edge cases were tested
- [ ] Full diff was inspected for unrelated changes and private data

Unchecked items must be explained:

<!-- State exactly what was not run, why, what was checked instead, and remaining risk. -->

## Hardware/protocol status

- [ ] No hardware-specific behavior changed
- [ ] Tested on stock YLTD003 hardware
- [ ] Hardware behavior remains `unverified`

<!-- Select the applicable item(s). For real-device tests include firmware, exact redacted requests/results/notifications, repetitions, and reconciliation queries. Never include a real address, ID, hostname, username, path, Wi-Fi name, credential, or token. -->

## Architecture, privacy, and licensing

- [ ] Protocol/domain code remains independent of WPF and Windows UI
- [ ] Identity matching uses exact normalized internal model, not retail-name substring
- [ ] Network input is bounded, validated, timed out, and cancellable
- [ ] No account credential, cloud token, telemetry, upload, public listener, or local web service was added
- [ ] New/changed dependencies include version, purpose, license, maintenance status, replacement cost, and artifact-size impact
- [ ] No code was copied from an unlicensed, incompatible, closed-source, or unclear source
- [ ] `THIRD_PARTY_NOTICES.md` and relevant documentation were updated when required

## UI checklist

<!-- Complete only when a UI exists/changes. -->

- [ ] Windows 10 and 11 behavior checked
- [ ] 100%, 125%, 150%, 175%, and 200% scaling considered
- [ ] Keyboard, focus, screen-reader, loading, empty, disabled, success, and error states considered
- [ ] English and Simplified Chinese resources updated
- [ ] Normal UI does not expose `lamp15` as the device name

## Risks and rollback

<!-- Describe known risks, compatibility impact, and the safe rollback path. -->
