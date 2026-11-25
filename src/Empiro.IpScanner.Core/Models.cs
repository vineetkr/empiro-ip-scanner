using System.Net;

namespace Empiro.IpScanner.Core;

public enum DeviceStatus { Online, Offline, Error }

public sealed class DeviceResult
{
    public string Status { get; set; } = "Offline";
    public string Name { get; set; } = "Unknown";
    public string IpAddress { get; set; } = "";
    public string Manufacturer { get; set; } = "Unknown";
    public string MacAddress { get; set; } = "-";
    public string ResponseTime { get; set; } = "-";
    public string Ports { get; set; } = "-";
    public string Type { get; set; } = "Unknown";
    public string OnvifServiceUrl { get; set; } = "";
    public string Comment { get; set; } = "";
    public bool IsFavorite { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
    public string StatusGlyph => Status == "Online" ? "●" : Status == "Error" ? "!" : "●";
}

public sealed class AppSettings
{
    public int PingTimeoutMs { get; set; } = 800;
    public int ConcurrentWorkers { get; set; } = 32;
    public int RetryCount { get; set; } = 0;
    public bool ResolveHostnames { get; set; } = true;
    public bool ResolveMacAddresses { get; set; } = true;
    public bool EnableArpDiscovery { get; set; } = true;
    public bool EnableCameraDiscovery { get; set; } = true;
    public bool EnablePortScan { get; set; }
    public List<int> Ports { get; set; } = [21, 22, 23, 25, 53, 80, 110, 135, 139, 143, 443, 445, 554, 8554, 3389, 8080];
    public bool RememberLastRange { get; set; } = true;
    public bool ConfirmBeforeStop { get; set; }
    public string LastRange { get; set; } = "";
    public List<string> RangeHistory { get; set; } = [];
    public string Theme { get; set; } = "Light";
    public double WindowWidth { get; set; } = 1050;
    public double WindowHeight { get; set; } = 660;
}

public sealed class FavoriteDevice
{
    public string IpAddress { get; set; } = "";
    public string Name { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Comment { get; set; } = "";
    public string Type { get; set; } = "Unknown";
    public DateTimeOffset? LastSeen { get; set; }
}

public sealed class ScanHistoryItem
{
    public DateTimeOffset StartedAt { get; set; }
    public string IpRange { get; set; } = "";
    public int ScannedAddresses { get; set; }
    public int DevicesFound { get; set; }
    public int OnlineDevices { get; set; }
    public double DurationSeconds { get; set; }
}

public sealed class AppState
{
    public AppSettings Settings { get; set; } = new();
    public List<FavoriteDevice> Favorites { get; set; } = [];
    public List<ScanHistoryItem> History { get; set; } = [];
    public Dictionary<string, string> Comments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RenamedDevices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ScanOptions(int PingTimeoutMs, int ConcurrentWorkers, int RetryCount,
    bool ResolveHostnames, bool ResolveMacAddresses, bool EnablePortScan, IReadOnlyList<int> Ports,
    bool EnableArpDiscovery = true, bool EnableCameraDiscovery = true);

public sealed record ScanProgress(DeviceResult Device, int Completed, int Total);

public sealed record NetworkInformation(string AdapterName, string Ipv4Address, string SubnetMask,
    string CidrNetwork, string MacAddress, IReadOnlyList<string> Gateways, IReadOnlyList<string> DnsServers);
