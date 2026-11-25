using System.Net;
using System.Runtime.InteropServices;

namespace Empiro.IpScanner.Core;

internal static class ArpResolver
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destinationIp, uint sourceIp, byte[] physicalAddress, ref int physicalAddressLength);

    public static async Task<string?> ResolveAsync(IPAddress address, CancellationToken cancellationToken)
    {
        if (!NetworkDetector.IsOnLink(address)) return null;
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var macBytes = new byte[8];
            var length = macBytes.Length;
            var destination = BitConverter.ToUInt32(address.GetAddressBytes());
            var result = SendARP(destination, 0, macBytes, ref length);
            cancellationToken.ThrowIfCancellationRequested();
            if (result != 0 || length is < 6 or > 8) return null;
            return string.Join(":", macBytes.Take(length).Select(value => value.ToString("X2")));
        }, cancellationToken).ConfigureAwait(false);
    }
}
