using System.Net;
using System.Net.Sockets;

namespace Empiro.IpScanner.Core;

public static class IpRangeParser
{
    public const int MaximumAddresses = 4096;

    public static IReadOnlyList<IPAddress> Parse(string input, int maximumAddresses = MaximumAddresses)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new FormatException("Enter an IPv4 address, range, or CIDR network.");

        if (maximumAddresses < 1) throw new ArgumentOutOfRangeException(nameof(maximumAddresses));

        input = input.Trim();
        var ranges = input.Split([';', ','], StringSplitOptions.TrimEntries);
        if (ranges.Length > 1)
        {
            var unique = new Dictionary<uint, IPAddress>();
            foreach (var range in ranges)
            {
                if (range.Length == 0) throw new FormatException("Remove empty entries from the range list.");
                foreach (var address in ParseSingleRange(range, maximumAddresses))
                {
                    unique.TryAdd(ToUInt32(address), address);
                    if (unique.Count > maximumAddresses)
                        throw new FormatException($"That range list contains more than {maximumAddresses:N0} unique addresses.");
                }
            }
            return unique.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
        }

        return ParseSingleRange(input, maximumAddresses);
    }

    private static IReadOnlyList<IPAddress> ParseSingleRange(string input, int maximumAddresses)
    {
        if (input.Contains('/')) return ParseCidr(input, maximumAddresses);

        if (input.Contains('-'))
        {
            var parts = input.Split('-', 2, StringSplitOptions.TrimEntries);
            var start = ParseIpv4(parts[0]);
            var end = ParseIpv4(parts[1]);
            var first = ToUInt32(start);
            var last = ToUInt32(end);
            if (last < first) throw new FormatException("The ending IP must be greater than or equal to the starting IP.");
            var count = (long)last - first + 1;
            EnsureLimit(count, maximumAddresses);
            return Enumerable.Range(0, (int)count).Select(i => FromUInt32(first + (uint)i)).ToArray();
        }

        return [ParseIpv4(input)];
    }

    private static IReadOnlyList<IPAddress> ParseCidr(string input, int max)
    {
        var parts = input.Split('/', 2, StringSplitOptions.TrimEntries);
        var ip = ParseIpv4(parts[0]);
        if (!int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32)
            throw new FormatException("CIDR prefix must be between 0 and 32.");
        var address = ToUInt32(ip);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var network = address & mask;
        var broadcast = network | ~mask;
        var count = (long)broadcast - network + 1;
        EnsureLimit(count, max);
        var first = prefix < 31 ? network + 1 : network;
        var last = prefix < 31 ? broadcast - 1 : broadcast;
        return Enumerable.Range(0, checked((int)(last - first + 1)))
            .Select(i => FromUInt32(first + (uint)i)).ToArray();
    }

    private static IPAddress ParseIpv4(string value)
    {
        if (!IPAddress.TryParse(value, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            throw new FormatException($"'{value}' is not a valid IPv4 address.");
        return ip;
    }

    private static uint ToUInt32(IPAddress ip) => BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray());
    private static IPAddress FromUInt32(uint value) => new(BitConverter.GetBytes(value).Reverse().ToArray());

    private static void EnsureLimit(long count, int max)
    {
        if (count > max) throw new FormatException($"That range contains {count:N0} addresses. The limit is {max:N0} per scan.");
    }
}
