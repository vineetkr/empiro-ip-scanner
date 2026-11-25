using System.Net;
using Empiro.IpScanner.Core;
using Xunit;

namespace Empiro.IpScanner.Tests;

public sealed class CameraDiscoveryTests
{
    [Fact]
    public void SadpKeepsAdvertisedOtherSubnetAddressAndVendorDetails()
    {
        var result = CameraDiscovery.ParseSadpResponse("""
            <ProbeMatch><Uuid>test</Uuid><IPv4Address>192.168.29.20</IPv4Address>
            <DeviceDescription>Front camera</DeviceDescription><MAC>AA:BB:CC:11:22:33</MAC>
            <HttpPort>80</HttpPort><CommandPort>8000</CommandPort></ProbeMatch>
            """, "test");
        Assert.NotNull(result);
        Assert.Equal("192.168.29.20", result.IpAddress);
        Assert.Equal("Hikvision", result.Manufacturer);
        Assert.Equal("AA:BB:CC:11:22:33", result.MacAddress);
        Assert.Equal("80, 8000", result.Ports);
        Assert.Empty(result.OnvifServiceUrl); // SADP does not establish ONVIF support.
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("<Probe><IPv4Address>192.168.29.20</IPv4Address></Probe>")]
    [InlineData("<ProbeMatch><IPv4Address>0.0.0.0</IPv4Address></ProbeMatch>")]
    [InlineData("<ProbeMatch><IPv4Address>239.1.1.1</IPv4Address></ProbeMatch>")]
    [InlineData("<ProbeMatch><IPv4Address>::1</IPv4Address></ProbeMatch>")]
    [InlineData("<!DOCTYPE ProbeMatch [<!ENTITY x '192.168.29.20'>]><ProbeMatch><IPv4Address>&x;</IPv4Address></ProbeMatch>")]
    public void InvalidSadpPacketsAreIgnored(string xml) => Assert.Null(CameraDiscovery.ParseSadpResponse(xml));

    [Fact]
    public void UnrelatedSadpReplyIsIgnored() => Assert.Null(CameraDiscovery.ParseSadpResponse(
        "<ProbeMatch><Uuid>other</Uuid><IPv4Address>192.168.29.20</IPv4Address></ProbeMatch>", "expected"));

    [Fact]
    public void MergeRetainsOnvifEndpointAndAddsVendorMetadata()
    {
        var result = Assert.Single(CameraDiscovery.Merge(
            [new DeviceResult { IpAddress = "192.168.29.20", Name = "ONVIF camera", OnvifServiceUrl = "http://192.168.29.20/onvif/device_service" }],
            [new DeviceResult { IpAddress = "192.168.29.20", Name = "Front", Manufacturer = "Hikvision", MacAddress = "AA:BB:CC:11:22:33" }]));
        Assert.Equal("Front", result.Name);
        Assert.Equal("Hikvision", result.Manufacturer);
        Assert.Equal("http://192.168.29.20/onvif/device_service", result.OnvifServiceUrl);
    }

    [Fact]
    public void OnvifUsesAdvertisedAddressWhenSenderHasAnotherAddress()
    {
        var result = OnvifDiscovery.ParseResponse("""
            <ProbeMatches xmlns="http://schemas.xmlsoap.org/ws/2005/04/discovery"><ProbeMatch>
            <XAddrs>ftp://192.168.1.5/bad http://192.168.29.20:8080/onvif/device_service</XAddrs>
            </ProbeMatch></ProbeMatches>
            """, IPAddress.Parse("192.168.1.5"));
        Assert.NotNull(result);
        Assert.Equal("192.168.29.20", result.IpAddress);
        Assert.Equal("http://192.168.29.20:8080/onvif/device_service", result.OnvifServiceUrl);
    }

    [Fact]
    public async Task CancellationIsRespectedBeforeOpeningSockets()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CameraDiscovery.DiscoverAsync(TimeSpan.FromSeconds(2), cancellation.Token));
    }
}
