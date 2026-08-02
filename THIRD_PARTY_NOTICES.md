# Third-Party Research Notices

Research snapshot: 2026-08-02.

## Scope

LibraTray is licensed under Apache-2.0 and is independently implemented from
Yeelight's public LAN specification, public product material, and reproducible
device observations. The projects below were inspected only to compare
protocol behavior, product mapping, architecture, and user experience.

**Source code copied into LibraTray: no, for every entry in this document.**

This file does not replace the license texts of future binary dependencies.
Before a release, the dependency graph and published artifacts must be audited
and this notice updated with every redistributed component, exact version, and
required attribution.

## Investigation register

| Project / reviewed area | Upstream | License found | What was learned | Copied code | Use category |
| --- | --- | --- | --- | --- | --- |
| Home Assistant core, `homeassistant/components/yeelight` | <https://github.com/home-assistant/core/tree/dev/homeassistant/components/yeelight> | Apache-2.0 | device table, two logical channels, local-push lifecycle, targeted re-query after suspicious background power | No | protocol behavior and architecture reference |
| Home Assistant documentation device table | <https://github.com/home-assistant/home-assistant.io/blob/current/source/_integrations/yeelight.markdown> | CC BY-NC-SA documentation repository terms at review; core separately Apache-2.0 | `lamp15` → YLTD003 → LED Screen Light Bar Pro corroboration | No | identity corroboration; facts independently recorded |
| homebridge-yeelight-screen-light-bar, README and `src/yeelight/screen-light-bar.ts` | <https://github.com/kyuuri10010/homebridge-yeelight-screen-light-bar> | MIT | reported YLTD003/`lamp15` tests, dual-channel shape, device-update/re-query behavior | No | protocol behavior reference |
| python-yeelight, supported-device table and ambient/listener API | <https://gitlab.com/stavros/python-yeelight> | BSD-2-Clause | generic LAN behavior, `lamp15` identity, ambient and push concepts | No | protocol behavior reference |
| Ryszard-S/yeelight-GUI | <https://github.com/Ryszard-S/yeelight-GUI> | No LICENSE found | generic Python/Tkinter LAN GUI comparison | No | feature comparison only; copying prohibited |
| BrockDeveloper/y2e-Yeelight-controller | <https://github.com/BrockDeveloper/y2e-Yeelight-controller> | No valid LICENSE found | Java/JavaFX generic controller comparison | No | feature comparison only; copying prohibited |
| vanyason/yeelight | <https://github.com/vanyason/yeelight> | WTFPL | Go/Wails/React desktop packaging and generic single-channel UX comparison | No | architecture/UX comparison only |
| dckiller51/esphome-yeelight-led-screen-light-bar | <https://github.com/dckiller51/esphome-yeelight-led-screen-light-bar> | Apache-2.0 | confirms a similar ESPHome project targets YLTD001, not `lamp15` | No | product-boundary comparison |
| Martlnez/MiMonitorLightTray | <https://github.com/Martlnez/MiMonitorLightTray> | MIT | focused Windows tray, hotkey, and power-event UX; different miIO token protocol | No | UX/architecture reference |
| NumberOneBot/yeelight-client | <https://github.com/NumberOneBot/yeelight-client> | MIT | current `lamp15` dual-channel/notification leads; segment command was physically verified but remains blocked while its relationship to a later firmware-38 cold-start defect is unresolved | No | protocol behavior lead only |
| h-kod/yeelight-controller-gui | <https://github.com/h-kod/yeelight-controller-gui> | MIT | C#/WinForms generic LAN client and partial YLTD003 claim | No | feature comparison only |
| Zooblik/Zooblik_Yeelight_Screen-Light-Bar | <https://github.com/Zooblik/Zooblik_Yeelight_Screen-Light-Bar> | MIT | YLTD003 hardware and physical-remote behavior under replacement firmware | No | hardware-boundary comparison; not stock protocol |
| SR2k/homebridge-yeelight-screen-bar-pro | <https://github.com/SR2k/homebridge-yeelight-screen-bar-pro> | Conflicting: repository LICENSE says Apache-2.0; package metadata says MIT | token-based, polling dual-channel approach | No | comparison only; copying prohibited until license conflict is resolved |
| Yeelight Chroma Connector sample | <https://github.com/Yeelight/Yeelight-Chroma-Connector/blob/769a616b9012877baf7fa3df426b38ca6697a167/ChromaBroadcastSampleApplication/ChromaBroadcastSampleApplicationDlg.cpp> | BSD-3-Clause | Yeelight-owned `lamp15` check and separation of Chroma broadcast behavior from LAN protocol | No | official identity/protocol-boundary evidence |

## Actual build and test dependencies

The phase-B production core and tools are BCL-only and have no third-party
`PackageReference`. The non-packable test projects directly reference the
centrally pinned `MSTest` meta-package 4.3.2. A completed NuGet restore resolved
the following shared test-only graph:

| Package | Resolved version | Purpose | Package license metadata |
| --- | --- | --- | --- |
| MSTest | 4.3.2 | Direct meta-package | MIT |
| MSTest.TestFramework | 4.3.2 | Test attributes, assertions, and execution model | MIT |
| MSTest.TestAdapter | 4.3.2 | Test discovery and runner adapter | MIT |
| MSTest.Analyzers | 4.3.2 | Compile-time test diagnostics | MIT |
| Microsoft.NET.Test.Sdk | 18.4.0 | MSBuild test SDK/host integration | MIT |
| Microsoft.TestPlatform.ObjectModel | 18.4.0 | Test-platform contracts | MIT |
| Microsoft.TestPlatform.TestHost | 18.4.0 | Test process host | MIT |
| Microsoft.CodeCoverage | 18.4.0 | Coverage data collection | MIT |
| Microsoft.Testing.Extensions.CodeCoverage | 18.9.0 | Microsoft Testing Platform coverage extension | **Microsoft .NET Library license file, not MIT/Apache-2.0** |
| Microsoft.Testing.Platform | 2.3.2 | Test execution platform | MIT |
| Microsoft.Testing.Platform.MSBuild | 2.3.2 | MSBuild integration | MIT |
| Microsoft.Testing.Extensions.VSTestBridge | 2.3.2 | VSTest compatibility bridge | MIT |
| Microsoft.Testing.Extensions.TrxReport | 2.3.2 | TRX report generation | MIT |
| Microsoft.Testing.Extensions.TrxReport.Abstractions | 2.3.2 | TRX report contracts | MIT |
| Microsoft.Testing.Extensions.Telemetry | 2.3.2 | Test-platform telemetry component; opt-out enforced by repository scripts | MIT |
| Microsoft.ApplicationInsights | 2.23.0 | Transitive test-platform telemetry transport | MIT |
| Microsoft.DiaSymReader | 2.2.9 | Symbol reading for coverage | MIT |
| Microsoft.Extensions.DependencyModel | 10.0.8 | Test dependency inspection | MIT |
| Newtonsoft.Json | 13.0.3 | Transitive test-platform JSON support | MIT |

Why these are used: the BCL does not provide test discovery, adapters, a test
process protocol, TRX reporting, analyzers, or coverage instrumentation.
MSTest is Microsoft-supported and integrates with the selected .NET toolchain.
Replacing it would have low-to-medium cost for attributes/assertions but higher
cost for equivalent runner/report/coverage integration.

Distribution impact: **zero in the application package**. These packages are
restore/build/test inputs and must not appear in a LibraTray portable or
installer artifact. The code-coverage extension's Microsoft .NET Library terms
are not represented as an open-source license and must not be described as MIT
or Apache-2.0. Repository verification scripts set
`DOTNET_CLI_TELEMETRY_OPTOUT=1`; the application itself contains no test
telemetry component.

Before every release, regenerate the resolved graph from locked restore
metadata, audit every package license/notice, and compare the published
application payload to ensure test tooling is absent.

## Redistributed runtime

The self-contained Windows x64 application redistributes the Microsoft .NET
10 runtime selected by the locked SDK (10.0.10 in the current candidate). Each
portable and MSI payload includes `DOTNET-LICENSE.txt` and
`DOTNET-THIRD-PARTY-NOTICES.txt` copied from that SDK. Those files govern the
redistributed runtime components; LibraTray's own source remains Apache-2.0.

## Installer build dependency (not redistributed as code)

`WixToolset.Sdk` and `WixToolset.UI.wixext` 6.0.2 build the MSI and its standard
wizard. WiX is a build-time dependency: the selected WiX 6 dialog set uses no
custom action by default, and no WiX runtime binary is included in the
application payload.
Use of this WiX version to generate an installer is governed by the WiX Open
Source Maintenance Fee EULA, including its revenue-dependent terms;
contributors and commercial redistributors must review those terms before
generating installers. The portable ZIP does not require WiX. Replacing WiX
would have medium cost because installer authoring, upgrade identity,
validation, and CI packaging would need to be recreated.

## CI and bootstrap tooling (not redistributed)

These tools execute repository automation but are not linked into or shipped
with LibraTray application artifacts:

| Tool | Pinned version / source | Purpose | License | Maintenance and replacement | Application payload impact |
| --- | --- | --- | --- | --- | --- |
| `actions/checkout` | v6.1.0, commit `d23441a48e516b6c34aea4fa41551a30e30af803` | Check out the repository on GitHub Actions | MIT | Official GitHub Action; low replacement cost with another source checkout step | None |
| `actions/setup-dotnet` | v5.3.0, commit `9a946fdbd5fb07b82b2f5a4466058b876ab72bb2` | Install the SDK selected by `global.json` and cache locked NuGet inputs | MIT | Official GitHub Action; medium replacement cost because SDK setup and cache behavior must be reproduced | None |
| `actions/upload-artifact` | v7.0.1, commit `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a` | Retain CI TRX and coverage evidence | MIT | Official GitHub Action; low replacement cost, with different retention semantics to revalidate | None |
| WiX Toolset SDK | 6.0.2, locked NuGet SDK | Build and validate the current-user MSI | OSMF EULA | Build-only; medium replacement cost. Revenue-dependent terms require review by anyone generating the MSI | None; generated MSI uses standard Windows Installer tables |
| WiX Toolset UI extension | 6.0.2, locked NuGet package | Provide the standard install-directory and maintenance wizard | OSMF EULA | Build-only; coupled to the WiX SDK version | Standard Windows Installer UI tables only; no runtime binary or custom action selected |
| GitHub CLI (`gh`) | GitHub-hosted runner version | Create a draft Release from a validated tag and upload assets | MIT | Official GitHub CLI; low replacement cost through the GitHub API or another maintained action | None |
| Microsoft `dotnet-install.ps1` | Official `https://dot.net/v1/dotnet-install.ps1` endpoint; downloaded only on explicit bootstrap | Install exact SDK `10.0.302` into the ignored local `.dotnet` directory | MIT (`dotnet/install-scripts`) | Microsoft-maintained; medium replacement cost via a system SDK or verified offline installer. The bootstrap refuses scripts without a valid Microsoft Authenticode signature | None |

GitHub Actions are pinned to immutable commits; Dependabot may propose reviewed
updates. The bootstrap installer itself is neither committed nor redistributed.
Its mutable official endpoint is an acknowledged bootstrap trade-off mitigated
by an exact SDK version, Microsoft publisher verification, and an ignored local
tool directory.

## Public specifications and product material

The following public material was used as factual documentation, not copied
software:

- [Yeelight WiFi Light Inter-Operation Specification](https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf);
- [Yeelight Monitor Light Bar Pro store page](https://store.yeelight.com/products/yeelight-monitor-light-bar-pro-flagship-edition);
- [Yeelight LED Screen Light Bar Pro product page](https://en.yeelight.com/product/led-screen-light-bar-pro/);
- [Yeelight 2022 product brochure](https://en.yeelight.com/wp-content/uploads/sites/4/2022/06/Yeelight-product-brochure-2022-5-31.pdf);
- [Yeelight Japan Libra Pro instruction PDF](https://japan.yeelight.com/wp-content/uploads/sites/5/2021/11/%E3%82%B9%E3%82%AF%E3%83%AA%E3%83%BC%E3%83%B3%E3%83%8F%E3%83%B3%E3%82%B0%E3%83%A9%E3%82%A4%E3%83%88_%E5%8F%96%E3%82%8A%E6%89%B1%E3%81%84%E8%AA%AC%E6%98%8E%E6%9B%B8-Libra-Pro.pdf).

Product and company names remain trademarks of their respective owners. Their
appearance here describes compatibility and research provenance and does not
imply endorsement.

## Compatibility rule

- No code without an explicit license is accepted.
- GPL-licensed implementation code is not copied into this Apache-2.0 project.
- A permissive license does not remove attribution, notice, provenance, or
  trademark obligations.
- Conflicting license metadata is treated as unresolved.
- Closed-source decompilation, leaked firmware, and unattributed snippets are
  prohibited.
- Public behavior may be independently reimplemented with new code and tests.
