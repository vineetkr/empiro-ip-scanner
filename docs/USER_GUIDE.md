# User guide and demonstration

## Run locally

Use Windows 10/11 x64 and a .NET 8 SDK compatible with `global.json`. From the repository root:

```powershell
dotnet restore .\Empiro.IpScanner.sln
dotnet build .\Empiro.IpScanner.sln --configuration Release
dotnet run --project .\src\Empiro.IpScanner.Wpf --configuration Release --no-build -- --demo
```

The demo is useful for interviews and offline presentations. It has 12 fictional devices, six favorites and five fictional history entries. Addresses use `192.0.2.0/24`; MAC addresses are synthetic locally administered addresses. Names, manufacturers, responses and timings are examples, not measurements or supported-vendor claims.

## Five-minute walkthrough

1. Show Results and explain the address, status, vendor, MAC, response and port columns.
2. Select Favorites to show a shorter list of frequently used devices.
3. Type `Camera` into Filter to display the three camera examples.
4. Open Actions → Scan history to explain previous ranges and result counts. Demo history is fabricated.
5. Open Settings and explain concurrency, timeout, retries and optional discovery checks.

The demo's reload button resets the inventory. Changes made during the demo do not overwrite saved live settings. Close the app and relaunch without `--demo` to use live mode.

## Live scanning

Choose Detect local network or enter an authorized IPv4 target, such as a single address, a start–end range, or a CIDR. Separate multiple ranges with semicolons. Select Scan; pause or stop when required. Filter results, add favorites, rename entries or attach comments from the context menu. Export from File → Save results as to CSV, JSON or text. Device actions require reachable devices and installed external tools where applicable.

The app reports observed responses. A missing device is not proof that an address is free, and a port number does not prove a device type. Manufacturer identification uses a limited offline OUI map.

## Camera monitor

In live mode, open Actions → Camera AI monitor. Discover cameras, provide credentials if required to resolve ONVIF media, or enter a documented RTSP URL. Supply a compatible ONNX detection model before using detection. Hardware, native dependencies, stream credentials and model compatibility need independent validation. The demo deliberately does not simulate a live video feed.

## Troubleshooting

- SDK not found: check `dotnet --list-sdks` and `global.json`; run commands from the repository root.
- Missing devices: inspect selected range, firewall, multicast filtering, network routing and device services.
- Camera found but inaccessible: discovery and routing are separate; verify network reachability.
- CUDA unavailable: verify compatible runtime dependencies; inference can fall back to CPU.
- Older installed app fails to launch: this repository's build result does not verify the installed binary. Rebuild and diagnose the matching executable rather than assuming it is identical.
