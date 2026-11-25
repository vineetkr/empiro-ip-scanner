using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Empiro.IpScanner.Core;

public static class NetworkDetector
{
    public static bool IsOnLink(IPAddress target)
    {
        if (target.AddressFamily != AddressFamily.InterNetwork) return false;
        var targetBytes = target.GetAddressBytes();
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask is not null)
            .Any(unicast =>
            {
                var localBytes = unicast.Address.GetAddressBytes();
                var maskBytes = unicast.IPv4Mask.GetAddressBytes();
                return Enumerable.Range(0, 4).All(index => (targetBytes[index] & maskBytes[index]) == (localBytes[index] & maskBytes[index]));
            });
    }

    public static NetworkInformation? DetectLocalNetwork()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var properties = adapter.GetIPProperties();
            var unicast = properties.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address));
            if (unicast is null) continue;
            var mask = unicast.IPv4Mask;
            var prefix = mask.GetAddressBytes().Sum(b => System.Numerics.BitOperations.PopCount((uint)b));
            var ip = unicast.Address;
            var maskBytes = mask.GetAddressBytes();
            var networkBytes = ip.GetAddressBytes().Zip(maskBytes, (a, m) => (byte)(a & m)).ToArray();
            var network = new IPAddress(networkBytes);
            return new NetworkInformation(adapter.Name, ip.ToString(), mask.ToString(), $"{network}/{prefix}",
                string.Join(":", adapter.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))),
                properties.GatewayAddresses.Select(g => g.Address.ToString()).Where(s => s != "0.0.0.0").ToArray(),
                properties.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork).Select(d => d.ToString()).ToArray());
        }
        return null;
    }
}
