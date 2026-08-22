# Releasing LibraTray

This is the release runbook for public LibraTray releases.

## Version policy

LibraTray follows Semantic Versioning:

- `v0.1.0`: protocol probe;
- `v0.2.0`: verified basic two-channel control;
- `v0.3.0`: tray, quick panel, and shortcuts;
- `v0.4.0`: state reconciliation and reconnect;
- `v0.5.0`: Windows lifecycle automation;
- `v1.0.0`: stable public release.

A tag must match `vMAJOR.MINOR.PATCH`. Do not create a version heading, badge,
download link, or tag until its artifact has passed this runbook.

## Preconditions

- Required real-device behavior is marked verified in protocol documentation.
- `CHANGELOG.md`, README files, notices, and version metadata agree.
- The tree contains no secret, personal path, real device address, diagnostic
  log, or ordinary build artifact.
- Dependency licenses and vulnerabilities have been reviewed.
- The release commit is on the intended branch and the working tree is clean.

## Local verification

Run on a supported Windows x64 host:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\restore.ps1 -Locked
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\verify.ps1 -SkipRestore
```

The second command verifies formatting/analyzers, builds Release, runs all test
projects with reports and coverage, and performs the dependency audit. Then
build the candidate assets:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 1.1.0 -Locked
```

Verify:

- the publish directory exists;
- the expected launcher exists;
- a clean machine or VM can start the portable build;
- no development-only settings or symbols are included unintentionally;
- ZIP contents use relative paths;
- SHA-256 checksums match freshly generated artifacts.
- the MSI installs for the current user without elevation, launches the app,
  upgrades the previous candidate, and removes its program files and Start menu
  shortcut on uninstall;
- double-click installation shows the standard wizard, license page, selectable
  install directory, and a final confirmation; maintenance removal also asks for
  confirmation;
- if “start with Windows” was enabled, uninstall also removes the
  `LibraTray.lnk` Startup-folder shortcut;
- both portable and installed builds display the branded executable, window,
  and tray icon at normal Windows scaling.

Never report a step as passed unless it was actually run for the tagged commit.

## v1.0.0 candidate record

The 2026-08-02 candidate has completed the repository's automated suite and
maintainer testing on Windows with real YLTD003 hardware. The exercised surface
includes discovery, both light channels, brightness, colour temperature,
whole-background RGB, presets, shortcut capture and execution, tray controls,
Windows lifecycle behavior, the install/uninstall wizard, dark-theme controls,
and normal-versus-startup launch behavior.

The following limitations remain explicit and are not release blockers unless
the maintainer decides otherwise:

- artifacts are unsigned;
- validation used the maintainer's Windows host rather than a separate clean VM;
- target-device evidence is primarily firmware 38;
- two-zone background RGB remains disabled;
- firmware-38 background-off notifications can be incorrect and require query
  reconciliation;
- the firmware-38 ambient renderer may require the bounded reconnect recovery
  documented in the protocol notes.

Reviewed release notes are stored in `docs/releases/v1.0.0.md`, with a Chinese
translation in `docs/releases/v1.0.0.zh-CN.md`. The release-preparation PR,
annotated tag, workflow-built artifacts, checksum review, and publication were
completed on 2026-08-02.

## v1.0.1 candidate record

The 2026-08-04 patch candidate passed the repository verification suite and
maintainer UI and real-device checks for titleless windows, the custom colour
picker, swatches, resizing, slider snap points, and light control. Release notes
are stored in `docs/releases/v1.0.1.md` and
`docs/releases/v1.0.1.zh-CN.md`. The existing unsigned-artifact, single-device,
firmware-38, and clean-VM limitations remain unchanged.

## v1.1.0 candidate record

The 2026-08-22 candidate adds default-off experimental segmented ambient RGB,
a manual renderer POST, secure-screensaver/lock-state fusion, and safer
shutdown/startup restoration. Repository verification passed 340/340 tests, a
zero-warning Release build, and the dependency vulnerability audit. Maintainer
testing passed `Win+L`, secure-screensaver automation, direct segmented colour,
and the query-stage shutdown-ticket restore path. The corrected segmented-preset
path, overlapping display events, independent both-lights-on sign-in policy,
and separately confirmed cold-power POST remain unverified on the real device
and must stay explicit during release review. Segment colours remain
requested/unconfirmed, only firmware 38 has direct evidence, one target device
is available, and artifacts are unsigned. Candidate assets must be rebuilt and
audited after those checks.

Release notes are stored in `docs/releases/v1.1.0.md` and
`docs/releases/v1.1.0.zh-CN.md`.

## Tag and GitHub release

1. Update `CHANGELOG.md` with the release date.
2. Commit the release metadata.
3. Create and push the signed or annotated version tag according to maintainer
   policy.
4. Let the tag-triggered workflow rebuild from source and create a **draft**
   GitHub Release.
5. Compare workflow artifacts and checksums with the expected manifest.
6. Review the generated notes, including known limitations and all
   still-unverified device behavior.
7. Publish the draft only after a maintainer reviews the artifacts.

The release contains a self-contained Windows x64 portable ZIP, a current-user
MSI that installs under `%LOCALAPPDATA%\Programs\LibraTray`, and a checksum
manifest. Code signing is not currently available. State plainly that unsigned
artifacts may trigger Windows SmartScreen or publisher warnings; never imply a
trusted publisher signature.

## Rollback

Do not silently replace assets under an existing tag. If an artifact is wrong,
mark the release affected, remove it from normal download paths when necessary,
investigate, and publish a new patch version. Preserve evidence and notify users
when security or data integrity is involved.
