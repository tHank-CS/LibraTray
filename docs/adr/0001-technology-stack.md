# ADR 0001: Technology Stack

- Status: Accepted
- Date: 2026-07-28
- Decision owners: LibraTray maintainers

## Context

LibraTray needs reliable Windows 10/11 tray and input integration, asynchronous
UDP/TCP protocol handling, a compact accessible UI, low idle overhead,
portable publication, reproducible tests, and long-term maintenance. The
product is Windows-specific; cross-platform reach has value only if it does not
weaken native lifecycle behavior or increase operational cost.

Five coherent candidates were compared:

1. **WPF** — C# / .NET 10 LTS / WPF with thin Win32 interop;
2. **WinUI 3** — C# / .NET 10 / Windows App SDK;
3. **Tauri 2** — Rust host, web UI, WebView2;
4. **Avalonia 11** — C# / .NET 10 / Avalonia;
5. **native C++** — C++20 / Win32 with a small custom/native UI surface.

The comparison is architecture selection, not a measured benchmark. Scores use
the documentation and ecosystem state reviewed on 2026-07-28. Package size,
startup, and memory figures are estimates and must be replaced by release
measurements.

## Method

Each criterion has weight 1–5. Scores are 1 (poor/high-cost) through 5
(strong/low-cost). The weights sum to 100. The total is:

```text
sum(weight × score) / 5
```

and is therefore a percentage out of 100.

## Complete 30-criterion matrix

| # | Criterion | Weight | WPF | WinUI 3 | Tauri 2 | Avalonia 11 | C++/Win32 | Scoring basis |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 1 | Windows 10 build 19044 compatibility | 5 | 5 | 5 | 5 | 5 | 5 | All can technically target Windows 10; formal support still follows Microsoft/.NET/WebView servicing boundaries. |
| 2 | Windows 11 compatibility | 2 | 5 | 5 | 5 | 5 | 5 | All candidates have a supported Windows 11 path. |
| 3 | System tray | 5 | 5 | 2 | 5 | 4 | 5 | WPF/C++ can use `Shell_NotifyIcon`; Tauri ships a tray API; Avalonia exposes `TrayIcon`; Windows App SDK lacks a first-class equivalent and requires workaround/interop. |
| 4 | Tray mouse events | 4 | 4 | 3 | 4 | 2 | 5 | Direct Win32 offers the complete callback surface; WPF/WinUI/Tauri need adapters; Avalonia's abstraction is less complete for wheel/double-click requirements. |
| 5 | Global hotkeys | 4 | 4 | 4 | 5 | 3 | 5 | Win32 `RegisterHotKey` is direct; WPF/WinUI interop is small; Tauri has an official plugin; Avalonia needs platform interop/package work. |
| 6 | Lock/unlock events | 3 | 5 | 4 | 3 | 3 | 5 | WPF/C++ can directly receive WTS session notifications; other hosts need plugin/native bridges. |
| 7 | Sleep/wake events | 3 | 5 | 4 | 3 | 3 | 5 | WPF/C++ map cleanly to `WM_POWERBROADCAST`; alternatives add host/plugin layers. |
| 8 | Display-power events | 4 | 5 | 4 | 3 | 3 | 5 | Direct window-message/power-setting registration is strongest in WPF+interop and C++; other stacks require native bridging. |
| 9 | Compact custom window | 3 | 5 | 5 | 5 | 5 | 3 | Managed/web UI frameworks make a 360–440 px adaptive panel straightforward; raw C++ UI costs more custom layout work. |
| 10 | Non-activating OSD | 4 | 5 | 4 | 5 | 5 | 5 | All can create a no-activate/topmost window; WPF/Tauri/Avalonia/C++ have straightforward window-style hooks, WinUI needs additional HWND work. |
| 11 | High-DPI scaling | 2 | 4 | 5 | 4 | 5 | 2 | WinUI/Avalonia are strongest by default; WPF is mature but needs per-monitor testing; raw Win32 requires more manual DPI work. |
| 12 | Accessibility and keyboard | 3 | 5 | 5 | 3 | 4 | 2 | WPF/WinUI have mature automation peers and focus systems; web accessibility varies; Avalonia is good but less Windows-native; raw C++ is costly. |
| 13 | Async TCP/UDP | 5 | 5 | 5 | 5 | 5 | 3 | .NET and Rust provide mature async networking; native C++ requires more infrastructure/discipline. |
| 14 | Streaming JSON | 3 | 5 | 5 | 5 | 5 | 3 | .NET `System.Text.Json` and Rust serde ecosystems are mature; C++ generally adds and maintains another library. |
| 15 | Unit testing | 2 | 5 | 4 | 4 | 5 | 4 | .NET non-UI layers test cleanly; WinUI host tests are more involved; Rust is strong; native UI boundaries remain costly. |
| 16 | Mock-device testing | 4 | 5 | 5 | 5 | 5 | 4 | All managed/Rust candidates support loopback async test servers naturally; C++ can but at greater harness cost. |
| 17 | Startup time | 4 | 4 | 3 | 3 | 3 | 5 | Native C++ has the best expected cold start; managed desktop is acceptable; packaged/webview stacks add startup layers. |
| 18 | Idle memory | 5 | 4 | 3 | 2 | 3 | 5 | Native C++ is leanest; WPF avoids a browser process; WinUI/Avalonia are moderate; WebView2 is expected to cost most. |
| 19 | Package size | 3 | 3 | 2 | 4 | 3 | 5 | C++ is smallest; Tauri is small when WebView2 is already present; self-contained .NET and Windows App SDK bundles are larger. |
| 20 | Single-file/portable publication | 5 | 5 | 3 | 3 | 5 | 5 | WPF/Avalonia self-contained and native C++ portable folders are straightforward; WinUI deployment and WebView2 packaging add constraints. |
| 21 | No additional runtime installation | 4 | 5 | 4 | 3 | 5 | 5 | Self-contained .NET and static/native publication can carry dependencies; Tauri normally relies on serviced WebView2 or bundles it. |
| 22 | Installer creation | 2 | 4 | 3 | 5 | 3 | 2 | Tauri has documented Windows bundling; WPF is conventional; WinUI has deployment choices; Avalonia/C++ need more packaging work. |
| 23 | GitHub Actions build | 2 | 5 | 4 | 4 | 5 | 4 | .NET SDK builds are direct; WinUI/Windows SDK and Rust/WebView/native toolchains add setup. |
| 24 | Dependency maintenance | 3 | 5 | 3 | 4 | 2 | 5 | WPF/BCL and Win32 are Microsoft platform surfaces; Windows App SDK/Avalonia add framework churn; Tauri is active but multi-ecosystem. |
| 25 | Contribution threshold | 1 | 5 | 4 | 2 | 4 | 2 | C#/.NET is one approachable stack; Tauri requires Rust plus web skills; native C++/Win32 has the steepest entry. |
| 26 | Long-term maintenance risk | 4 | 5 | 3 | 3 | 2 | 4 | WPF and Win32 are stable; newer app frameworks and multi-stack/webview dependencies have higher change risk. |
| 27 | Windows-consistent UI | 3 | 4 | 5 | 3 | 4 | 3 | WinUI is the current Windows design surface; WPF/Avalonia can be polished deliberately; web/native custom UI need more work. |
| 28 | Internationalization | 2 | 4 | 5 | 3 | 4 | 3 | WinUI and .NET resource models are mature; all are viable, but web/native approaches need more explicit plumbing. |
| 29 | Optional auto-update | 2 | 3 | 4 | 5 | 3 | 2 | Tauri has the most integrated updater path; WinUI has deployment options; WPF/Avalonia/native require a separately secured updater. |
| 30 | Debugging and diagnostics | 4 | 5 | 4 | 3 | 4 | 4 | .NET tooling and structured diagnostics are strongest; WinUI packaging and web/native boundaries add friction. |
|  | **Weighted total** | **100** | **92.8** | **77.8** | **77.6** | **77.8** | **84.4** | Percentage after dividing weighted score by five. |

The numeric rows and totals are the accepted decision record and must not be
silently reweighted after implementation begins. A future replacement requires
a new ADR.

## Official evidence reviewed

- [.NET and .NET Core lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-and-net-core);
- [.NET installation and supported Windows releases](https://learn.microsoft.com/en-us/dotnet/core/install/windows);
- [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/);
- [`Shell_NotifyIconW`](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw);
- [`RegisterHotKey`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey);
- [`WM_WTSSESSION_CHANGE`](https://learn.microsoft.com/en-us/windows/win32/termserv/wm-wtssession-change);
- [`WM_POWERBROADCAST`](https://learn.microsoft.com/en-us/windows/win32/power/wm-powerbroadcast);
- [Windows App SDK tray issue 713](https://github.com/microsoft/WindowsAppSDK/issues/713);
- [Tauri global-shortcut plugin](https://v2.tauri.app/plugin/global-shortcut/);
- [Tauri Windows installer](https://v2.tauri.app/distribute/windows-installer/);
- [Avalonia Windows platform guide](https://docs.avaloniaui.net/docs/platform-specific-guides/windows).

These sources establish available platform mechanisms, not LibraTray-specific
performance. Scores for maintenance burden and contributor experience are
engineering judgments stated explicitly above.

## Decision

Use:

- **C# and .NET 10 LTS**;
- **WPF** for the future desktop shell;
- **thin Win32 interop** only for system tray event detail, global hotkeys,
  session notifications, power/monitor events, and special window styles;
- .NET BCL for UDP/TCP, cancellation, JSON, configuration primitives, and
  diagnostics;
- MSTest for automated tests.

The protocol core, identity mapping, state engine, and application services
must remain free of WPF references. Win32 calls live behind small disposable
interfaces and are mockable at the service boundary.

### Why .NET 10 instead of .NET 8

At the review date, Microsoft's lifecycle page lists:

- .NET 10 LTS support through **2028-11-14**;
- .NET 8 LTS support through **2026-11-10**.

Starting a new public project on .NET 8 would create an immediate migration
deadline. .NET 10 therefore provides the longer supported baseline. The build
records the SDK family; future roll-forward occurs only through an explicit
maintenance change.

## Expected publication and resources

The intended first public artifact is a self-contained `win-x64` portable
directory compressed as ZIP. An installer is optional for a later phase.
There is no code-signing certificate; documentation must warn that unsigned
artifacts may trigger Windows publisher/SmartScreen warnings.

The following are **planning estimates, not measured promises**:

| Candidate | Estimated publish/package size | Estimated idle working set |
| --- | --- | --- |
| WPF self-contained | 120–180 MB uncompressed; 45–85 MB ZIP | 60–130 MB |
| WinUI 3 | 150–250 MB | 90–200 MB |
| Tauri 2 | 5–25 MB when WebView2 is present; roughly +127 MB for an offline WebView2 payload | 100–250 MB |
| Avalonia 11 | 100–190 MB self-contained; possible NativeAOT experiment 25–70 MB | 70–170 MB |
| native C++ | 2–15 MB | 10–50 MB |

The first release candidate must measure clean-machine cold start, warm start,
idle private working set after five minutes, unpacked publish size, and ZIP
size. If the selected WPF build materially exceeds these ranges, investigate
framework-dependent versus self-contained trade-offs without making users
install an unsupported runtime.

## Minimum operating system

The technical compatibility target remains Windows 10 build 19044 or later and
Windows 11, x64. Formal support is limited to Windows editions/builds still in
Microsoft's servicing lifecycle; an otherwise compatible out-of-service build
is best effort. CI and a real-machine smoke matrix must include a supported
Windows 10 environment where available and Windows 11.

## Rejected alternatives

### Native C++ / Win32 — 84.4

Best resource profile and direct system integration, but rejected because JSON,
async cancellation, accessibility, internationalization, custom UI, test
harnesses, and contributor onboarding would consume disproportionate project
effort. Protocol correctness matters more than saving tens of megabytes before
measurement.

### WinUI 3 — 77.8

Best modern Windows visual consistency. Rejected for the current project
because tray support is not first-class, HWND/deployment complexity remains,
and package/startup cost is higher for a tray-first utility. It does not remove
the Win32 work central to this application.

### Avalonia 11 — 77.8

Strong cross-platform UI and testing story. Rejected because cross-platform
delivery is not a goal, while Windows lifecycle/tray detail still needs native
interop. The additional UI framework broadens dependency and long-term
maintenance risk without solving a current requirement.

### Tauri 2 — 77.6

Strong tray, updater, and installer tooling and potentially small application
payload. Rejected because WebView2 increases idle-memory/process complexity,
offline packaging changes the size story, and Rust plus web UI raises the
contribution and diagnostic boundary cost. The product does not need web
technology.

## Current dependency register

### Production

The phase-B `LibraTray.Core` has **no third-party PackageReference**. It uses the
.NET base class libraries for networking, JSON, cancellation, and collections.
This keeps the core auditable and avoids package/runtime duplication.

WPF will be part of the Microsoft Windows Desktop shared framework/publication
surface when the UI phase begins; it is the selected platform, not an
additional component library. A UI library with overlapping controls must not
be added without a new dependency review.

### Test-only

| Dependency | Version | Purpose | License | Why BCL is insufficient | Maintenance | Replacement cost | Release-size impact |
| --- | --- | --- | --- | --- | --- | --- | --- |
| MSTest meta-package | 4.3.2, centrally pinned | Test framework, adapter, runner integration, TRX/coverage toolchain | MIT per NuGet package metadata and Microsoft testfx repository | BCL has assertions but no discovery, runner protocol, adapter, reporting, or coverage integration | Microsoft-supported; 4.3.2 published 2026-07-13 at review | Low-to-medium; tests use MSTest attributes/assertions and could migrate mechanically | None; referenced only by the non-packable test project and not shipped in app artifacts |

MSTest 4.3.2 brings test SDK/adapter/framework/report/coverage dependencies.
Their exact resolved versions are captured by NuGet restore metadata. They are
build/test inputs, not production redistribution. Before a release, CI must
restore locked metadata, audit the complete transitive graph and license
metadata, and ensure none appears in the portable application payload.

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| WPF defaults can look dated | restrained custom theme, native typography, DPI/accessibility testing; no second UI framework |
| Win32 lifetime mistakes | tiny SafeHandle/disposable adapters, message-window tests, explicit unregister on shutdown |
| Windows 10 servicing ambiguity | document technical minimum versus supported lifecycle; smoke-test actual supported SKUs |
| Self-contained size | measure real artifacts; publish only `win-x64`; avoid unnecessary packages/resources |
| Trimming/single-file incompatibility | portable directory/ZIP is the baseline; enable trimming/single-file only after WPF and reflection tests |
| UI thread blocked by LAN work | async service boundary; never wait synchronously on socket operations |
| Hardware behavior inferred from generic protocol | probe-first evidence gates; `unverified` status blocks adapter/UI promotion |
| Notification defects | source/confidence state model and bounded reconciliation query |
| Global hotkey collision | configurable bindings; explicit error; one failed registration does not exit |
| Resource estimates wrong | publish-time benchmark with documented machine/build and regression threshold |

## Consequences

Positive:

- one language/runtime for core, tests, services, and UI;
- mature Windows accessibility, localization, debugging, and async networking;
- direct system integration without a browser runtime;
- small third-party production dependency surface.

Negative:

- Windows-only UI;
- larger self-contained package and higher memory than optimized native C++;
- manual interop for several tray/lifecycle details;
- visual polish must be designed rather than inherited from WinUI.
