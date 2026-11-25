using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Empiro.IpScanner.Core;

public sealed class NetworkScanner
{
    private static readonly IReadOnlyDictionary<string, string> KnownManufacturers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["00000C"] = "Cisco Systems", ["000393"] = "Apple, Inc.", ["000CF1"] = "Samsung Electronics",
        ["001422"] = "Dell Inc.", ["0017A4"] = "Hewlett Packard", ["001302"] = "Intel Corporation"
    };

    private static readonly int[] CameraPorts = [554, 8554];

    public async Task ScanAsync(IReadOnlyList<IPAddress> addresses, ScanOptions options, ScanControl control,
        IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var completed = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(options.ConcurrentWorkers, 1, 64),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(addresses, parallelOptions, async (address, token) =>
        {
            await control.WaitIfPausedAsync(token).ConfigureAwait(false);
            var result = await ProbeAsync(address, options, token).ConfigureAwait(false);
            var count = Interlocked.Increment(ref completed);
            progress?.Report(new ScanProgress(result, count, addresses.Count));
        }).ConfigureAwait(false);

        if (!options.EnableCameraDiscovery) return;
        var cameras = await CameraDiscovery.DiscoverAsync(TimeSpan.FromMilliseconds(2200), cancellationToken).ConfigureAwait(false);
        foreach (var camera in cameras)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.ResolveMacAddresses && camera.MacAddress == "-" && NetworkDetector.IsOnLink(IPAddress.Parse(camera.IpAddress)))
                camera.MacAddress = await ArpResolver.ResolveAsync(IPAddress.Parse(camera.IpAddress), cancellationToken).ConfigureAwait(false) ?? "-";
            if (camera.Manufacturer == "Unknown") camera.Manufacturer = LookupManufacturer(camera.MacAddress);
            if (options.ResolveHostnames)
            {
                try
                {
                    var host = await Dns.GetHostEntryAsync(camera.IpAddress, cancellationToken)
                        .WaitAsync(TimeSpan.FromMilliseconds(900), cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(host.HostName)) camera.Name = host.HostName.TrimEnd('.');
                }
                catch (Exception ex) when (ex is SocketException or TimeoutException or OperationCanceledException)
                {
                    if (ex is OperationCanceledException) throw;
                }
            }
            progress?.Report(new ScanProgress(camera, addresses.Count, addresses.Count));
        }
    }

    private static async Task<DeviceResult> ProbeAsync(IPAddress address, ScanOptions options, CancellationToken token)
    {
        var ipText = address.ToString();
        var result = new DeviceResult { IpAddress = ipText, Name = ipText, Status = "Offline" };
        var ping = new Ping();
        try
        {
            PingReply? reply = null;
            for (var attempt = 0; attempt <= Math.Clamp(options.RetryCount, 0, 3); attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    reply = await ping.SendPingAsync(address, Math.Clamp(options.PingTimeoutMs, 200, 5000))
                        .WaitAsync(TimeSpan.FromMilliseconds(Math.Clamp(options.PingTimeoutMs, 200, 5000) + 500), token)
                        .ConfigureAwait(false);
                    if (reply.Status == IPStatus.Success) break;
                }
                catch (PingException) { }
                catch (TimeoutException) { }
            }

            var pingOnline = reply?.Status == IPStatus.Success;
            string? mac = null;
            if ((options.ResolveMacAddresses || options.EnableArpDiscovery) && NetworkDetector.IsOnLink(address))
                mac = await ArpResolver.ResolveAsync(address, token).ConfigureAwait(false);

            var portsToCheck = new List<int>();
            if (options.EnablePortScan) portsToCheck.AddRange(options.Ports);
            if (options.EnableCameraDiscovery) portsToCheck.AddRange(CameraPorts);
            IReadOnlyList<int> openPorts = portsToCheck.Count == 0
                ? Array.Empty<int>()
                : await ScanPortsAsync(address, portsToCheck, token).ConfigureAwait(false);
            var cameraDetected = openPorts.Any(port => CameraPorts.Contains(port));
            var tcpReachable = openPorts.Count > 0;
            var arpReachable = options.EnableArpDiscovery && mac is not null;
            if (!pingOnline && !arpReachable && !tcpReachable) return result;

            result.Status = "Online";
            result.ResponseTime = pingOnline ? $"{reply!.RoundtripTime} ms" : arpReachable ? "ARP" : "TCP";
            result.LastSeen = DateTimeOffset.Now;
            if (options.ResolveMacAddresses && mac is not null) result.MacAddress = mac;
            result.Manufacturer = LookupManufacturer(result.MacAddress);
            result.Type = cameraDetected ? "Camera" : tcpReachable ? "Network device" : "Unknown";
            result.Ports = openPorts.Count == 0 ? "-" : string.Join(", ", openPorts.Order());

            if (options.ResolveHostnames)
            {
                try
                {
                    var host = await Dns.GetHostEntryAsync(ipText, token).WaitAsync(TimeSpan.FromMilliseconds(900), token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(host.HostName)) result.Name = host.HostName.TrimEnd('.');
                }
                catch (Exception ex) when (ex is SocketException or TimeoutException or OperationCanceledException)
                {
                    if (ex is OperationCanceledException) throw;
                }
            }
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            result.Status = "Error";
            return result;
        }
        finally { ping.Dispose(); }
    }

    private static string LookupManufacturer(string mac)
    {
        var prefix = Regex.Replace(mac, "[^0-9A-Fa-f]", "").ToUpperInvariant();
        return prefix.Length >= 6 && KnownManufacturers.TryGetValue(prefix[..6], out var vendor) ? vendor : "Unknown";
    }

    private static async Task<IReadOnlyList<int>> ScanPortsAsync(IPAddress address, IReadOnlyList<int> ports, CancellationToken token)
    {
        var open = new ConcurrentBag<int>();
        await Parallel.ForEachAsync(ports.Distinct().Where(port => port is > 0 and < 65536),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (port, ct) =>
            {
                using var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(address, port, ct).AsTask().WaitAsync(TimeSpan.FromMilliseconds(400), ct).ConfigureAwait(false);
                    if (client.Connected) open.Add(port);
                }
                catch (Exception ex) when (ex is SocketException or TimeoutException or OperationCanceledException)
                {
                    if (ex is OperationCanceledException) throw;
                }
            }).ConfigureAwait(false);
        return open.Order().ToArray();
    }
}
