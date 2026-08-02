# Releasing LibraTray

This is the release runbook. It documents intended procedure; no LibraTray
release has been produced yet.

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
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 1.0.0 -Locked
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
