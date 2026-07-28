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
dotnet --info
dotnet restore LibraTray.slnx
dotnet build LibraTray.slnx -c Release --no-restore
dotnet test LibraTray.slnx -c Release --no-build
```

Then run the repository's packaging script once it exists. Verify:

- the publish directory exists;
- the expected launcher exists;
- a clean machine or VM can start the portable build;
- no development-only settings or symbols are included unintentionally;
- ZIP contents use relative paths;
- SHA-256 checksums match freshly generated artifacts.

Never report a step as passed unless it was actually run for the tagged commit.

## Tag and GitHub release

1. Update `CHANGELOG.md` with the release date.
2. Commit the release metadata.
3. Create and push the signed or annotated version tag according to maintainer
   policy.
4. Let the tag-triggered workflow rebuild from source.
5. Compare workflow artifacts and checksums with the expected manifest.
6. Draft release notes from the changelog, including known limitations and all
   still-unverified device behavior.
7. Publish only after a maintainer reviews the artifacts.

The planned release is a self-contained Windows x64 portable ZIP and, when the
installer work is complete, an installer. Code signing is not currently
required. State plainly that unsigned artifacts may trigger Windows
SmartScreen or publisher warnings; never imply a trusted publisher signature.

## Rollback

Do not silently replace assets under an existing tag. If an artifact is wrong,
mark the release affected, remove it from normal download paths when necessary,
investigate, and publish a new patch version. Preserve evidence and notify users
when security or data integrity is involved.
