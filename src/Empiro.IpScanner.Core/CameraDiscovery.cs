using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Empiro.IpScanner.Core;

/// <summary>Local-link discovery, including devices configured for another IPv4 subnet.</summary>
public static class CameraDiscovery
{
    public static async Task<IReadOnlyList<DeviceResult>> DiscoverAsync(TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (timeout <= TimeSpan.Zero) return [];
        var onvif = OnvifDiscovery.DiscoverAsync(timeout, token);
        var sadp = DiscoverSadpAsync(timeout, token);
        await Task.WhenAll(onvif, sadp).ConfigureAwait(false);
        return Merge(await onvif, await sadp);
    }

    public static IReadOnlyList<DeviceResult> Merge(IEnumerable<DeviceResult> onvif, IEnumerable<DeviceResult> vendorDevices)
    {
        var devices = onvif.ToDictionary(d => d.IpAddress, StringComparer.OrdinalIgnoreCase);
        foreach (var vendor in vendorDevices)
        {
            if (!devices.TryGetValue(vendor.IpAddress, out var existing)) devices[vendor.IpAddress] = vendor;
            else
            {
                existing.Manufacturer = vendor.Manufacturer;
                if (vendor.MacAddress != "-") existing.MacAddress = vendor.MacAddress;
                if (existing.Name is "ONVIF camera" or "Unknown") existing.Name = vendor.Name;
                existing.ResponseTime = "ONVIF + SADP";
            }
        }
        return devices.Values.ToArray();
    }

    private static async Task<IReadOnlyList<DeviceResult>> DiscoverSadpAsync(TimeSpan timeout, CancellationToken token)
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
            .Distinct().ToArray();
        if (addresses.Length == 0) return [];
        var clients = new List<UdpClient>();
        var found = new ConcurrentDictionary<string, DeviceResult>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        var listeners = new List<Task>();
        var group = IPAddress.Parse("239.255.255.250");
        var uuid = Guid.NewGuid().ToString();
        var payload = Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"utf-8\"?><Probe><Uuid>{uuid}</Uuid><Types>inquiry</Types></Probe>");
        try
        {
            // Some firmware responds to the standard SADP port or multicast rather than the sender port.
            var listener = new UdpClient(AddressFamily.InterNetwork);
            try
            {
                listener.ExclusiveAddressUse = false;
                listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Client.Bind(new IPEndPoint(IPAddress.Any, 37020));
                foreach (var address in addresses)
                {
                    try { listener.JoinMulticastGroup(group, address); }
                    catch (SocketException) { }
                }
                clients.Add(listener);
                listeners.Add(ReceiveAsync(listener));
            }
            catch (SocketException) { listener.Dispose(); }

            foreach (var address in addresses)
            {
                var client = new UdpClient(AddressFamily.InterNetwork);
                try
                {
                    client.Client.Bind(new IPEndPoint(address, 0));
                    client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                    client.Ttl = 1;
                    clients.Add(client);
                    listeners.Add(ReceiveAsync(client));
                    await client.SendAsync(payload, new IPEndPoint(group, 37020), deadline.Token).ConfigureAwait(false);
                }
                catch (SocketException) { client.Dispose(); }
            }
            await Task.WhenAll(listeners).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        finally
        {
            deadline.Cancel();
            foreach (var client in clients) client.Dispose();
            await Task.WhenAll(listeners).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        return found.Values.ToArray();

        async Task ReceiveAsync(UdpClient client)
        {
            try
            {
                while (!deadline.IsCancellationRequested)
                {
                    var packet = await client.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                    var device = ParseSadpResponse(Encoding.UTF8.GetString(packet.Buffer), uuid);
                    if (device is not null) found[device.IpAddress] = device;
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }
    }

    public static DeviceResult? ParseSadpResponse(string xml, string? expectedUuid = null)
    {
        if (xml.Length > 65_536) return null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65_536 });
            var root = XDocument.Load(reader).Root;
            if (root?.Name.LocalName != "ProbeMatch") return null;
            string Field(string name) => root.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";
            if (expectedUuid is not null && !string.Equals(Field("Uuid"), expectedUuid, StringComparison.OrdinalIgnoreCase)) return null;
            if (!IPAddress.TryParse(Field("IPv4Address"), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork
                || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast) || IPAddress.IsLoopback(ip)
                || ip.GetAddressBytes()[0] >= 224) return null;
            var name = Field("DeviceDescription");
            if (string.IsNullOrWhiteSpace(name)) name = Field("DeviceType");
            var ports = new[] { Field("HttpPort"), Field("CommandPort") }
                .Where(p => int.TryParse(p, out var port) && port is > 0 and < 65536).Distinct();
            return new DeviceResult
            {
                IpAddress = ip.ToString(), Name = string.IsNullOrWhiteSpace(name) ? "Hikvision device" : name,
                Manufacturer = "Hikvision", MacAddress = string.IsNullOrWhiteSpace(Field("MAC")) ? "-" : Field("MAC"),
                Status = "Online", Type = "Video device", ResponseTime = "SADP",
                Ports = string.Join(", ", ports), LastSeen = DateTimeOffset.Now
            };
        }
        catch (XmlException) { return null; }
    }
}
