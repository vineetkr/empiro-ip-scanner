using Empiro.IpScanner.Core;
using System.Collections.Concurrent;
using Xunit;

namespace Empiro.IpScanner.Tests;

public sealed class IpRangeParserTests
{
    [Fact]
    public void ParsesSingleIpv4Address()
    {
        var addresses = IpRangeParser.Parse(" 192.168.1.12 ");
        Assert.Single(addresses);
        Assert.Equal("192.168.1.12", addresses[0].ToString());
    }

    [Fact]
    public void CidrOmitsNetworkAndBroadcastAddresses()
    {
        var addresses = IpRangeParser.Parse("192.168.1.0/24");
        Assert.Equal(254, addresses.Count);
        Assert.Equal("192.168.1.1", addresses[0].ToString());
        Assert.Equal("192.168.1.254", addresses[^1].ToString());
    }

    [Fact]
    public void Slash31IncludesBothUsableAddresses()
    {
        var addresses = IpRangeParser.Parse("10.0.0.4/31");
        Assert.Equal(new[] { "10.0.0.4", "10.0.0.5" }, addresses.Select(a => a.ToString()));
    }

    [Fact]
    public void ParsesExplicitRangeIncludingEndpoints()
    {
        var addresses = IpRangeParser.Parse("192.168.5.8 - 192.168.5.10");
        Assert.Equal(new[] { "192.168.5.8", "192.168.5.9", "192.168.5.10" }, addresses.Select(a => a.ToString()));
    }

    [Fact]
    public void ParsesAndDeduplicatesMultipleExplicitRanges()
    {
        var addresses = IpRangeParser.Parse("192.168.1.1-192.168.1.3; 192.168.1.3, 192.168.100.5/32");
        Assert.Equal(new[] { "192.168.1.1", "192.168.1.2", "192.168.1.3", "192.168.100.5" },
            addresses.Select(address => address.ToString()));
    }

    [Fact]
    public void RejectsMultiRangeWhenUniqueAddressLimitIsExceeded()
    {
        Assert.Throws<FormatException>(() => IpRangeParser.Parse("10.0.0.1-10.0.0.3;10.0.0.4-10.0.0.6", maximumAddresses: 5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("192.168.1.0/33")]
    [InlineData("2001:db8::1")]
    [InlineData("192.168.1.20-192.168.1.10")]
    [InlineData("0.0.0.0/0")]
    public void RejectsInvalidOrOversizedRanges(string input) => Assert.Throws<FormatException>(() => IpRangeParser.Parse(input));

    [Fact]
    public async Task ScansLoopbackAndReportsARealResult()
    {
        var updates = new InlineProgress<ScanProgress>();
        var scanner = new NetworkScanner();
        var options = new ScanOptions(1000, 2, 0, ResolveHostnames: false,
            ResolveMacAddresses: false, EnablePortScan: false, Ports: Array.Empty<int>(),
            EnableArpDiscovery: false, EnableCameraDiscovery: false);
        await scanner.ScanAsync(IpRangeParser.Parse("127.0.0.1/32"), options, new ScanControl(), updates,
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        var update = Assert.Single(updates.Items);
        Assert.Equal("127.0.0.1", update.Device.IpAddress);
        Assert.Equal("Online", update.Device.Status);
        Assert.Equal(1, update.Completed);
        Assert.Equal(1, update.Total);
    }

    [Fact]
    public void ParsesOnvifCameraAnnouncement()
    {
        const string xml = """
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope">
              <s:Body><ProbeMatches xmlns="http://schemas.xmlsoap.org/ws/2005/04/discovery">
                <ProbeMatch><XAddrs>http://192.168.100.5/onvif/device_service</XAddrs>
                  <Scopes>onvif://www.onvif.org/name/Front%20Door</Scopes>
                </ProbeMatch>
              </ProbeMatches></s:Body>
            </s:Envelope>
            """;

        var device = OnvifDiscovery.ParseResponse(xml, System.Net.IPAddress.Parse("192.168.100.5"));
        Assert.NotNull(device);
        Assert.Equal("192.168.100.5", device.IpAddress);
        Assert.Equal("Front Door", device.Name);
        Assert.Equal("Camera", device.Type);
        Assert.Equal("Online", device.Status);
    }

    [Fact]
    public void IgnoresMalformedOnvifResponses()
    {
        Assert.Null(OnvifDiscovery.ParseResponse("not xml", System.Net.IPAddress.Parse("192.168.1.5")));
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        public ConcurrentBag<T> Items { get; } = new();
        public void Report(T value) => Items.Add(value);
    }
}
