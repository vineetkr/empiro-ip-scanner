# Development, testing and release

## Build and tests

```powershell
dotnet restore .\Empiro.IpScanner.sln
dotnet build .\Empiro.IpScanner.sln --configuration Release --no-restore
dotnet test .\tests\Empiro.IpScanner.Tests --configuration Release --no-build --no-restore
```

Validation on 2026-11-24: Release build succeeded with zero warnings and zero errors; 27 tests passed, zero failed and zero skipped. Tests cover IPv4 parsing and limits, deduplication, discovery response parsing, cancellation, a loopback scan, and synthetic demo data constraints. These tests do not establish real-camera compatibility, WAN discovery, exhaustive security coverage or throughput benchmarks.

## Reproduce the five images

```powershell
dotnet run --project .\src\Empiro.IpScanner.Wpf --configuration Release --no-build -- --render-demo .\docs\screenshots
```

The renderer opens the real WPF views, renders their client content, writes five PNG files and exits. It does not scan the local network or read/write live state. Demo inputs are defined in `src/Empiro.IpScanner.Core/DemoData.cs`; window orchestration is in `MainWindow.Demo.cs`. The files are application renders rather than photos of the operating-system window frame.

## Packaging

Install Inno Setup 6, then run:

```powershell
.\build_windows.ps1
```

Optional `-Dotnet` and `-InnoCompiler` parameters select local tool installations. The script runs tests, publishes a self-contained x64 executable and builds a per-user installer. The current source version fields remain 1.1.2; choose and synchronize a new release version in the projects and installer before publishing changes as a new release.

Installers and compiled outputs belong in GitHub Releases, not in source control. The existing installed executable predates repository preparation; no new installer was built during screenshot preparation. Inspect native dependency redistribution terms before a binary release. The project uses GPL-3.0-only; see the root LICENSE and THIRD_PARTY_NOTICES.md.

## Review and contribution

Keep core behavior separate from presentation logic. Add meaningful regression tests when changing parsing, discovery or state behavior. Validate on an authorized test network before claiming hardware support. Do not commit scan exports, local state, real camera credentials, SDK installations, model weights or build caches.

## Known limits and next steps

- Camera and multicast behavior requires validation with real hardware; no office network was available for this preparation.
- A local build and synthetic demo do not resolve the earlier installed-app launch issue.
- UI behavior and demo action isolation have code-level guards but no complete automated UI test suite.
- No CI workflow, signed installer, performance benchmark or formal security audit is included.
- Useful next work: injectable scan services, view models, automated UI tests, expanded export/input review, dependency lock files and CI.
