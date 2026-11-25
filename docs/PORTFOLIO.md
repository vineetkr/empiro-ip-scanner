# Empiro IP Scanner — portfolio case study

## Project overview

Empiro IP Scanner is a Windows desktop utility for discovering and organizing devices on an authorized IPv4 network. It combines a compact scanning interface with favorites, searchable results, scan history, exports and optional camera monitoring. The repository includes an offline demonstration so it can be reviewed without access to the original office network.

## Problem and approach

Network administration often involves finding device addresses, identifying reachable services, revisiting important devices and keeping a usable inventory. The application brings these tasks into a desktop workflow. It combines ICMP, optional ARP/TCP checks and local camera discovery because any single discovery method can miss devices.

| Engineering area | Implementation evidence |
|---|---|
| Input validation | `IpRangeParser.cs`: IPv4 range/CIDR parsing, deduplication and address limits |
| Concurrency and responsiveness | `NetworkScanner.cs`, `ScanControl.cs`: bounded async work, cancellation and pause |
| Protocol handling | `OnvifDiscovery.cs`, `CameraDiscovery.cs`: response parsing and local discovery |
| Windows UI | XAML views and C# code-behind, collection views and filtering |
| Persistence | `AppStateStore.cs`: JSON state with temporary-file replacement |
| Video processing | `CameraStream.cs`, `LatestFrameSlot.cs`, `YoloDetector.cs` |
| Verification | xUnit tests and reproducible demo renders |
| Distribution | PowerShell publishing and Inno Setup packaging |

## Technical decisions to discuss

- Bounded concurrency prevents unlimited simultaneous probes, while cancellation allows the operator to stop work.
- Layer separation lets core parsing/discovery tests run without constructing the WPF UI.
- Multiple discovery methods improve coverage but cannot eliminate firewall, routing or firmware limitations.
- A latest-frame slot trades processing every frame for displaying more recent camera information.
- Synthetic demo data enables repeatable screenshots and interviews without exposing an office inventory.
- Code-behind keeps this desktop implementation straightforward; moving orchestration into view models would improve testability as it grows.

## Verified outcome

The prepared source builds with zero warnings/errors, and all 27 tests passed on 2026-11-24. Five labeled application screenshots are available. These are evidence of the build, tests and demo UI; the sample device counts, latency values and scan durations are not performance results.

The project owner reports using the applications for several months. This repository does not include an independently audited deployment history, customer count or measured business impact.

## Interview introduction

“Empiro IP Scanner is my Windows network-discovery project. The current application uses C#, .NET 8 and WPF, with XAML for the interface. It combines bounded asynchronous scanning with local camera discovery, searchable inventory, favorites and exports. I can demonstrate the workflow offline using fictional data, and explain the parser, cancellation flow, local persistence and testing approach.”

Use this introduction only for areas you can explain and demonstrate. When discussing personal contributions, describe your actual work and decisions; the repository itself does not establish who implemented each part.

## Resume project entry

**Empiro IP Scanner | C#, .NET 8, WPF, XAML, xUnit**

- Windows network inventory project featuring asynchronous IPv4 discovery, favorites, history and CSV/JSON/text exports.
- Includes ONVIF/SADP discovery, optional camera monitoring, a 27-test suite and a reproducible offline demonstration.

## Questions to prepare for

1. How do CIDR parsing, address limits and deduplication work?
2. How do cancellation and bounded concurrency affect responsiveness?
3. Why can a camera be discovered without being reachable for video?
4. What does a missing ping response actually mean?
5. How is demo data prevented from replacing live state?
6. Which behaviors are tested, and which need hardware validation?
7. What would you refactor before adding remote agents or multi-user storage?
