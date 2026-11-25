using Empiro.IpScanner.Core;
using Xunit;

namespace Empiro.IpScanner.Tests;

public sealed class DemoDataTests
{
    [Fact]
    public void DemoUsesUniqueDocumentationAddressesAndLocalMacs()
    {
        var devices = DemoData.CreateDevices();
        Assert.Equal(12, devices.Count);
        Assert.Equal(devices.Count, devices.Select(d => d.IpAddress).Distinct().Count());
        Assert.All(devices, d => { Assert.StartsWith("192.0.2.", d.IpAddress); Assert.StartsWith("02:", d.MacAddress); Assert.Empty(d.OnvifServiceUrl); });
        Assert.Equal(6, devices.Count(d => d.IsFavorite));
    }

    [Fact]
    public void DemoReloadDoesNotReuseEditedObjects()
    {
        var first = DemoData.CreateDevices(); first[0].Name = "Edited";
        Assert.Equal("Office Gateway", DemoData.CreateDevices()[0].Name);
        Assert.All(DemoData.CreateHistory(), h => Assert.Equal("192.0.2.0/24", h.IpRange));
    }
}
