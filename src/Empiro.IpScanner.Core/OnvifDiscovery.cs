using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace Empiro.IpScanner.Core;

public static class OnvifDiscovery
{
    private static readonly IPEndPoint DiscoveryEndpoint = new(IPAddress.Parse("239.255.255.250"), 3702);
    private const string Probe = """
        <?xml version="1.0" encoding="UTF-8"?>
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:a="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery" xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
          <s:Header>
            <a:MessageID>urn:uuid:6f9619ff-8b86-d011-b42d-00cf4fc964ff</a:MessageID>
            <a:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To>
            <a:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action>
          </s:Header>
          <s:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></s:Body>
        </s:Envelope>
        """;

    public static async Task<IReadOnlyList<DeviceResult>> DiscoverAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var localAddresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any))
            .Distinct()
            .ToArray();
        if (localAddresses.Length == 0) return [];

        var clients = new List<UdpClient>();
        try
        {
            foreach (var localAddress in localAddresses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var client = new UdpClient(AddressFamily.InterNetwork);
                try
                {
                    client.Client.Bind(new IPEndPoint(localAddress, 0));
                    client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, localAddress.GetAddressBytes());
                    client.Ttl = 1;
                    clients.Add(client);
                    var probe = Probe.Replace("6f9619ff-8b86-d011-b42d-00cf4fc964ff", Guid.NewGuid().ToString(), StringComparison.Ordinal);
                    await client.SendAsync(Encoding.UTF8.GetBytes(probe), DiscoveryEndpoint, cancellationToken).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    client.Dispose();
                }
            }

            var found = new Dictionary<string, DeviceResult>(StringComparer.OrdinalIgnoreCase);
            var stopwatch = Stopwatch.StartNew();
            using var listenerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var pendingReceives = clients.Select(client => (Client: client, Receive: client.ReceiveAsync(listenerCancellation.Token).AsTask())).ToList();
            while (clients.Count > 0 && stopwatch.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = timeout - stopwatch.Elapsed;
                var anyReceive = Task.WhenAny(pendingReceives.Select(item => item.Receive));
                if (await Task.WhenAny(anyReceive, Task.Delay(remaining, cancellationToken)).ConfigureAwait(false) != anyReceive)
                    break;
                foreach (var pending in pendingReceives.Where(item => item.Receive.IsCompleted).ToArray())
                {
                    var index = pendingReceives.FindIndex(item => ReferenceEquals(item.Client, pending.Client));
                    if (pending.Receive.IsCompletedSuccessfully && pending.Receive.Result.Buffer.Length <= 65_536)
                    {
                        var packet = pending.Receive.Result;
                        var response = ParseResponse(Encoding.UTF8.GetString(packet.Buffer), packet.RemoteEndPoint.Address);
                        if (response is not null) found[response.IpAddress] = response;
                    }
                    if (index < 0) continue;
                    if (pending.Receive.IsCompletedSuccessfully)
                        pendingReceives[index] = (pending.Client, pending.Client.ReceiveAsync(listenerCancellation.Token).AsTask());
                    else
                    {
                        pendingReceives.RemoveAt(index);
                        clients.Remove(pending.Client);
                        pending.Client.Dispose();
                    }
                }
            }
            listenerCancellation.Cancel();
            try { await Task.WhenAll(pendingReceives.Select(item => item.Receive)).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            return found.Values.ToArray();
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
        }
    }

    public static DeviceResult? ParseResponse(string xml, IPAddress remoteAddress)
    {
        if (remoteAddress.AddressFamily != AddressFamily.InterNetwork) return null;
        try
        {
            if (xml.Length > 65_536) return null;
            using var reader = System.Xml.XmlReader.Create(new StringReader(xml), new System.Xml.XmlReaderSettings
            { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65_536 });
            var document = XDocument.Load(reader);
            if (!document.Descendants().Any(element => element.Name.LocalName == "ProbeMatch" &&
                element.Name.NamespaceName == "http://schemas.xmlsoap.org/ws/2005/04/discovery")) return null;
            var elements = document.Descendants().ToArray();
            var xaddrs = elements.FirstOrDefault(element => element.Name.LocalName == "XAddrs")?.Value;
            var scopes = elements.FirstOrDefault(element => element.Name.LocalName == "Scopes")?.Value ?? "";
            if (string.IsNullOrWhiteSpace(xaddrs) && string.IsNullOrWhiteSpace(scopes)) return null;

            var name = "ONVIF camera";
            foreach (var scope in scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var marker = "/name/";
                var index = scope.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;
                var candidate = Uri.UnescapeDataString(scope[(index + marker.Length)..].TrimEnd('/'));
                if (!string.IsNullOrWhiteSpace(candidate)) name = candidate;
                break;
            }
            var endpoints = (xaddrs ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
                .Where(uri => uri is not null && (uri.Scheme == "http" || uri.Scheme == "https") && string.IsNullOrEmpty(uri.UserInfo))
                .Cast<Uri>().ToArray();
            // The advertised address can differ from the packet sender (multi-homed cameras).
            var endpoint = endpoints.FirstOrDefault(uri => uri.Host == remoteAddress.ToString())
                ?? endpoints.FirstOrDefault(uri => IPAddress.TryParse(uri.Host, out var ip) &&
                    ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip) &&
                    !ip.Equals(IPAddress.Any) && ip.GetAddressBytes()[0] < 224);
            var advertisedIp = endpoint is not null && IPAddress.TryParse(endpoint.Host, out var parsed) ? parsed : remoteAddress;
            return new DeviceResult
            {
                IpAddress = advertisedIp.ToString(), Name = name, Status = "Online", Type = "Camera",
                OnvifServiceUrl = endpoint?.AbsoluteUri ?? "",
                ResponseTime = "ONVIF", Ports = string.IsNullOrWhiteSpace(xaddrs) ? "ONVIF" : "ONVIF service",
                LastSeen = DateTimeOffset.Now
            };
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
